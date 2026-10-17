using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Modules.Advertising.Services;
using Modules.Conversations.Domain;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Workers;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class BookingReadReceiptTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(GroupBookingOrigin.Manual)]
    [InlineData(GroupBookingOrigin.Public)]
    [InlineData(GroupBookingOrigin.Ai)]
    public async Task Successful_booking_reads_only_the_last_incoming_message_captured_at_commit(GroupBookingOrigin origin)
    {
        var scenario = await SeedAsync();
        var request = await BookAndReadRequestAsync(scenario, origin);
        Assert.Equal(scenario.LastIncoming.Id, request.MessageId);
        await using (var writer = postgres.CreateContext())
        {
            writer.Messages.Add(new Message { ConversationId = scenario.Conversation.Id, Direction = "Incoming",
                Content = "after booking", MessageType = "Text", ExternalMessageId = "after-booking",
                WhatsAppRemoteJid = "123456789012345@lid", Timestamp = DateTime.UtcNow.AddMinutes(1) });
            await writer.SaveChangesAsync();
        }
        await using var db = postgres.CreateContext();
        Assert.True(await db.GroupAppointmentBookings.IgnoreQueryFilters().AnyAsync(booking => booking.Id == request.BookingId));
        var gateway = new ReceiptGateway();
        var job = ReadJob(db, gateway);

        await job.ExecuteAsync(request);
        await job.ExecuteAsync(request);

        var sent = Assert.Single(gateway.ReadRequests);
        Assert.Equal(request.ProjectId, sent.GetProperty("projectId").GetGuid());
        Assert.Equal(scenario.Conversation.WhatsAppAccountId, sent.GetProperty("whatsappAccountId").GetGuid());
        Assert.Equal("last-before-booking", sent.GetProperty("messageId").GetString());
        Assert.Equal("123456789012345@lid", sent.GetProperty("remoteJid").GetString());
        Assert.False(string.IsNullOrEmpty(sent.GetProperty("expectedConnectedAt").GetString()));
        Assert.Equal("Processed", (await db.IntegrationInboxReceipts.IgnoreQueryFilters()
            .SingleAsync(receipt => receipt.EventId == request.Id)).State);
    }

    [Fact]
    public async Task Messages_with_the_same_provider_second_are_ordered_by_actual_receipt_not_random_ids()
    {
        var lastId = Guid.Parse("00000000-" + Guid.NewGuid().ToString()[9..]);
        var olderId = Guid.Parse("ffffffff-" + Guid.NewGuid().ToString()[9..]);
        var scenario = await SeedAsync(lastId, olderId);
        var received = DateTime.UtcNow;
        await using (var db = postgres.CreateContext())
        {
            var old = await db.Messages.SingleAsync(m => m.ConversationId == scenario.Conversation.Id && m.ExternalMessageId == "older");
            old.Timestamp = scenario.LastIncoming.Timestamp;
            old.ReceivedAtUtc = received.AddMilliseconds(-1);
            await db.Messages.Where(m => m.Id == scenario.LastIncoming.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(m => m.ReceivedAtUtc, received));
            await db.SaveChangesAsync();
        }
        var request = await BookAndReadRequestAsync(scenario, GroupBookingOrigin.Manual);
        Assert.Equal(scenario.LastIncoming.Id, request.MessageId);
    }

    [Fact]
    public async Task Failed_or_duplicate_booking_does_not_queue_a_new_read_receipt()
    {
        var scenario = await SeedAsync();
        await using var db = Context(scenario.ProjectId);
        var command = BookingCommand(scenario, GroupBookingOrigin.Manual);
        var group = await db.GroupAppointments.SingleAsync(group => group.Id == scenario.Group.Id);
        group.Capacity = 0;
        await db.SaveChangesAsync();
        Assert.Equal(GroupBookingStatus.GroupFull, (await new GroupBookingCoordinator(db).BookAsync(command)).Status);
        Assert.Empty(await ReadRequestsAsync(db, scenario.ProjectId));
        group.Capacity = 5;
        await db.SaveChangesAsync();
        Assert.Equal(GroupBookingStatus.Created, (await new GroupBookingCoordinator(db).BookAsync(command)).Status);
        Assert.Equal(GroupBookingStatus.BookingAlreadyExists, (await new GroupBookingCoordinator(db).BookAsync(command)).Status);
        Assert.Single(await ReadRequestsAsync(db, scenario.ProjectId));
    }

    [Fact]
    public async Task Offline_account_keeps_the_read_pending_until_the_same_account_reconnects()
    {
        var scenario = await SeedAsync();
        var request = await BookAndReadRequestAsync(scenario, GroupBookingOrigin.Manual);
        var gateway = new ReceiptGateway { Connected = false };
        await using (var offline = postgres.CreateContext())
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => ReadJob(offline, gateway).ExecuteAsync(request));
            Assert.Empty(gateway.ReadRequests);
            Assert.False(await offline.IntegrationInboxReceipts.IgnoreQueryFilters().AnyAsync(r => r.EventId == request.Id));
        }
        gateway.Connected = true;
        await using var online = postgres.CreateContext();
        await ReadJob(online, gateway).ExecuteAsync(request);
        Assert.Equal("last-before-booking", Assert.Single(gateway.ReadRequests).GetProperty("messageId").GetString());
    }

    [Theory]
    [InlineData(null, "201012345678@s.whatsapp.net")]
    [InlineData("543210987654321@lid", "543210987654321@lid")]
    public async Task Messages_received_before_the_upgrade_use_the_existing_customer_identity(string? lid, string expectedJid)
    {
        var scenario = await SeedAsync();
        await using (var legacy = postgres.CreateContext())
        {
            await legacy.Messages.Where(m => m.Id == scenario.LastIncoming.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(m => m.WhatsAppRemoteJid, (string?)null));
            await legacy.Customers.IgnoreQueryFilters().Where(c => c.Id == scenario.Customer.Id)
                .ExecuteUpdateAsync(update => update.SetProperty(c => c.WhatsAppLid, lid));
        }
        var request = await BookAndReadRequestAsync(scenario, GroupBookingOrigin.Manual);
        var gateway = new ReceiptGateway();
        await using var db = postgres.CreateContext();
        await ReadJob(db, gateway).ExecuteAsync(request);
        Assert.Equal(expectedJid, Assert.Single(gateway.ReadRequests).GetProperty("remoteJid").GetString());
    }

    [Fact]
    public async Task Provider_failure_is_retryable_and_never_records_a_successful_read_early()
    {
        var scenario = await SeedAsync();
        var request = await BookAndReadRequestAsync(scenario, GroupBookingOrigin.Manual);
        var gateway = new ReceiptGateway { FailNextRead = true };
        await using (var failed = postgres.CreateContext())
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => ReadJob(failed, gateway).ExecuteAsync(request));
            Assert.False(await failed.IntegrationInboxReceipts.IgnoreQueryFilters().AnyAsync(r => r.EventId == request.Id));
        }
        await using var retry = postgres.CreateContext();
        await ReadJob(retry, gateway).ExecuteAsync(request);
        Assert.Equal(2, gateway.ReadRequests.Count);
        Assert.All(gateway.ReadRequests, sent => Assert.Equal("last-before-booking", sent.GetProperty("messageId").GetString()));
        Assert.Equal("Processed", (await retry.IntegrationInboxReceipts.IgnoreQueryFilters().SingleAsync(r => r.EventId == request.Id)).State);
    }

    [Theory]
    [InlineData("project")]
    [InlineData("customer")]
    [InlineData("message")]
    [InlineData("deleted-booking")]
    public async Task Invalid_booking_message_scope_never_reaches_the_provider(string mismatch)
    {
        var scenario = await SeedAsync();
        var original = await BookAndReadRequestAsync(scenario, GroupBookingOrigin.Manual);
        await using var db = postgres.CreateContext();
        if (mismatch == "deleted-booking")
        {
            await db.GroupAppointmentBookings.IgnoreQueryFilters().Where(b => b.Id == original.BookingId).ExecuteDeleteAsync();
        }
        var request = new BookingLastMessageReadRequested {
            ProjectId = mismatch == "project" ? Guid.NewGuid() : original.ProjectId,
            CustomerId = mismatch == "customer" ? Guid.NewGuid() : original.CustomerId,
            MessageId = mismatch == "message" ? Guid.NewGuid() : original.MessageId,
            BookingId = original.BookingId, ConversationId = original.ConversationId
        };
        var gateway = new ReceiptGateway();
        await ReadJob(db, gateway).ExecuteAsync(request);
        Assert.Empty(gateway.ReadRequests);
        Assert.Equal("Skipped", (await db.IntegrationInboxReceipts.IgnoreQueryFilters().SingleAsync(r => r.EventId == request.Id)).State);
    }

    [Fact]
    public async Task Cancelled_booking_does_not_prevent_a_later_confirmed_booking_from_reading_the_message()
    {
        var scenario = await SeedAsync();
        var cancelled = await BookAndReadRequestAsync(scenario, GroupBookingOrigin.Manual);
        var gateway = new ReceiptGateway();
        await using (var db = postgres.CreateContext())
        {
            await db.GroupAppointmentBookings.IgnoreQueryFilters().Where(b => b.Id == cancelled.BookingId).ExecuteDeleteAsync();
            await ReadJob(db, gateway).ExecuteAsync(cancelled);
        }
        Assert.Empty(gateway.ReadRequests);
        await using var rebooking = Context(scenario.ProjectId);
        Assert.Equal(GroupBookingStatus.Created, (await new GroupBookingCoordinator(rebooking)
            .BookAsync(BookingCommand(scenario, GroupBookingOrigin.Manual))).Status);
        var confirmed = (await ReadRequestsAsync(rebooking, scenario.ProjectId)).Single(r => r.BookingId != cancelled.BookingId);
        await using var reader = postgres.CreateContext();
        await ReadJob(reader, gateway).ExecuteAsync(confirmed);
        Assert.Single(gateway.ReadRequests);
    }

    [Fact]
    public async Task Explicit_booking_source_keeps_a_newer_message_on_another_account_unread()
    {
        var scenario = await SeedAsync();
        await using (var db = Context(scenario.ProjectId))
        {
            var account = new WhatsAppAccount { ProjectId = scenario.ProjectId, Name = "Other account" };
            var other = new Conversation { ProjectId = scenario.ProjectId, CustomerId = scenario.Customer.Id,
                WhatsAppAccountId = account.Id };
            db.AddRange(account, other, new Message { ConversationId = other.Id, Direction = "Incoming",
                ExternalMessageId = "other-account", Content = "newer other account", MessageType = "Text",
                Timestamp = DateTime.UtcNow.AddMinutes(1) });
            await db.SaveChangesAsync();
        }
        var request = await BookAndReadRequestAsync(scenario, GroupBookingOrigin.Ai);
        var gateway = new ReceiptGateway();
        await using var reader = postgres.CreateContext();
        await ReadJob(reader, gateway).ExecuteAsync(request);
        Assert.Equal(scenario.Conversation.Id, request.ConversationId);
        Assert.Equal(scenario.Conversation.WhatsAppAccountId, Assert.Single(gateway.ReadRequests).GetProperty("whatsappAccountId").GetGuid());
    }

    private async Task<BookingLastMessageReadRequested> BookAndReadRequestAsync(Scenario scenario, GroupBookingOrigin origin)
    {
        await using var db = Context(scenario.ProjectId);
        var booking = await new GroupBookingCoordinator(db).BookAsync(BookingCommand(scenario, origin));
        Assert.Equal(GroupBookingStatus.Created, booking.Status);
        return Assert.Single(await ReadRequestsAsync(db, scenario.ProjectId));
    }

    private static GroupBookingCommand BookingCommand(Scenario scenario, GroupBookingOrigin origin) => new()
    {
        ProjectId = scenario.ProjectId, GroupId = scenario.Group.Id, CustomerName = scenario.Customer.Name,
        CustomerPhone = scenario.Customer.PhoneNumber, KnownCustomerId = scenario.Customer.Id,
        SourceConversationId = origin == GroupBookingOrigin.Ai ? scenario.Conversation.Id : null,
        CustomerCity = "الإسكندرية", AttendanceMode = "offline", Origin = origin,
        ExistingBookingPolicy = ExistingGroupBookingPolicy.Reject
    };

    private static async Task<BookingLastMessageReadRequested[]> ReadRequestsAsync(AppDbContext db, Guid projectId)
    {
        var envelopes = await db.IntegrationOutboxMessages.Where(envelope =>
            envelope.EventType == "BookingLastMessageReadRequested.v1").ToListAsync();
        var registry = new IntegrationEventTypeRegistry();
        return envelopes.Select(envelope => (BookingLastMessageReadRequested)registry.Deserialize(
            envelope.EventType, envelope.SchemaVersion, envelope.PayloadJson)).Where(request => request.ProjectId == projectId).ToArray();
    }

    private AppDbContext Context(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return postgres.CreateContext(tenant);
    }

    private async Task<Scenario> SeedAsync(Guid? lastMessageId = null, Guid? olderMessageId = null)
    {
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        var project = new Project { Name = "Booking read receipt" };
        var customer = new Customer { ProjectId = project.Id, PhoneNumber = "201012345678", Name = "طالب", City = "الإسكندرية" };
        var account = new WhatsAppAccount { Id = project.Id, ProjectId = project.Id, IsDefault = true };
        var conversation = new Conversation { ProjectId = project.Id, CustomerId = customer.Id, WhatsAppAccountId = account.Id };
        var group = new GroupAppointment { ProjectId = project.Id, Name = "Course", DateTime = DateTime.UtcNow.AddDays(3), Capacity = 5 };
        var last = new Message { Id = lastMessageId ?? Guid.NewGuid(), ConversationId = conversation.Id, ExternalMessageId = "last-before-booking",
            Direction = "Incoming", Content = "confirm booking", MessageType = "Text", WhatsAppRemoteJid = "123456789012345@lid",
            Timestamp = DateTime.UtcNow.AddMinutes(-1) };
        db.AddRange(project, customer, account, conversation, group, last,
            new Message { Id = olderMessageId ?? Guid.NewGuid(), ConversationId = conversation.Id, ExternalMessageId = "older",
                Direction = "Incoming", Content = "old", MessageType = "Text", Timestamp = last.Timestamp.AddMinutes(-1) },
            new Message { ConversationId = conversation.Id, ExternalMessageId = "outgoing",
                Direction = "Outgoing", Content = "reply", MessageType = "Text", Timestamp = DateTime.UtcNow },
            new Message { ConversationId = conversation.Id, ExternalMessageId = "reaction",
                Direction = "Incoming", Content = "❤️", MessageType = "Reaction", Timestamp = DateTime.UtcNow });
        await db.SaveChangesAsync();
        return new(project.Id, customer, conversation, group, last);
    }

    private static BookingLastMessageReadJob ReadJob(AppDbContext db, ReceiptGateway gateway)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["WhatsAppGateway:Url"] = "http://gateway.test"
        }).Build();
        return new(db, gateway, configuration, new WhatsAppGatewaySessionClient(gateway.CreateClient("session"), configuration));
    }

    private sealed record Scenario(Guid ProjectId, Customer Customer, Conversation Conversation,
        GroupAppointment Group, Message LastIncoming);

    private sealed class ReceiptGateway : HttpMessageHandler, IHttpClientFactory
    {
        public List<JsonElement> ReadRequests { get; } = [];
        public bool FailNextRead { get; set; }
        public bool Connected { get; set; } = true;
        public HttpClient CreateClient(string name) => new(this, disposeHandler: false);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new {
                    status = Connected ? "Connected" : "Disconnected", phoneNumber = "201099999999", connectedAt = DateTime.UtcNow.AddHours(-1)
                }), Encoding.UTF8, "application/json") };
            ReadRequests.Add(JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)).RootElement.Clone());
            var status = FailNextRead ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            FailNextRead = false;
            return new(status);
        }
    }
}
