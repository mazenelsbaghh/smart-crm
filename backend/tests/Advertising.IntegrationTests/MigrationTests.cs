using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Advertising.Jobs;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class MigrationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Existing_database_upgrade_creates_the_v2_advertising_foundation()
    {
        await using var context = postgres.CreateContext();
        await context.Database.MigrateAsync();

        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();

        Assert.True(await TableExists(connection, "AdvertisingCapabilitySnapshots"));
        Assert.True(await TableExists(connection, "AdvertisingProviderOperations"));
        Assert.True(await TableExists(connection, "WhatsAppInboundRouteProjections"));
        Assert.True(await TableExists(connection, "WhatsAppAccounts"));
        Assert.True(await TableExists(connection, "WhatsAppPhoneCustomerIdentities"));
        Assert.True(await TableExists(connection, "ConversationReplyWindows"));
        Assert.Equal(5, await ScalarIntAsync(connection, """
            SELECT count(*)::integer
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND (table_name, column_name) IN (
                  ('GroupAppointments', 'WhatsAppAccountId'),
                  ('FollowUps', 'DependsOnFollowUpId'),
                  ('FollowUps', 'ActiveAutomationSlotKey'),
                  ('Conversations', 'WhatsAppDeliveryUnknownAt'),
                  ('Conversations', 'WhatsAppDeliveryUnknownKey'))
            """));
    }

    [Fact]
    public async Task Projection_backfill_is_resumable_and_idempotent_without_provider_calls()
    {
        await using var context = postgres.CreateContext();
        await context.Database.MigrateAsync();
        var job = new AdvertisingProjectionBackfillJob(context, NullLogger<AdvertisingProjectionBackfillJob>.Instance);

        await job.RunAsync();
        await job.RunAsync();

        var run = Assert.Single(await context.AdvertisingProjectionBackfillRuns.AsNoTracking().ToListAsync());
        Assert.Equal("Completed", run.State);
        Assert.Null(run.LastFailureCode);
    }

    [Fact]
    public async Task Canonical_phone_upgrade_backfills_legacy_rows_and_creates_generated_indexed_columns()
    {
        const string previousMigration = "20260825184117_AddContentWeeklyPlans";
        const string canonicalMigration = "20260827120000_AddIndexedGroupBookingCanonicalPhones";
        var projectId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();

        await using var isolatedDatabase = await postgres.CreateIsolatedDatabaseAsync();
        await using var context = isolatedDatabase.CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);

        await using (var connection = new NpgsqlConnection(isolatedDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT INTO "GroupAppointments"
                    ("Id", "ProjectId", "Name", "DateTime", "Capacity", "IsActive", "Days", "Mode",
                     "InstructorName", "CreatedAt", "UpdatedAt")
                VALUES (@group_id, @project_id, 'Legacy group', @group_time, 2, TRUE, '', 'offline', '', @now, @now);

                INSERT INTO "Customers"
                    ("Id", "ProjectId", "PhoneNumber", "Name", "City", "LeadScore", "Tags", "Notes",
                     "Interests", "IsBlacklisted", "PurchaseProbability", "CreatedAt", "UpdatedAt")
                VALUES (@customer_id, @project_id, '٠١٠ ١٢٣٤ ٥٦٧٨', 'Legacy customer', '', 10,
                        ARRAY[]::text[], '', ARRAY[]::text[], FALSE, 0, @now, @now);

                INSERT INTO "GroupAppointmentBookings"
                    ("Id", "ProjectId", "GroupAppointmentId", "CustomerId", "CustomerName", "CustomerPhone",
                     "IsAttended", "IsPaid", "CreatedAt", "UpdatedAt")
                VALUES (@booking_id, @project_id, @group_id, @customer_id, 'Legacy customer',
                        '٠١٠ (١٢٣٤) ٥٦٧٨', FALSE, FALSE, @now, @now);
                """;
            var now = DateTime.UtcNow;
            seed.Parameters.AddWithValue("group_id", groupId);
            seed.Parameters.AddWithValue("project_id", projectId);
            seed.Parameters.AddWithValue("group_time", now.AddDays(2));
            seed.Parameters.AddWithValue("customer_id", customerId);
            seed.Parameters.AddWithValue("booking_id", bookingId);
            seed.Parameters.AddWithValue("now", now);
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(canonicalMigration);

        await using var verification = new NpgsqlConnection(isolatedDatabase.ConnectionString);
        await verification.OpenAsync();
        Assert.Equal("201012345678", await ScalarStringAsync(
            verification,
            "SELECT \"PhoneNumberCanonical\" FROM \"Customers\" WHERE \"Id\" = @id",
            customerId));
        Assert.Equal("201012345678", await ScalarStringAsync(
            verification,
            "SELECT \"CustomerPhoneCanonical\" FROM \"GroupAppointmentBookings\" WHERE \"Id\" = @id",
            bookingId));
        Assert.Equal(2, await ScalarIntAsync(verification, """
            SELECT count(*)::integer
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND is_generated = 'ALWAYS'
              AND (table_name, column_name) IN (
                  ('Customers', 'PhoneNumberCanonical'),
                  ('GroupAppointmentBookings', 'CustomerPhoneCanonical'))
            """));
        await AssertCanonicalIndexAsync(
            verification,
            "Customers",
            "IX_Customers_ProjectId_PhoneNumberCanonical",
            "PhoneNumberCanonical");
        await AssertCanonicalIndexAsync(
            verification,
            "GroupAppointmentBookings",
            "IX_GroupAppointmentBookings_ProjectId_CustomerPhoneCanonical",
            "CustomerPhoneCanonical");
    }

    [Fact]
    public async Task Multi_WhatsApp_upgrade_preserves_the_legacy_account_and_scopes_existing_routes()
    {
        const string previousMigration = "20260831023403_AddAdvertisingOverviewQueryIndexes";
        const string multiAccountMigration = "20260831224329_AddMultiWhatsAppAccounts";
        var projectId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var messengerConversationId = Guid.NewGuid();
        var followUpId = Guid.NewGuid();
        var messengerFollowUpId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var baileysDestinationId = Guid.NewGuid();
        var cloudDestinationId = Guid.NewGuid();
        const string legacyLid = "123456789012345@lid";

        await using var isolatedDatabase = await postgres.CreateIsolatedDatabaseAsync();
        await using var context = isolatedDatabase.CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);

        await using (var connection = new NpgsqlConnection(isolatedDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT INTO "Projects" ("Id", "Name", "CreatedAt", "UpdatedAt")
                VALUES (@project_id, 'Legacy multi-account project', @now, @now);

                INSERT INTO "Customers"
                    ("Id", "ProjectId", "PhoneNumber", "WhatsAppLid", "Name", "City", "LeadScore",
                     "Tags", "Notes", "Interests", "IsBlacklisted", "PurchaseProbability", "CreatedAt", "UpdatedAt")
                VALUES
                    (@customer_id, @project_id, '201000000001', @legacy_lid, 'Legacy customer', '', 0,
                     ARRAY[]::text[], '', ARRAY[]::text[], FALSE, 0, @now, @now);

                INSERT INTO "Conversations"
                    ("Id", "ProjectId", "CustomerId", "Status", "Channel", "LastMessageTimestamp", "CreatedAt", "UpdatedAt")
                VALUES
                    (@conversation_id, @project_id, @customer_id, 'Open', 'WhatsApp', @now, @now, @now),
                    (@messenger_conversation_id, @project_id, @customer_id, 'Open', 'Messenger', @now, @now, @now);

                INSERT INTO "FollowUps"
                    ("Id", "ProjectId", "CustomerId", "ConversationId", "Channel", "DueDate", "Status",
                     "Notes", "Type", "AppointmentTime", "Tone", "CreatedAt", "UpdatedAt")
                VALUES
                    (@follow_up_id, @project_id, @customer_id, @conversation_id, NULL, @due_date, 'Pending',
                     'Legacy WhatsApp follow-up', 'Nurturing', NULL, 'Default', @now, @now),
                    (@messenger_follow_up_id, @project_id, @customer_id, @messenger_conversation_id, NULL, @due_date, 'Pending',
                     'Legacy Messenger follow-up', 'Nurturing', NULL, 'Default', @now, @now);

                INSERT INTO "Campaigns"
                    ("Id", "ProjectId", "Name", "SegmentId", "MessageTemplateA", "MessageTemplateB", "Status",
                     "SentCount", "DeliveredCount", "ReadCount", "ResponseCount", "CreatedAt", "UpdatedAt")
                VALUES
                    (@campaign_id, @project_id, 'Legacy campaign', @segment_id, 'Template A', 'Template B', 0,
                     0, 0, 0, 0, @now, @now);

                INSERT INTO "AdvertisingWhatsAppDestinations"
                    ("Id", "ProjectId", "ConnectionId", "Provider", "WabaExternalId", "PhoneNumberExternalId",
                     "PageExternalId", "DatasetExternalId", "ReceivingIdentityExternalId", "WhatsAppIntegrationMode",
                     "MessagingState", "AdvertisingState", "BusinessEventsState", "ReferralCaptureState", "State",
                     "Version", "ConcurrencyToken", "CreatedAt", "UpdatedAt")
                VALUES
                    (@baileys_destination_id, @project_id, @connection_id, 'Baileys', 'legacy-waba', 'legacy-phone',
                     '', '', 'legacy-phone', 2, 'Connected', 'Ready', 'Ready', 0, 1, 1, 0, @now, @now),
                    (@cloud_destination_id, @project_id, @connection_id, 'MetaWhatsApp', 'cloud-waba', 'cloud-phone',
                     '', '', 'cloud-phone', 0, 'Connected', 'Ready', 'Ready', 0, 1, 1, 0, @now, @now);
                """;
            var now = DateTime.UtcNow;
            seed.Parameters.AddWithValue("project_id", projectId);
            seed.Parameters.AddWithValue("customer_id", customerId);
            seed.Parameters.AddWithValue("conversation_id", conversationId);
            seed.Parameters.AddWithValue("messenger_conversation_id", messengerConversationId);
            seed.Parameters.AddWithValue("follow_up_id", followUpId);
            seed.Parameters.AddWithValue("messenger_follow_up_id", messengerFollowUpId);
            seed.Parameters.AddWithValue("campaign_id", campaignId);
            seed.Parameters.AddWithValue("segment_id", Guid.NewGuid());
            seed.Parameters.AddWithValue("baileys_destination_id", baileysDestinationId);
            seed.Parameters.AddWithValue("cloud_destination_id", cloudDestinationId);
            seed.Parameters.AddWithValue("connection_id", Guid.NewGuid());
            seed.Parameters.AddWithValue("legacy_lid", legacyLid);
            seed.Parameters.AddWithValue("due_date", now.AddDays(1));
            seed.Parameters.AddWithValue("now", now);
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(multiAccountMigration);

        await using var verification = new NpgsqlConnection(isolatedDatabase.ConnectionString);
        await verification.OpenAsync();
        Assert.True(await ScalarBoolAsync(
            verification,
            "SELECT \"IsDefault\" FROM \"WhatsAppAccounts\" WHERE \"Id\" = @id",
            projectId));
        Assert.Equal(projectId, await ScalarGuidAsync(
            verification,
            "SELECT \"ProjectId\" FROM \"WhatsAppAccounts\" WHERE \"Id\" = @id",
            projectId));
        Assert.Equal(projectId, await ScalarGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"Conversations\" WHERE \"Id\" = @id",
            conversationId));
        Assert.Null(await ScalarNullableGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"Conversations\" WHERE \"Id\" = @id",
            messengerConversationId));
        Assert.Equal(projectId, await ScalarGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"FollowUps\" WHERE \"Id\" = @id",
            followUpId));
        Assert.Equal(conversationId, await ScalarGuidAsync(
            verification,
            "SELECT \"ConversationId\" FROM \"FollowUps\" WHERE \"Id\" = @id",
            followUpId));
        Assert.Equal("WhatsApp", await ScalarStringAsync(
            verification,
            "SELECT \"Channel\" FROM \"FollowUps\" WHERE \"Id\" = @id",
            followUpId));
        Assert.Null(await ScalarNullableGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"FollowUps\" WHERE \"Id\" = @id",
            messengerFollowUpId));
        Assert.Equal("Messenger", await ScalarStringAsync(
            verification,
            "SELECT \"Channel\" FROM \"FollowUps\" WHERE \"Id\" = @id",
            messengerFollowUpId));
        Assert.Equal(projectId, await ScalarGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"Campaigns\" WHERE \"Id\" = @id",
            campaignId));
        Assert.Equal(projectId, await ScalarGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"AdvertisingWhatsAppDestinations\" WHERE \"Id\" = @id",
            baileysDestinationId));
        Assert.Null(await ScalarNullableGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"AdvertisingWhatsAppDestinations\" WHERE \"Id\" = @id",
            cloudDestinationId));
        Assert.Equal(legacyLid, await ScalarStringAsync(
            verification,
            "SELECT \"ExternalId\" FROM \"WhatsAppCustomerIdentities\" WHERE \"CustomerId\" = @id",
            customerId));
        Assert.Equal("Lid", await ScalarStringAsync(
            verification,
            "SELECT \"Kind\" FROM \"WhatsAppCustomerIdentities\" WHERE \"CustomerId\" = @id",
            customerId));
        Assert.Equal(projectId, await ScalarGuidAsync(
            verification,
            "SELECT \"WhatsAppAccountId\" FROM \"WhatsAppCustomerIdentities\" WHERE \"CustomerId\" = @id",
            customerId));
        Assert.Equal(5, await ScalarIntAsync(verification, """
            SELECT count(*)::integer
            FROM pg_constraint
            WHERE contype = 'f'
              AND confrelid = '"WhatsAppAccounts"'::regclass
            """));
        Assert.Equal(1, await ScalarIntAsync(verification, """
            SELECT count(*)::integer
            FROM pg_constraint
            WHERE contype = 'f'
              AND conrelid = '"WhatsAppCustomerIdentities"'::regclass
              AND confrelid = '"Customers"'::regclass
            """));
    }

    [Fact]
    public async Task Campaign_recipient_hardening_keeps_the_safest_legacy_row_and_enforces_uniqueness()
    {
        const string previousMigration = "20260831224329_AddMultiWhatsAppAccounts";
        const string hardeningMigration = "20260831232531_HardenMultiWhatsAppDelivery";
        var projectId = Guid.NewGuid();
        var campaignId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await using var isolatedDatabase = await postgres.CreateIsolatedDatabaseAsync();
        await using var context = isolatedDatabase.CreateContext();
        var migrator = context.Database.GetService<IMigrator>();
        await migrator.MigrateAsync(previousMigration);

        await using (var connection = new NpgsqlConnection(isolatedDatabase.ConnectionString))
        {
            await connection.OpenAsync();
            await using var seed = connection.CreateCommand();
            seed.CommandText = """
                INSERT INTO "CampaignRecipients"
                    ("Id", "ProjectId", "CampaignId", "CustomerId", "Variant", "Status",
                     "ErrorMessage", "CreatedAt", "UpdatedAt")
                VALUES
                    (@pending_id, @project_id, @campaign_id, @customer_id, 'A', 0, '', @older, @older),
                    (@sent_id, @project_id, @campaign_id, @customer_id, 'A', 1, '', @newer, @newer);
                """;
            var now = DateTime.UtcNow;
            seed.Parameters.AddWithValue("pending_id", Guid.NewGuid());
            seed.Parameters.AddWithValue("sent_id", Guid.NewGuid());
            seed.Parameters.AddWithValue("project_id", projectId);
            seed.Parameters.AddWithValue("campaign_id", campaignId);
            seed.Parameters.AddWithValue("customer_id", customerId);
            seed.Parameters.AddWithValue("older", now.AddMinutes(-5));
            seed.Parameters.AddWithValue("newer", now);
            await seed.ExecuteNonQueryAsync();
        }

        await migrator.MigrateAsync(hardeningMigration);

        await using var verification = new NpgsqlConnection(isolatedDatabase.ConnectionString);
        await verification.OpenAsync();
        await using (var retained = verification.CreateCommand())
        {
            retained.CommandText = """
                SELECT count(*)::integer, max("Status")::integer
                FROM "CampaignRecipients"
                WHERE "CampaignId" = @campaign_id AND "CustomerId" = @customer_id;
                """;
            retained.Parameters.AddWithValue("campaign_id", campaignId);
            retained.Parameters.AddWithValue("customer_id", customerId);
            await using var reader = await retained.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal(1, reader.GetInt32(0));
            Assert.Equal(1, reader.GetInt32(1));
        }

        await using var duplicate = verification.CreateCommand();
        duplicate.CommandText = """
            INSERT INTO "CampaignRecipients"
                ("Id", "ProjectId", "CampaignId", "CustomerId", "Variant", "Status",
                 "ErrorMessage", "CreatedAt", "UpdatedAt")
            VALUES (@id, @project_id, @campaign_id, @customer_id, 'A', 0, '', @now, @now);
            """;
        duplicate.Parameters.AddWithValue("id", Guid.NewGuid());
        duplicate.Parameters.AddWithValue("project_id", projectId);
        duplicate.Parameters.AddWithValue("campaign_id", campaignId);
        duplicate.Parameters.AddWithValue("customer_id", customerId);
        duplicate.Parameters.AddWithValue("now", DateTime.UtcNow);
        var exception = await Assert.ThrowsAsync<PostgresException>(() => duplicate.ExecuteNonQueryAsync());
        Assert.Equal(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    private static async Task<bool> TableExists(NpgsqlConnection connection, string tableName)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'public' AND table_name = @table_name
            );
            """;
        command.Parameters.AddWithValue("table_name", tableName);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<string?> ScalarStringAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        return (string?)await command.ExecuteScalarAsync();
    }

    private static async Task<Guid> ScalarGuidAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        return (Guid)(await command.ExecuteScalarAsync() ?? Guid.Empty);
    }

    private static async Task<Guid?> ScalarNullableGuidAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? null : (Guid)value;
    }

    private static async Task<bool> ScalarBoolAsync(NpgsqlConnection connection, string sql, Guid id)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("id", id);
        return (bool)(await command.ExecuteScalarAsync() ?? false);
    }

    private static async Task<int> ScalarIntAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (int)(await command.ExecuteScalarAsync() ?? 0);
    }

    private static async Task AssertCanonicalIndexAsync(
        NpgsqlConnection connection,
        string tableName,
        string indexName,
        string canonicalColumn)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT pg_get_indexdef(index_catalog.oid)
            FROM pg_class AS index_catalog
            INNER JOIN pg_index AS index_metadata ON index_metadata.indexrelid = index_catalog.oid
            INNER JOIN pg_class AS table_catalog ON table_catalog.oid = index_metadata.indrelid
            INNER JOIN pg_namespace AS schema_catalog ON schema_catalog.oid = table_catalog.relnamespace
            WHERE schema_catalog.nspname = 'public'
              AND table_catalog.relname = @table_name
              AND index_catalog.relname = @index_name;
            """;
        command.Parameters.AddWithValue("table_name", tableName);
        command.Parameters.AddWithValue("index_name", indexName);
        var definition = Assert.IsType<string>(await command.ExecuteScalarAsync());

        Assert.Contains($"(\"ProjectId\", \"{canonicalColumn}\")", definition, StringComparison.Ordinal);
        Assert.Contains($"WHERE (\"{canonicalColumn}\" IS NOT NULL)", definition, StringComparison.Ordinal);
    }
}
