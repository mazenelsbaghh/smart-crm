using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Conversations.Domain;
using Modules.Conversations.Jobs;
using Modules.Conversations.Services;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Shared.Events;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class ConversationReplyWindowConcurrencyTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Concurrent_staging_and_dispatch_create_one_outbox_event_with_the_complete_window()
    {
        var seed = await SeedConversationAsync();
        var dueAtUtc = DateTime.UtcNow.AddSeconds(-1);
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using var firstStageDb = postgres.CreateContext();
        await using var secondStageDb = postgres.CreateContext();
        var firstStage = new ConversationReplyWindowService(firstStageDb).StageAsync(
            Request(seed, seed.FirstMessageId, "ممكن", seed.FirstMessageAtUtc, dueAtUtc, connectedAt));
        var secondStage = new ConversationReplyWindowService(secondStageDb).StageAsync(
            Request(seed, seed.SecondMessageId, "السعر", seed.SecondMessageAtUtc, dueAtUtc, connectedAt));

        await Task.WhenAll(firstStage, secondStage);

        await using (var reactionDb = postgres.CreateContext())
        {
            reactionDb.Messages.Add(new Message
            {
                ConversationId = seed.ConversationId,
                ExternalMessageId = "reaction-after-latest",
                Direction = "Incoming",
                Content = "👍",
                MessageType = "Reaction",
                Timestamp = seed.SecondMessageAtUtc.AddMilliseconds(500)
            });
            await reactionDb.SaveChangesAsync();
        }

        await using var firstDispatcherDb = postgres.CreateContext();
        await using var secondDispatcherDb = postgres.CreateContext();
        await Task.WhenAll(
            new ConversationReplyWindowDispatcher(
                firstDispatcherDb,
                NullLogger<ConversationReplyWindowDispatcher>.Instance).DispatchAsync(),
            new ConversationReplyWindowDispatcher(
                secondDispatcherDb,
                NullLogger<ConversationReplyWindowDispatcher>.Instance).DispatchAsync());

        await using (var recoveryDb = postgres.CreateContext())
        {
            await new ConversationReplyWindowService(recoveryDb).StageAsync(
                Request(seed, seed.SecondMessageId, "السعر", seed.SecondMessageAtUtc, dueAtUtc, connectedAt));
            await new ConversationReplyWindowDispatcher(
                recoveryDb,
                NullLogger<ConversationReplyWindowDispatcher>.Instance).DispatchAsync();
        }

        await using var verification = postgres.CreateContext();
        var window = await verification.ConversationReplyWindows.IgnoreQueryFilters()
            .SingleAsync(item => item.ConversationId == seed.ConversationId);
        Assert.Equal(seed.SecondMessageId, window.LatestIncomingMessageId);
        Assert.Equal(window.EventId, window.DispatchedEventId);
        var outbox = await verification.IntegrationOutboxMessages
            .SingleAsync(item => item.EventId == window.EventId);
        Assert.Equal(1, await verification.IntegrationOutboxMessages
            .CountAsync(item => item.EventType == "MessageAggregated.v1"
                && item.PayloadJson.Contains(seed.ConversationId.ToString())));
        var payload = JsonSerializer.Deserialize<MessageAggregatedEvent>(outbox.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(seed.AccountId, payload.WhatsAppAccountId);
        Assert.Equal("ممكن\nالسعر", payload.Content);
        Assert.Equal(connectedAt, payload.RequiredWhatsAppConnectedAt);
    }

    [Fact]
    public async Task Same_message_cannot_regress_to_an_older_WhatsApp_connection_epoch()
    {
        var seed = await SeedConversationAsync();
        var newerEpoch = DateTimeOffset.UtcNow.AddMinutes(-1);
        var olderEpoch = newerEpoch.AddMinutes(-10);
        var newerDueAtUtc = DateTime.UtcNow.AddMinutes(1);
        await using var db = postgres.CreateContext();
        var replyWindows = new ConversationReplyWindowService(db);

        await replyWindows.StageAsync(Request(
            seed,
            seed.SecondMessageId,
            "السعر",
            seed.SecondMessageAtUtc,
            newerDueAtUtc,
            newerEpoch));
        await replyWindows.StageAsync(Request(
            seed,
            seed.SecondMessageId,
            "السعر",
            seed.SecondMessageAtUtc,
            DateTime.UtcNow.AddSeconds(-1),
            olderEpoch));

        var window = await db.ConversationReplyWindows.IgnoreQueryFilters()
            .SingleAsync(item => item.ConversationId == seed.ConversationId);
        Assert.Equal(newerEpoch, window.RequiredWhatsAppConnectedAt);
        Assert.Equal(
            ConversationReplyWindowService.WhatsAppDeliveryKey(seed.SecondMessageId, newerEpoch),
            window.WhatsAppDeliveryIdempotencyKey);
        Assert.Equal(newerDueAtUtc, window.DueAtUtc);
    }

    [Fact]
    public async Task Concurrent_follow_up_upserts_leave_one_active_automation_slot()
    {
        var seed = await SeedConversationAsync(includeLegacyFollowUp: true);
        var slotKey = $"whatsapp-ai-nurture:{seed.AccountId:N}:{seed.ConversationId:N}";
        var request = new PendingAutomationFollowUpRequest(
            seed.ProjectId,
            seed.CustomerId,
            slotKey,
            DateTime.UtcNow.AddDays(1),
            "نطمن على الحجز",
            ConversationId: seed.ConversationId,
            WhatsAppAccountId: seed.AccountId,
            Channel: "WhatsApp");
        await using var firstDb = postgres.CreateContext();
        await using var secondDb = postgres.CreateContext();

        await Task.WhenAll(
            new AutomationFollowUpService(firstDb).UpsertPendingAutomationFollowUpAsync(request),
            new AutomationFollowUpService(secondDb).UpsertPendingAutomationFollowUpAsync(request));

        await using var verification = postgres.CreateContext();
        var followUps = await verification.FollowUps.IgnoreQueryFilters()
            .Where(item => item.ConversationId == seed.ConversationId)
            .ToListAsync();
        var active = Assert.Single(followUps, item =>
            (item.Status is "Pending" or "Processing")
            && item.ActiveAutomationSlotKey == slotKey);
        Assert.Equal(seed.AccountId, active.WhatsAppAccountId);
        Assert.Single(followUps, item => item.Status == "Bypassed");
    }

    [Fact]
    public async Task Dispatcher_consumes_a_window_superseded_by_a_newer_outgoing_message()
    {
        var seed = await SeedConversationAsync();
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-5);
        await using (var stageDb = postgres.CreateContext())
        {
            await new ConversationReplyWindowService(stageDb).StageAsync(Request(
                seed,
                seed.SecondMessageId,
                "السعر",
                seed.SecondMessageAtUtc,
                DateTime.UtcNow.AddSeconds(-1),
                connectedAt));
            stageDb.Messages.Add(new Message
            {
                ConversationId = seed.ConversationId,
                ExternalMessageId = "human-reply",
                Direction = "Outgoing",
                Content = "اتفضل",
                MessageType = "Text",
                Timestamp = seed.SecondMessageAtUtc.AddSeconds(1)
            });
            await stageDb.SaveChangesAsync();
        }

        await using (var dispatchDb = postgres.CreateContext())
            await new ConversationReplyWindowDispatcher(
                dispatchDb,
                NullLogger<ConversationReplyWindowDispatcher>.Instance).DispatchAsync();

        await using var verification = postgres.CreateContext();
        var window = await verification.ConversationReplyWindows.IgnoreQueryFilters()
            .SingleAsync(item => item.ConversationId == seed.ConversationId);
        Assert.Equal(window.EventId, window.DispatchedEventId);
        Assert.False(await verification.IntegrationOutboxMessages
            .AnyAsync(item => item.EventId == window.EventId));
    }

    [Theory]
    [InlineData("Messenger", "{\"pageId\":\"page-1\",\"senderPSID\":\"sender-1\"}")]
    [InlineData("FacebookComment", "{\"pageId\":\"page-1\",\"commentId\":\"comment-1\"}")]
    public async Task Non_WhatsApp_live_and_recovery_share_the_source_message_contract(
        string channel,
        string metadata)
    {
        var seed = await SeedConversationAsync();
        await using var db = postgres.CreateContext();
        var conversation = await db.Conversations.IgnoreQueryFilters()
            .SingleAsync(item => item.Id == seed.ConversationId);
        conversation.Channel = channel;
        conversation.WhatsAppAccountId = null;
        await db.SaveChangesAsync();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MessageAggregation:MinDelayMs"] = "0",
                ["MessageAggregation:MaxDelayMs"] = "0"
            })
            .Build();
        var aggregator = new MessageAggregator(
            db,
            new ConversationReplyWindowService(db),
            configuration,
            new NoOpBackgroundJobClient());

        await aggregator.AggregateMessageAsync(
            seed.ProjectId,
            "sender-1",
            "السعر",
            seed.SecondMessageId,
            seed.SecondMessageAtUtc,
            conversationId: seed.ConversationId,
            channel: channel,
            channelMetadata: metadata);
        await new ConversationReplyWindowDispatcher(
            db,
            NullLogger<ConversationReplyWindowDispatcher>.Instance).DispatchAsync();
        var firstEventId = (await db.ConversationReplyWindows.IgnoreQueryFilters()
            .SingleAsync(item => item.ConversationId == seed.ConversationId)).EventId;

        await new ConversationReplyWindowService(db).StageAsync(new ConversationReplyWindowRequest(
            seed.ProjectId,
            seed.ConversationId,
            seed.SecondMessageId,
            "sender-1",
            "السعر",
            seed.SecondMessageAtUtc,
            DateTime.UtcNow.AddSeconds(-1),
            ConversationReplyWindowService.SourceMessageOccurrenceKey(seed.SecondMessageId),
            channel,
            ChannelMetadata: metadata));
        await new ConversationReplyWindowDispatcher(
            db,
            NullLogger<ConversationReplyWindowDispatcher>.Instance).DispatchAsync();

        var outbox = await db.IntegrationOutboxMessages.SingleAsync(item => item.EventId == firstEventId);
        var payload = JsonSerializer.Deserialize<MessageAggregatedEvent>(outbox.PayloadJson);
        Assert.NotNull(payload);
        Assert.Equal(channel, payload.Channel);
        Assert.Equal(metadata, payload.ChannelMetadata);
        Assert.Equal(1, await db.IntegrationOutboxMessages.CountAsync(item => item.EventId == firstEventId));
    }

    private async Task<ConversationSeed> SeedConversationAsync(bool includeLegacyFollowUp = false)
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var firstMessageAtUtc = DateTime.UtcNow.AddMinutes(-2);
        var secondMessageAtUtc = firstMessageAtUtc.AddSeconds(1);
        var firstMessageId = Guid.NewGuid();
        var secondMessageId = Guid.NewGuid();
        await using var db = postgres.CreateContext();
        await db.Database.MigrateAsync();
        db.Projects.Add(new Project { Id = projectId, Name = "Reply window concurrency" });
        db.WhatsAppAccounts.Add(new WhatsAppAccount
        {
            Id = accountId,
            ProjectId = projectId,
            Name = "Replies",
            IsDefault = true
        });
        db.Customers.Add(new Customer
        {
            Id = customerId,
            ProjectId = projectId,
            PhoneNumber = "201000000001",
            Name = "عميل",
            City = string.Empty,
            Notes = string.Empty
        });
        db.Conversations.Add(new Conversation
        {
            Id = conversationId,
            ProjectId = projectId,
            CustomerId = customerId,
            WhatsAppAccountId = accountId,
            Channel = "WhatsApp",
            Status = "Open",
            LastMessageTimestamp = secondMessageAtUtc
        });
        db.Messages.AddRange(
            new Message
            {
                Id = firstMessageId,
                ConversationId = conversationId,
                ExternalMessageId = "first",
                Direction = "Incoming",
                Content = "ممكن",
                MessageType = "Text",
                Timestamp = firstMessageAtUtc
            },
            new Message
            {
                Id = secondMessageId,
                ConversationId = conversationId,
                ExternalMessageId = "second",
                Direction = "Incoming",
                Content = "السعر",
                MessageType = "Text",
                Timestamp = secondMessageAtUtc
            });
        if (includeLegacyFollowUp)
        {
            db.FollowUps.Add(new FollowUp
            {
                ProjectId = projectId,
                CustomerId = customerId,
                ConversationId = conversationId,
                WhatsAppAccountId = accountId,
                Channel = "WhatsApp",
                DueDate = DateTime.UtcNow.AddHours(3),
                Notes = "قديم",
                Status = "Pending"
            });
        }
        await db.SaveChangesAsync();
        return new ConversationSeed(
            projectId,
            accountId,
            customerId,
            conversationId,
            firstMessageId,
            secondMessageId,
            firstMessageAtUtc,
            secondMessageAtUtc);
    }

    private static ConversationReplyWindowRequest Request(
        ConversationSeed seed,
        Guid messageId,
        string content,
        DateTime messageAtUtc,
        DateTime dueAtUtc,
        DateTimeOffset connectedAt) => new(
        seed.ProjectId,
        seed.ConversationId,
        messageId,
        "201000000001",
        content,
        messageAtUtc,
        dueAtUtc,
        ConversationReplyWindowService.WhatsAppEpochOccurrenceKey(connectedAt),
        WhatsAppAccountId: seed.AccountId,
        RequiredWhatsAppConnectedAt: connectedAt,
        WhatsAppDeliveryIdempotencyKey:
            ConversationReplyWindowService.WhatsAppDeliveryKey(messageId, connectedAt));

    private sealed record ConversationSeed(
        Guid ProjectId,
        Guid AccountId,
        Guid CustomerId,
        Guid ConversationId,
        Guid FirstMessageId,
        Guid SecondMessageId,
        DateTime FirstMessageAtUtc,
        DateTime SecondMessageAtUtc);

    private sealed class NoOpBackgroundJobClient : IBackgroundJobClient
    {
        public string Create(Job job, IState state) => Guid.NewGuid().ToString("N");

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
