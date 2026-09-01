using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AdvertisingOverviewQueryTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Current_project_rows_are_aggregated_into_the_overview_contract()
    {
        var projectId = Guid.NewGuid();
        var tenant = Tenant(projectId);
        await using var db = postgres.CreateContext(tenant);
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var window = new AdvertisingOverviewWindow(
            new DateTime(2026, 8, 31, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));

        SeedOverviewRows(db, projectId, window);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var insights = await AdvertisingOverviewQuery.InsightsAsync(db, projectId, window, default);
        var conversions = await AdvertisingOverviewQuery.ConversionsAsync(db, projectId, window, default);
        var advertisements = await AdvertisingOverviewQuery.AdvertisementsAsync(db, projectId, default);

        Assert.Equal(12m, insights.Spend);
        Assert.Equal(120L, insights.Impressions);
        Assert.Equal(9L, insights.Clicks);
        Assert.Equal(2, insights.DaysLoaded);
        Assert.Equal(2, insights.Snapshots);
        Assert.Equal(17m, insights.AllTimeSpend);
        Assert.Equal(70m, conversions.Revenue);
        Assert.Equal(1, conversions.Leads);
        Assert.Equal(1, conversions.QualifiedLeads);
        Assert.Equal(1, conversions.Bookings);
        Assert.Equal(2, conversions.Purchases);
        Assert.Equal(1, advertisements.ActiveAds);
        Assert.Equal(3, advertisements.TotalAds);
        Assert.True(advertisements.HasDeliveringAd);
        Assert.Equal("current campaign", advertisements.CurrentCampaign?.Name);
    }

    [Fact]
    public async Task Project_without_activity_returns_zero_overview_metrics()
    {
        var projectId = Guid.NewGuid();
        await using var db = postgres.CreateContext(Tenant(projectId));
        await db.Database.MigrateAsync();
        var window = new AdvertisingOverviewWindow(DateTime.UtcNow.AddDays(-1), DateTime.UtcNow);

        var insights = await AdvertisingOverviewQuery.InsightsAsync(db, projectId, window, default);
        var conversions = await AdvertisingOverviewQuery.ConversionsAsync(db, projectId, window, default);
        var advertisements = await AdvertisingOverviewQuery.AdvertisementsAsync(db, projectId, default);

        Assert.Equal(0m, insights.Spend);
        Assert.Equal(0, insights.Snapshots);
        Assert.Equal(0m, conversions.Revenue);
        Assert.Equal(0, conversions.Leads);
        Assert.Equal(0, advertisements.TotalAds);
        Assert.Null(advertisements.CurrentCampaign);
    }

    [Fact]
    public async Task Failed_disable_command_is_reported_as_needing_attention()
    {
        var projectId = Guid.NewGuid();
        var disableRequestId = Guid.NewGuid();
        await using var db = postgres.CreateContext(Tenant(projectId));
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        db.AdvertisingExecutionCommands.AddRange(
            DisableCommand(projectId, disableRequestId, "campaign", CommandState.Succeeded),
            DisableCommand(projectId, disableRequestId, "ad", CommandState.Failed));
        await db.SaveChangesAsync();

        var commands = await AdvertisingOverviewQuery.DisableCommandsAsync(
            db, projectId, disableRequestId, default);

        Assert.NotNull(commands);
        Assert.Equal(2, commands.Total);
        Assert.Equal(1, commands.Succeeded);
        Assert.True(commands.NeedsAttention);
    }

    [Fact]
    public async Task Current_advertising_state_produces_ready_readiness()
    {
        var projectId = Guid.NewGuid();
        await using var db = postgres.CreateContext(Tenant(projectId));
        await db.Database.MigrateAsync();
        await using var transaction = await db.Database.BeginTransactionAsync();
        SeedReadyProject(db, projectId);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var service = new AdvertisingReadinessService(
            db,
            capabilities: null!,
            gateway: null!,
            vault: null!,
            new AdvertisingAuditService(db),
            new Modules.WhatsApp.Services.WhatsAppAccountService(db));

        var readiness = await service.GetAsync(projectId);

        Assert.True(readiness.Ready);
        Assert.Equal(7, readiness.Items.Count);
        Assert.All(readiness.Items, readinessItem => Assert.True(readinessItem.Ready));
    }

    private static void SeedOverviewRows(
        AppDbContext db,
        Guid projectId,
        AdvertisingOverviewWindow window)
    {
        db.AdvertisingInsights.AddRange(
            Insight(projectId, window.StartUtc.AddHours(2), 12m, 120, 9, true),
            Insight(projectId, window.StartUtc.AddDays(-1), 5m, 50, 4, true),
            Insight(projectId, window.StartUtc.AddHours(3), 999m, 999, 999, false));
        db.AdvertisingConversions.AddRange(
            Conversion(projectId, "lead", "Lead", window.StartUtc.AddHours(1), null),
            Conversion(projectId, "qualified", "QualifiedLead", window.StartUtc.AddHours(2), 20m),
            Conversion(projectId, "enrollment", "EnrollmentPaid", window.StartUtc.AddHours(3), 40m),
            Conversion(projectId, "purchase", "Purchase", window.StartUtc.AddHours(4), 10m),
            Conversion(projectId, "old", "Purchase", window.StartUtc.AddDays(-1), 500m));
        db.ManagedAdvertisements.AddRange(
            Advertisement(projectId, "old campaign", "PAUSED", window.StartUtc.AddDays(-1)),
            Advertisement(projectId, "current campaign", "active", window.StartUtc.AddHours(1)),
            Advertisement(projectId, "pending campaign", "PENDING", null));
    }

    private static InsightsSnapshot Insight(
        Guid projectId,
        DateTime intervalStartUtc,
        decimal spend,
        long impressions,
        long clicks,
        bool isCurrent) =>
        new()
        {
            ProjectId = projectId,
            TargetId = Guid.NewGuid(),
            IntervalStartUtc = intervalStartUtc,
            IntervalEndUtc = intervalStartUtc.AddHours(1),
            Spend = spend,
            Impressions = impressions,
            Clicks = clicks,
            FetchedAtUtc = intervalStartUtc.AddHours(2),
            IsCurrent = isCurrent
        };

    private static CanonicalConversion Conversion(
        Guid projectId,
        string canonicalKey,
        string eventType,
        DateTime occurredAtUtc,
        decimal? currentValue) =>
        new()
        {
            ProjectId = projectId,
            CanonicalKey = canonicalKey,
            EventType = eventType,
            OccurredAtUtc = occurredAtUtc,
            CurrentValue = currentValue
        };

    private static ManagedAdvertisement Advertisement(
        Guid projectId,
        string name,
        string effectiveStatus,
        DateTime? lastSyncedAtUtc) =>
        new()
        {
            ProjectId = projectId,
            PromotionId = Guid.NewGuid(),
            CreativeId = Guid.NewGuid(),
            Name = name,
            EffectiveStatus = effectiveStatus,
            LastSyncedAtUtc = lastSyncedAtUtc,
            CreatedAt = lastSyncedAtUtc ?? DateTime.UnixEpoch
        };

    private static ExecutionCommand DisableCommand(
        Guid projectId,
        Guid disableRequestId,
        string target,
        CommandState state) =>
        new()
        {
            ProjectId = projectId,
            DecisionId = Guid.NewGuid(),
            IdempotencyKey = $"disable:{disableRequestId:N}:{target}",
            CommandType = "PauseAd",
            RequestFingerprint = Guid.NewGuid().ToString("N"),
            State = state
        };

    private static void SeedReadyProject(AppDbContext db, Guid projectId)
    {
        var now = DateTime.UtcNow;
        var connection = new AdvertisingConnection
        {
            ProjectId = projectId,
            State = AdvertisingConnectionState.Ready
        };
        var destination = new AuthorizedWhatsAppDestination
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            WabaExternalId = $"waba-{projectId:N}",
            PhoneNumberExternalId = $"phone-{projectId:N}",
            DatasetExternalId = $"dataset-{projectId:N}",
            WhatsAppIntegrationMode = WhatsAppIntegrationMode.CloudApi,
            State = AuthorizedDestinationState.Eligible,
            LastValidatedAtUtc = now
        };
        var capability = new AdvertisingCapabilitySnapshot
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            DestinationId = destination.Id,
            OptimizationGoalsJson = "[\"CONVERSATIONS\"]",
            PlacementEligibilityJson = "{\"automatic\":true,\"whatsappDestinationEligible\":true}",
            CheckedAtUtc = now,
            ExpiresAtUtc = now.AddHours(1),
            State = AdvertisingCapabilityState.Healthy
        };
        destination.CapabilitySnapshotId = capability.Id;
        db.ProjectAdvertisingContextProjections.Add(new()
        {
            ProjectId = projectId,
            ReportingTimezoneIana = "Africa/Cairo"
        });
        db.AdvertisingConnections.Add(connection);
        db.AdvertisingWhatsAppDestinations.Add(destination);
        db.AdvertisingCapabilitySnapshots.Add(capability);
        db.AutonomyEnvelopes.Add(new()
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            DailyCap = 100m,
            PeriodCap = 1000m,
            Currency = "EGP",
            HardIncludedGeoJson = "[\"EG\"]",
            StartsAtUtc = now.AddDays(-1),
            EndsAtUtc = now.AddDays(1),
            State = EnvelopeState.Active
        });
        db.AdvertisingOffers.Add(new()
        {
            ProjectId = projectId,
            ProfileId = Guid.NewGuid(),
            Name = "Eligible offer",
            State = "Eligible"
        });
        db.AdvertisingTrackingHealthSnapshots.Add(new()
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            DestinationId = destination.Id,
            TrackingHealthPolicyId = Guid.NewGuid(),
            State = TrackingHealthState.Healthy,
            EvaluatedAtUtc = now
        });
    }

    private static TenantContext Tenant(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return tenant;
    }
}
