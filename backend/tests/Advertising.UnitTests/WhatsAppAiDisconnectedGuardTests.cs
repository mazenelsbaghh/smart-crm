using System.Net;
using System.Reflection;
using System.Text;
using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.WebUtilities;
using Modules.Advertising.Services;
using Modules.AI.Services;
using Modules.AI.Workers;
using Modules.Conversations.Domain;
using Modules.Conversations.Jobs;
using Modules.Conversations.Services;
using Modules.Facebook.Domain;
using Modules.Projects.Domain;
using Modules.WhatsApp.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppAiDisconnectedGuardTests
{
    [Theory]
    [InlineData("Disconnected", null)]
    [InlineData("Connecting", "201000000000")]
    [InlineData("Connected", null)]
    public async Task Ai_reply_worker_rejects_recovery_events_without_a_connected_WhatsApp_session_2026_08_31(
        string gatewayStatus,
        string? gatewayPhone)
    {
        var gateway = CreateGateway(gatewayStatus, gatewayPhone);
        using var services = new ServiceCollection()
            .AddSingleton(gateway)
            .BuildServiceProvider();
        var publishedEvents = new RecordingEventBus();
        var worker = new AIReplyWorker(
            services,
            new RejectingMarketingBrain(),
            publishedEvents,
            NullLogger<AIReplyWorker>.Instance);

        await worker.HandleAsync(new MessageAggregatedEvent
        {
            ProjectId = Guid.NewGuid(),
            Sender = "201111111111",
            Content = "لسه مستني الرد",
            Channel = "WhatsApp"
        });

        Assert.Empty(publishedEvents.Events);
    }

    [Fact]
    public async Task Recovery_job_does_not_publish_WhatsApp_events_for_a_disconnected_project_2026_08_31()
    {
        var projectId = Guid.NewGuid();
        await using var db = CreateRecoveryDatabase(projectId);
        var dependencies = new UnansweredConversationRecoveryDependencies(
            CreateGateway("Disconnected", null),
            NullLogger<UnansweredConversationRecoveryJob>.Instance);
        var recoveryJob = new UnansweredConversationRecoveryJob(
            db,
            new ConversationReplyWindowService(db),
            dependencies);

        await recoveryJob.ExecuteAsync();

        Assert.Empty(db.ConversationReplyWindows);
    }

    [Fact]
    public async Task Lid_phone_request_waits_for_the_next_daily_slot_after_reconnect()
    {
        var projectId = Guid.NewGuid();
        await using var db = CreateDatabase(projectId);
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, Timezone = "UTC" });
        db.Customers.Add(new Customer
        {
            ProjectId = projectId,
            PhoneNumber = "synthetic@lid",
            WhatsAppLid = "synthetic@lid",
            Name = "عميل اختبار",
            City = "القاهرة",
            CreatedAt = DateTime.UtcNow.AddDays(-2).AddMinutes(-5)
        });
        await db.SaveChangesAsync();
        var publishedEvents = new RecordingEventBus();
        var job = new WhatsAppLidContactRecoveryJob(
            db,
            publishedEvents,
            RejectingProxy.Create<IConnectionMultiplexer>(),
            CreateGateway("Connected", "201000000000", DateTimeOffset.UtcNow.AddMinutes(-1)),
            new WhatsAppCustomerMergeService(db),
            NullLogger<WhatsAppLidContactRecoveryJob>.Instance);

        await job.ExecuteAsync();

        Assert.Empty(publishedEvents.Events);
    }

    [Fact]
    public async Task Ai_reply_worker_rejects_stale_recovery_from_before_the_current_connection_2026_09_01()
    {
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
        var gateway = CreateGateway("Connected", "201000000000", connectedAt);
        using var services = new ServiceCollection()
            .AddSingleton(gateway)
            .BuildServiceProvider();
        var publishedEvents = new RecordingEventBus();
        var worker = new AIReplyWorker(
            services,
            new RejectingMarketingBrain(),
            publishedEvents,
            NullLogger<AIReplyWorker>.Instance);

        await worker.HandleAsync(new MessageAggregatedEvent
        {
            ProjectId = Guid.NewGuid(),
            Sender = "201111111111",
            Content = "لسه مستني الرد",
            Channel = "WhatsApp",
            SourceMessageTimestampUtc = connectedAt.UtcDateTime.AddMinutes(-1)
        });

        Assert.Empty(publishedEvents.Events);
    }

    [Fact]
    public async Task Ai_reply_worker_rejects_an_event_from_a_previous_connection_epoch()
    {
        var previousConnection = DateTimeOffset.UtcNow.AddMinutes(-10);
        var currentConnection = DateTimeOffset.UtcNow.AddMinutes(-1);
        using var services = new ServiceCollection()
            .AddSingleton(CreateGateway("Connected", "201000000000", currentConnection))
            .BuildServiceProvider();
        var publishedEvents = new RecordingEventBus();
        var worker = new AIReplyWorker(
            services,
            new RejectingMarketingBrain(),
            publishedEvents,
            NullLogger<AIReplyWorker>.Instance);

        await worker.HandleAsync(new MessageAggregatedEvent
        {
            ProjectId = Guid.NewGuid(),
            Sender = "201111111111",
            Content = "لسه مستني الرد",
            Channel = "WhatsApp",
            SourceMessageTimestampUtc = DateTime.UtcNow.AddMinutes(-2),
            RequiredWhatsAppConnectedAt = previousConnection
        });

        Assert.Empty(publishedEvents.Events);
    }

    [Theory]
    [InlineData(-10, -5, 0)]
    [InlineData(-10, -15, 1)]
    [InlineData(-1442, -5, 1)]
    public async Task WhatsApp_recovery_only_requeues_messages_from_the_current_connection_2026_09_01(
        int messageAgeMinutes,
        int connectionAgeMinutes,
        int expectedEvents)
    {
        var projectId = Guid.NewGuid();
        var messageTimestamp = DateTime.UtcNow.AddMinutes(messageAgeMinutes);
        await using var db = CreateRecoveryDatabase(projectId, messageTimestamp);
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(connectionAgeMinutes);
        var dependencies = new UnansweredConversationRecoveryDependencies(
            CreateGateway(
                "Connected",
                "201000000000",
                connectedAt),
            NullLogger<UnansweredConversationRecoveryJob>.Instance);

        await new UnansweredConversationRecoveryJob(
            db,
            new ConversationReplyWindowService(db),
            dependencies).ExecuteAsync();

        if (expectedEvents == 0)
        {
            Assert.Empty(db.ConversationReplyWindows);
            return;
        }

        var recoveryWindow = Assert.Single(db.ConversationReplyWindows);
        Assert.Equal("WhatsApp", recoveryWindow.Channel);
        Assert.Equal(messageTimestamp, recoveryWindow.LatestIncomingAtUtc);
        Assert.Equal(connectedAt, recoveryWindow.RequiredWhatsAppConnectedAt);
        Assert.StartsWith("reply_", recoveryWindow.WhatsAppDeliveryIdempotencyKey);
    }

    [Fact]
    public async Task WhatsApp_recovery_publishes_only_once_for_the_same_daily_slot()
    {
        var projectId = Guid.NewGuid();
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-15);
        await using var db = CreateRecoveryDatabase(projectId, DateTime.UtcNow.AddMinutes(-10));
        var dependencies = new UnansweredConversationRecoveryDependencies(
            CreateGateway("Connected", "201000000000", connectedAt),
            NullLogger<UnansweredConversationRecoveryJob>.Instance);
        var job = new UnansweredConversationRecoveryJob(
            db,
            new ConversationReplyWindowService(db),
            dependencies);

        await job.ExecuteAsync();
        await job.ExecuteAsync();

        var window = Assert.Single(db.ConversationReplyWindows);
        Assert.Equal(1, window.LatestIncomingVersion);
    }

    [Fact]
    public async Task WhatsApp_recovery_revalidates_the_account_epoch_before_staging()
    {
        var projectId = Guid.NewGuid();
        var selectedEpoch = DateTimeOffset.UtcNow.AddMinutes(-15);
        await using var db = CreateRecoveryDatabase(projectId, DateTime.UtcNow.AddMinutes(-10));
        var gatewayHandler = new EpochChangingGatewayStatusHandler(
            selectedEpoch,
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var dependencies = new UnansweredConversationRecoveryDependencies(
            CreateGateway(gatewayHandler),
            NullLogger<UnansweredConversationRecoveryJob>.Instance);

        await new UnansweredConversationRecoveryJob(
            db,
            new ConversationReplyWindowService(db),
            dependencies).ExecuteAsync();

        Assert.Empty(db.ConversationReplyWindows);
        Assert.Equal(2, gatewayHandler.RequestCount);
    }

    [Fact]
    public async Task Offline_message_due_just_after_reconnect_waits_for_the_next_daily_occurrence()
    {
        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        await using var db = CreateRecoveryDatabase(projectId, now.AddMinutes(-1));
        var connectedAt = new DateTimeOffset(now.AddSeconds(-30), TimeSpan.Zero);
        var dependencies = new UnansweredConversationRecoveryDependencies(
            CreateGateway("Connected", "201000000000", connectedAt),
            NullLogger<UnansweredConversationRecoveryJob>.Instance);

        await new UnansweredConversationRecoveryJob(
            db,
            new ConversationReplyWindowService(db),
            dependencies).ExecuteAsync();

        Assert.Empty(db.ConversationReplyWindows);
    }

    [Fact]
    public async Task Recovery_requeues_only_the_connected_WhatsApp_account_in_a_multi_account_project()
    {
        var projectId = Guid.NewGuid();
        var connectedAccountId = Guid.NewGuid();
        var disconnectedAccountId = Guid.NewGuid();
        var messageTimestamp = DateTime.UtcNow.AddMinutes(-5);
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await using var db = CreateDatabase(projectId);
        db.ProjectSettings.Add(new ProjectSettings
        {
            ProjectId = projectId,
            AiAutoReplyEnabled = true
        });
        var connectedConversation = AddRecoveryConversation(db, projectId, new(
            "WhatsApp",
            "201111111111",
            "connected-account-message",
            messageTimestamp,
            connectedAccountId));
        AddRecoveryConversation(db, projectId, new(
            "WhatsApp",
            "201222222222",
            "disconnected-account-message",
            messageTimestamp,
            disconnectedAccountId));
        await db.SaveChangesAsync();
        var gatewayHandler = new AccountScopedGatewayStatusHandler(connectedAccountId, connectedAt);
        var dependencies = new UnansweredConversationRecoveryDependencies(
            CreateGateway(gatewayHandler),
            NullLogger<UnansweredConversationRecoveryJob>.Instance);

        await new UnansweredConversationRecoveryJob(
            db,
            new ConversationReplyWindowService(db),
            dependencies).ExecuteAsync();

        var recoveryWindow = Assert.Single(db.ConversationReplyWindows);
        Assert.Equal(connectedConversation.Id, recoveryWindow.ConversationId);
        Assert.Equal(connectedAccountId, recoveryWindow.WhatsAppAccountId);
        Assert.Equal(connectedAt, recoveryWindow.RequiredWhatsAppConnectedAt);
        Assert.Contains(connectedAccountId, gatewayHandler.RequestedAccountIds);
        Assert.Contains(disconnectedAccountId, gatewayHandler.RequestedAccountIds);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Old_WhatsApp_backlog_does_not_starve_Messenger_recovery_2026_09_01(
        bool whatsAppConnected)
    {
        var projectId = Guid.NewGuid();
        await using var db = CreateStarvationRegressionDatabase(projectId);
        var dependencies = new UnansweredConversationRecoveryDependencies(
            CreateGateway(
                whatsAppConnected ? "Connected" : "Disconnected",
                whatsAppConnected ? "201000000000" : null,
                whatsAppConnected ? DateTimeOffset.UtcNow.AddHours(-1) : null),
            NullLogger<UnansweredConversationRecoveryJob>.Instance);
        var recoveryJob = new UnansweredConversationRecoveryJob(
            db,
            new ConversationReplyWindowService(db),
            dependencies);

        await recoveryJob.ExecuteAsync();

        var recoveryWindow = Assert.Single(db.ConversationReplyWindows);
        Assert.Equal("Messenger", recoveryWindow.Channel);
    }

    private static AppDbContext CreateRecoveryDatabase(Guid projectId, DateTime? messageTimestamp = null)
    {
        var db = CreateDatabase(projectId);
        db.ProjectSettings.Add(new ProjectSettings
        {
            ProjectId = projectId,
            AiAutoReplyEnabled = true
        });
        AddRecoveryConversation(db, projectId, new(
            "WhatsApp",
            "201111111111",
            "incident-2026-08-31",
            messageTimestamp ?? DateTime.UtcNow.AddMinutes(-5)));
        db.SaveChanges();
        return db;
    }

    private static AppDbContext CreateStarvationRegressionDatabase(Guid projectId)
    {
        var db = CreateDatabase(projectId);
        db.ProjectSettings.Add(new ProjectSettings
        {
            ProjectId = projectId,
            AiAutoReplyEnabled = true,
            MessengerAiAutoReplyEnabled = true
        });
        var oldestWhatsAppMessage = DateTime.UtcNow.AddDays(-2);
        for (var index = 0; index < 25; index++)
            AddRecoveryConversation(db, projectId, new(
                "WhatsApp",
                $"201000000{index:D3}",
                $"old-whatsapp-{index}",
                oldestWhatsAppMessage.AddMinutes(index)));

        AddRecoveryConversation(db, projectId, new(
            "Messenger",
            "messenger-customer",
            "eligible-messenger",
            DateTime.UtcNow.AddMinutes(-5)));
        db.ConnectedPages.Add(new ConnectedPage
        {
            ProjectId = projectId,
            FacebookPageId = "page-1",
            PageName = "Test page",
            PageAccessToken = "test-token",
            IsActive = true
        });
        db.SaveChanges();
        return db;
    }

    private static AppDbContext CreateDatabase(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var services = new ServiceCollection().BuildServiceProvider();
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            services);
    }

    private static Conversation AddRecoveryConversation(
        AppDbContext db,
        Guid projectId,
        RecoveryConversationSeed seed)
    {
        var customer = new Customer
        {
            ProjectId = projectId,
            PhoneNumber = seed.Channel == "WhatsApp" ? seed.Sender : $"phone-{seed.Sender}",
            FacebookPSID = seed.Channel == "WhatsApp" ? null : seed.Sender,
            Name = "عميل اختبار",
            City = "القاهرة"
        };
        var conversation = new Conversation
        {
            ProjectId = projectId,
            CustomerId = customer.Id,
            WhatsAppAccountId = seed.WhatsAppAccountId,
            Channel = seed.Channel,
            Status = "Open",
            LastMessageTimestamp = seed.Timestamp
        };
        db.Customers.Add(customer);
        db.Conversations.Add(conversation);
        db.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            ExternalMessageId = seed.ExternalMessageId,
            Direction = "Incoming",
            Content = "محدش رد عليا",
            MessageType = "Text",
            Timestamp = seed.Timestamp
        });
        return conversation;
    }

    private static WhatsAppGatewaySessionClient CreateGateway(
        string status,
        string? phoneNumber,
        DateTimeOffset? connectedAt = null)
        => CreateGateway(new GatewayStatusHandler(status, phoneNumber, connectedAt));

    private static WhatsAppGatewaySessionClient CreateGateway(HttpMessageHandler handler)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsAppGateway:Url"] = "http://gateway.test"
            })
            .Build();
        return new WhatsAppGatewaySessionClient(
            new HttpClient(handler),
            configuration);
    }

    private sealed record RecoveryConversationSeed(
        string Channel,
        string Sender,
        string ExternalMessageId,
        DateTime Timestamp,
        Guid? WhatsAppAccountId = null);

    private sealed class AccountScopedGatewayStatusHandler(
        Guid connectedAccountId,
        DateTimeOffset connectedAt) : HttpMessageHandler
    {
        public ConcurrentBag<Guid> RequestedAccountIds { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var query = QueryHelpers.ParseQuery(request.RequestUri?.Query ?? string.Empty);
            var accountId = Guid.Parse(query["whatsappAccountId"].ToString());
            RequestedAccountIds.Add(accountId);
            var isConnected = accountId == connectedAccountId;
            var responseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = isConnected ? "Connected" : "Disconnected",
                phoneNumber = isConnected ? "201000000000" : null,
                connectedAt = isConnected ? connectedAt : (DateTimeOffset?)null,
                error = (string?)null
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class GatewayStatusHandler(
        string status,
        string? phoneNumber,
        DateTimeOffset? connectedAt) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var responseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                status,
                phoneNumber,
                connectedAt,
                error = (string?)null
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class EpochChangingGatewayStatusHandler(
        DateTimeOffset selectedEpoch,
        DateTimeOffset liveEpoch) : HttpMessageHandler
    {
        private int _requestCount;

        public int RequestCount => _requestCount;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var connectedAt = Interlocked.Increment(ref _requestCount) == 1
                ? selectedEpoch
                : liveEpoch;
            var responseJson = System.Text.Json.JsonSerializer.Serialize(new
            {
                status = "Connected",
                phoneNumber = "201000000000",
                connectedAt,
                error = (string?)null
            });
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
        }
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<IntegrationEvent> Events { get; } = [];

        public Task PublishAsync<T>(T @event) where T : IntegrationEvent
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }

        public void Subscribe<T, THandler>(int consumerCount = 1)
            where T : IntegrationEvent
            where THandler : IIntegrationEventHandler<T>
        {
        }
    }

    private sealed class RejectingMarketingBrain : IAIMarketingBrain
    {
        public Task<MarketingAnalysisResult> AnalyzeAndGenerateReplyAsync(
            string messageContent,
            string apiKeyOverride = null!,
            string brainContext = null!,
            string chatHistory = null!,
            string customerMemory = null!,
            string[] existingLabels = null!,
            string customerProfile = null!,
            byte[] fileBytes = null!,
            string mimeType = null!,
            string aiTonePreference = null!,
            string aiTargetAudience = null!,
            CustomerReplyRuntime? customerReply = null,
            string? systemPromptOverride = null,
            AIBehaviorSettings? aiBehaviorSettings = null,
            string channel = "WhatsApp") => throw UnexpectedAiUse();

        public string BuildStaticPrompt(
            string agentName,
            string tonePref,
            string targetAud,
            string approvedKnowledgeBaseText,
            string? systemPromptOverride = null,
            AIBehaviorSettings? aiBehaviorSettings = null,
            string channel = "WhatsApp") => throw UnexpectedAiUse();

        public string GetCurrentAgentName(string? agentInstructions = null) => throw UnexpectedAiUse();

        public Task<string> RewriteFollowUpNotesAsync(
            string customerName,
            string notes,
            bool hasAttended,
            string? tone = null,
            string? apiKeyOverride = null,
            string? modelOverride = null) => throw UnexpectedAiUse();

        private static InvalidOperationException UnexpectedAiUse() =>
            new("Disconnected WhatsApp recovery must not reach the AI boundary.");
    }

    private class RedisConnectionProxy : DispatchProxy
    {
        private IDatabase _database = null!;

        public static IConnectionMultiplexer Create()
        {
            var connection = DispatchProxy.Create<IConnectionMultiplexer, RedisConnectionProxy>();
            ((RedisConnectionProxy)(object)connection)._database = RedisDatabaseProxy.Create();
            return connection;
        }

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? _database
                : throw new InvalidOperationException($"Unexpected Redis connection call: {targetMethod?.Name}");
    }

    private class RedisDatabaseProxy : DispatchProxy
    {
        private readonly HashSet<string> _keys = [];

        public static IDatabase Create() => DispatchProxy.Create<IDatabase, RedisDatabaseProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => targetMethod?.Name switch
        {
            nameof(IDatabase.StringSetAsync) => Task.FromResult(_keys.Add(args![0]!.ToString()!)),
            nameof(IDatabase.KeyDeleteAsync) => Task.FromResult(_keys.Remove(args![0]!.ToString()!)),
            _ => throw new InvalidOperationException($"Unexpected Redis database call: {targetMethod?.Name}")
        };
    }

    private class RejectingProxy : DispatchProxy
    {
        public static T Create<T>() where T : class => DispatchProxy.Create<T, RejectingProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Disconnected recovery must not acquire a Redis publish lock.");
    }
}
