using System.Net;
using System.Reflection;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using Microsoft.Extensions.Hosting.Internal;
using Modules.Brain.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Advertising.Services;
using Modules.AI.Services;
using Modules.AI.Workers;
using Modules.Brain.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Conversations.Services;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.Facebook.Domain;
using Modules.Facebook.Services;
using Modules.GroupAppointments.Domain;
using Modules.Media.Domain;
using Modules.Media.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.IntegrationTests;

[Collection(PostgresCollection.Name)]
public sealed class AIReplySideEffectsTests(AIReplySideEffectsDatabase database)
    : IClassFixture<AIReplySideEffectsDatabase>
{
    [Theory]
    [InlineData("عايز أحجز لصاحبي أحمد ورقمه 01011112222", null, false)]
    [InlineData("01011112222", "ممكن تبعت رقم موبايل صاحبك؟", false)]
    [InlineData("01011112222", null, false)]
    [InlineData("رقمي 01011112222", null, true)]
    [InlineData("٠١٠ ١١١١ ٢٢٢٢", "ممكن تبعت رقم موبايلك؟", true)]
    [InlineData("01011112222", "علشان نكمل الحجز على واتساب، ممكن تبعت رقم الواتساب بتاعك؟", true)]
    public async Task Messenger_merges_phone_identity_only_when_requester_ownership_is_explicit(
        string content, string? precedingReply, bool ownsPhone)
    {
        await using var scenario = await CreateScenarioAsync(content);
        scenario.Settings.AiBehaviorSettingsJson = scenario.Behavior.Serialize(new AIBehaviorSettings
            { MessengerWhatsAppTransitionEnabled = true });
        var phoneCustomer = new Customer
        {
            ProjectId = scenario.ProjectId, PhoneNumber = "201011112222", Name = "Phone owner", City = "",
            Notes = "Phone owner's private notes"
        };
        scenario.Db.Customers.Add(phoneCustomer);
        if (precedingReply != null)
            scenario.Db.Messages.Add(Reply(scenario, precedingReply, scenario.Incoming.Timestamp.AddSeconds(-1)));
        await scenario.Db.SaveChangesAsync();

        await scenario.Worker.HandleAsync(scenario.Event());

        scenario.Db.ChangeTracker.Clear();
        Assert.Equal(ownsPhone ? 1 : 2, await scenario.Db.Customers.CountAsync());
        var conversation = await scenario.Db.Conversations.SingleAsync(item => item.Id == scenario.Conversation.Id);
        Assert.Equal(ownsPhone ? phoneCustomer.Id : scenario.Customer.Id, conversation.CustomerId);
        var persistedPhoneOwner = await scenario.Db.Customers.SingleAsync(item => item.Id == phoneCustomer.Id);
        Assert.Equal(ownsPhone ? scenario.Customer.FacebookPSID : null, persistedPhoneOwner.FacebookPSID);
        if (ownsPhone) Assert.Equal("201011112222", Assert.Single(scenario.Gateway.Recipients));
        else
        {
            Assert.Empty(scenario.Gateway.Recipients);
            Assert.Equal("Phone owner's private notes", persistedPhoneOwner.Notes);
            Assert.Single(scenario.Bus.Events.OfType<AIReplyGeneratedEvent>());
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_confirmation_requires_the_booking_deletion_to_commit(bool storageFails)
    {
        var failure = new BookingDeletionFailure { Enabled = storageFails };
        await using var scenario = await CreateScenarioAsync("الغي الحجز", failure);
        scenario.Settings.IsGroupAppointmentsEnabled = true;
        var group = new GroupAppointment
        {
            ProjectId = scenario.ProjectId, Name = "Booked group", Capacity = 10, DateTime = DateTime.UtcNow.AddDays(5)
        };
        var booking = new GroupAppointmentBooking
        {
            ProjectId = scenario.ProjectId, GroupAppointmentId = group.Id, CustomerId = scenario.Customer.Id,
            CustomerName = scenario.Customer.Name, CustomerPhone = "201011112222"
        };
        scenario.Db.AddRange(group, booking);
        await scenario.Db.SaveChangesAsync();
        scenario.Analysis = new() { CancelGroupBooking = true, ReplyContent = "تم إلغاء الحجز بنجاح" };

        if (storageFails)
            await Assert.ThrowsAsync<DbUpdateException>(() => scenario.Worker.HandleAsync(scenario.Event()));
        else
            await scenario.Worker.HandleAsync(scenario.Event());

        scenario.Db.ChangeTracker.Clear();
        Assert.Equal(storageFails, await scenario.Db.GroupAppointmentBookings.AnyAsync(item => item.Id == booking.Id));
        var replies = scenario.Bus.Events.OfType<AIReplyGeneratedEvent>();
        if (storageFails) Assert.Empty(replies);
        else Assert.Equal(scenario.Analysis.ReplyContent, Assert.Single(replies).Content);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(1, true)]
    public async Task Deferred_reply_uses_only_media_from_its_source_turn_even_after_two_minutes(
        int mediaAgeMinutes, bool answeredInPreviousTurn)
    {
        await using var scenario = await CreateScenarioAsync("[رسالة صوتية]");
        scenario.Incoming.Timestamp = DateTime.UtcNow.AddMinutes(-mediaAgeMinutes);
        await AddVoiceAsync(scenario);
        if (answeredInPreviousTurn)
        {
            scenario.Db.Messages.Add(Reply(scenario, "وصلت الرسالة السابقة", scenario.Incoming.Timestamp.AddSeconds(1)));
            var nextMessage = new Message
            {
                ConversationId = scenario.Conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(),
                Direction = "Incoming", MessageType = "Text", Content = "مواعيد العمل؟",
                Timestamp = scenario.Incoming.Timestamp.AddSeconds(2)
            };
            scenario.Db.Messages.Add(nextMessage);
            scenario.Incoming = nextMessage;
        }
        await scenario.Db.SaveChangesAsync();

        await scenario.Worker.RetryDeferredReplyAsync(scenario.Event());

        var media = Assert.Single(scenario.AnalysedMedia);
        if (answeredInPreviousTurn) Assert.Null(media);
        else Assert.Equal(new byte[] { 1, 2, 3 }, media);
        Assert.Single(scenario.Bus.Events.OfType<AIReplyGeneratedEvent>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Spoken_opt_out_cancels_followups_and_applies_the_selected_subscriber_policy(bool subscribersOnly)
    {
        await using var scenario = await CreateScenarioAsync("[رسالة صوتية]");
        scenario.Settings.AiBehaviorSettingsJson = scenario.Behavior.Serialize(new AIBehaviorSettings
        {
            SubscribersOnlyAutoStop = subscribersOnly, MessengerWhatsAppTransitionEnabled = false
        });
        await AddVoiceAsync(scenario);
        var followUp = new FollowUp
        {
            ProjectId = scenario.ProjectId, CustomerId = scenario.Customer.Id, Status = "Pending", Notes = "Pending contact"
        };
        scenario.Db.FollowUps.Add(followUp);
        await scenario.Db.SaveChangesAsync();
        scenario.Analysis = new() { Transcription = "مش عايز رسائل", ReplyContent = "رد لا يجب إرساله" };

        await scenario.Worker.HandleAsync(scenario.Event());

        scenario.Db.ChangeTracker.Clear();
        Assert.Equal(!subscribersOnly, (await scenario.Db.Customers.SingleAsync()).IsBlacklisted);
        Assert.Equal("Cancelled", (await scenario.Db.FollowUps.SingleAsync(item => item.Id == followUp.Id)).Status);
        Assert.Equal("مش عايز رسائل", (await scenario.Db.Messages.SingleAsync(item => item.Id == scenario.Incoming.Id)).Transcription);
        Assert.Empty(scenario.Bus.Events.OfType<AIReplyGeneratedEvent>());
    }

    [Theory]
    [InlineData("Gemini")]
    [InlineData("OpenAI")]
    [InlineData(CustomerReplyProviders.Xai)]
    public async Task Replayed_turn_reuses_paid_generation_but_withdrawn_knowledge_invalidates_it(string provider)
    {
        var externalRedis = Environment.GetEnvironmentVariable("ADVERTISING_TEST_REDIS_CONNECTION");
        await using var container = string.IsNullOrWhiteSpace(externalRedis)
            ? new ContainerBuilder("redis:7-alpine").WithPortBinding(6379, true)
                .WithWaitStrategy(Wait.ForUnixContainer().UntilInternalTcpPortIsAvailable(6379)).Build()
            : null;
        if (container is not null) await container.StartAsync();
        using var redis = await ConnectionMultiplexer.ConnectAsync(externalRedis
            ?? $"{container!.Hostname}:{container.GetMappedPublicPort(6379)}");
        using var handler = new ReplyProvider();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://provider.test/") };
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        await using var scenario = new WorkerScenario(database.Database.CreateContext(tenant), tenant,
            tenant.ProjectId, "ممكن تفاصيل", new ProviderBoundary(provider, redis, http));
        var document = new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = "Current offer",
            Content = "تفاصيل العرض المعتمد", Status = "Approved" };
        var factoryDocument = new KnowledgeDocument { ProjectId = tenant.ProjectId, Title = "Pricing Policy 2026",
            Content = "Our default subscription is $49/month with a 10% discount for annual payments.",
            SourceUrl = "https://example.com/pricing", Status = "Published", Version = 1 };
        scenario.Db.AddRange(new Project { Id = tenant.ProjectId, Name = "Reply cost regression" },
            scenario.Customer, scenario.Conversation, scenario.Settings, scenario.Incoming, document, factoryDocument,
            new KnowledgeChunk { KnowledgeDocumentId = document.Id, ChunkText = document.Content },
            new KnowledgeChunk { KnowledgeDocumentId = factoryDocument.Id, ChunkText = factoryDocument.Content },
            new WhatsAppAccount { Id = tenant.ProjectId, ProjectId = tenant.ProjectId, Name = "Default", IsDefault = true });
        await scenario.Db.SaveChangesAsync();
        var customerTurn = scenario.Event();

        await scenario.Worker.HandleAsync(customerTurn);
        await scenario.Worker.HandleAsync(customerTurn);

        Assert.Equal(1, handler.BillableRequests);
        Assert.All(scenario.Bus.Events.OfType<AIReplyGeneratedEvent>(), reply => Assert.Equal("وصلت الرسالة", reply.Content));
        document.Status = "Rejected";
        await scenario.Db.SaveChangesAsync();
        await scenario.Worker.HandleAsync(customerTurn);
        Assert.Equal(2, handler.BillableRequests);
        Assert.All(handler.Requests, request => Assert.DoesNotContain("Our default subscription", request));
    }

    private async Task<WorkerScenario> CreateScenarioAsync(string content, params IInterceptor[] interceptors)
    {
        var project = new Project { Name = "AI side effects regression" };
        var tenant = new TenantContext();
        tenant.SetProjectId(project.Id);
        var scenario = new WorkerScenario(database.Database.CreateContext(tenant, interceptors), tenant, project.Id, content);
        scenario.Db.AddRange(project, scenario.Customer, scenario.Conversation, scenario.Settings, scenario.Incoming,
            new WhatsAppAccount { Id = project.Id, ProjectId = project.Id, Name = "Default", IsDefault = true },
            new ConnectedPage { ProjectId = project.Id, FacebookPageId = scenario.PageId, PageName = "Page", PageAccessToken = "test-only" });
        await scenario.Db.SaveChangesAsync();
        return scenario;
    }

    private static async Task AddVoiceAsync(WorkerScenario scenario)
    {
        var asset = new Asset
        {
            ProjectId = scenario.ProjectId, FileName = "voice.ogg", ContentType = "audio/ogg", StoragePath = "voice.ogg"
        };
        scenario.Incoming.AssetId = asset.Id;
        scenario.Incoming.MessageType = "Voice";
        scenario.Db.Assets.Add(asset);
        await scenario.Db.SaveChangesAsync();
    }

    private static Message Reply(WorkerScenario scenario, string content, DateTime timestamp) => new()
    {
        ConversationId = scenario.Conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(),
        Direction = "Outgoing", MessageType = "Text", Content = content, Timestamp = timestamp
    };

    private sealed class WorkerScenario : IAsyncDisposable
    {
        private readonly ServiceProvider _services;
        public AppDbContext Db { get; }
        public Guid ProjectId { get; }
        public string PageId { get; } = Guid.NewGuid().ToString();
        public Customer Customer { get; }
        public Conversation Conversation { get; }
        public ProjectSettings Settings { get; }
        public Message Incoming { get; set; }
        public AIBehaviorSettingsService Behavior { get; } = new();
        public MarketingAnalysisResult Analysis { get; set; } = new() { ReplyContent = "وصلت الرسالة" };
        public List<byte[]?> AnalysedMedia { get; } = [];
        public GatewayHandler Gateway { get; } = new();
        public RecordingEventBus Bus { get; } = new();
        public AIReplyWorker Worker { get; }

        public WorkerScenario(AppDbContext db, TenantContext tenant, Guid projectId, string content, ProviderBoundary? provider = null)
        {
            Db = db;
            ProjectId = projectId;
            Customer = new() { ProjectId = projectId, Name = "Requester", PhoneNumber = "", FacebookPSID = Guid.NewGuid().ToString(), City = "Cairo" };
            Conversation = new() { ProjectId = projectId, CustomerId = Customer.Id, Channel = "Messenger" };
            Settings = new()
            {
                ProjectId = projectId, MessengerAiAutoReplyEnabled = true, CustomerReplyProvider = "OpenAI",
                CustomerReplyModel = "mock-model", MaxDailyMessages = 0,
                AiBehaviorSettingsJson = Behavior.Serialize(new AIBehaviorSettings { MessengerWhatsAppTransitionEnabled = false })
            };
            Incoming = new()
            {
                ConversationId = Conversation.Id, ExternalMessageId = Guid.NewGuid().ToString(), Direction = "Incoming",
                Content = content, Timestamp = DateTime.UtcNow.AddSeconds(-10), MessageType = "Text"
            };
            var configuration = new ConfigurationBuilder().Build();
            var protection = new EphemeralDataProtectionProvider();
            var redisDatabase = Proxy<IDatabase>((method, _) => method.Name == nameof(IDatabase.KeyDeleteAsync)
                ? Task.FromResult(true) : throw new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Isolated gateway test has no Redis"));
            IConnectionMultiplexer redis = provider?.Redis ?? Proxy<IConnectionMultiplexer>((method, _) => method.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? redisDatabase : throw new InvalidOperationException(method.Name));
            IAIMarketingBrain brain = Proxy<IAIMarketingBrain>((method, args) => method.Name switch
            {
                nameof(IAIMarketingBrain.BuildStaticPrompt) => "Test system context",
                nameof(IAIMarketingBrain.AnalyzeAndGenerateReplyAsync) => Analyse(args!),
                _ => throw new InvalidOperationException(method.Name)
            });
            IGeminiClient gemini = Proxy<IGeminiClient>((method, _) => throw new InvalidOperationException(method.Name));
            if (provider is not null)
            {
                var secretVault = new ProjectSecretVault(protection);
                var credential = secretVault.Protect(projectId, "cost-test-provider-key");
                Settings.CustomerReplyProvider = provider.Name;
                Settings.CustomerReplyModel = "test-model";
                Settings.GeminiModel = "gemini-2.5-flash-lite";
                Settings.GeminiApiKey = credential;
                Settings.CustomerReplyOpenAiApiKey = credential;
                Settings.CustomerReplyXaiApiKey = credential;
                gemini = new GeminiClient(configuration,
                    new GeminiMockHandler(new HostingEnvironment { EnvironmentName = "Production" }),
                    NullLogger<GeminiClient>.Instance, provider.Http);
                brain = new AIMarketingBrain(gemini,
                    new OpenAiResponsesClient(provider.Http, NullLogger<OpenAiResponsesClient>.Instance),
                    new XaiResponsesClient(provider.Http, NullLogger<XaiResponsesClient>.Instance), Behavior);
            }
            _services = new ServiceCollection()
                .AddSingleton(db).AddSingleton<ITenantContext>(tenant).AddSingleton<IConfiguration>(configuration)
                .AddSingleton<IAIBehaviorSettingsService>(Behavior).AddSingleton<IProjectSecretVault>(new ProjectSecretVault(protection))
                .AddSingleton(gemini).AddSingleton<IAICompanyBrain>(new AICompanyBrain(db, gemini))
                .AddSingleton(redis).AddSingleton(new AiGenerationCache(redis, protection, NullLogger<AiGenerationCache>.Instance))
                .AddSingleton(new WhatsAppAccountService(db)).AddSingleton(new WhatsAppCustomerMergeService(db))
                .AddSingleton(new WhatsAppConversationService(db)).AddSingleton(new AutomationFollowUpService(db))
                .AddSingleton(new CustomerOptOutService(db, Behavior))
                .AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(Gateway))
                .AddSingleton(new WhatsAppGatewaySessionClient(new HttpClient(Gateway, false), configuration))
                .AddSingleton(Hub()).AddSingleton(Proxy<IFacebookGraphService>((method, _) =>
                    method.Name == nameof(IFacebookGraphService.SendMessageAsync) ? Task.CompletedTask : throw new InvalidOperationException(method.Name)))
                .AddSingleton(Proxy<IMinIoStorageService>((method, _) => method.Name == nameof(IMinIoStorageService.DownloadFileAsync)
                    ? Task.FromResult<Stream>(new MemoryStream([1, 2, 3])) : throw new InvalidOperationException(method.Name)))
                .BuildServiceProvider();
            Worker = new(_services, brain, Bus, NullLogger<AIReplyWorker>.Instance);
        }

        private Task<MarketingAnalysisResult> Analyse(object?[] args)
        {
            AnalysedMedia.Add(args[7] as byte[]);
            return Task.FromResult(Analysis);
        }

        public MessageAggregatedEvent Event() => new()
        {
            ProjectId = ProjectId, ConversationId = Conversation.Id, Sender = Customer.FacebookPSID!, Channel = "Messenger",
            Content = Incoming.Content, SourceMessageTimestampUtc = Incoming.Timestamp,
            ChannelMetadata = JsonSerializer.Serialize(new { pageId = PageId, senderPSID = Customer.FacebookPSID })
        };

        public async ValueTask DisposeAsync()
        {
            await _services.DisposeAsync();
            Gateway.Dispose();
        }
    }

    private static IHubContext<NotificationHub> Hub()
    {
        var client = Proxy<IClientProxy>((method, _) => method.Name == nameof(IClientProxy.SendCoreAsync)
            ? Task.CompletedTask : throw new InvalidOperationException(method.Name));
        var clients = Proxy<IHubClients>((method, _) => method.Name == nameof(IHubClients.Group)
            ? client : throw new InvalidOperationException(method.Name));
        return Proxy<IHubContext<NotificationHub>>((method, _) => method.Name == "get_Clients"
            ? clients : throw new InvalidOperationException(method.Name));
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, BoundaryProxy>();
        ((BoundaryProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class BoundaryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }

    private sealed class GatewayHandler : HttpMessageHandler
    {
        public List<string> Recipients { get; } = [];
        private readonly DateTimeOffset _connectedAt = DateTimeOffset.UtcNow.AddHours(-1);
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
                return new(HttpStatusCode.OK) { Content = new StringContent(JsonSerializer.Serialize(new
                    { status = "Connected", phoneNumber = "201000000000", connectedAt = _connectedAt })) };
            using var payload = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            Recipients.Add(payload.RootElement.GetProperty("to").GetString()!);
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"messageId\":\"test-transition\"}") };
        }
    }

    private sealed record ProviderBoundary(string Name, IConnectionMultiplexer Redis, HttpClient Http);

    private sealed class ReplyProvider : HttpMessageHandler
    {
        public int BillableRequests { get; private set; }
        public List<string> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            BillableRequests++;
            Requests.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            const string answer = "{\"replyContent\":\"وصلت الرسالة\"}";
            var body = request.RequestUri!.AbsolutePath.Contains(":generateContent", StringComparison.Ordinal)
                ? JsonSerializer.Serialize(new { candidates = new[] { new { finishReason = "STOP", content = new { parts = new[] { new { text = answer } } } } } })
                : JsonSerializer.Serialize(new { status = "completed", output = new[] { new { type = "message", role = "assistant",
                    content = new[] { new { type = "output_text", text = answer } } } } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, false);
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public List<IntegrationEvent> Events { get; } = [];
        public Task PublishAsync<T>(T message) where T : IntegrationEvent
        {
            Events.Add(message);
            return Task.CompletedTask;
        }
        public void Subscribe<T, TH>(int consumers = 1) where T : IntegrationEvent where TH : IIntegrationEventHandler<T> { }
    }

    private sealed class BookingDeletionFailure : SaveChangesInterceptor
    {
        public bool Enabled { get; init; }
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && eventData.Context!.ChangeTracker.Entries<GroupAppointmentBooking>().Any(entry => entry.State == EntityState.Deleted))
                throw new DbUpdateException("Simulated booking storage outage");
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }
}

public sealed class AIReplySideEffectsDatabase(PostgresFixture postgres) : IAsyncLifetime
{
    public IsolatedPostgresDatabase Database { get; private set; } = null!;
    public async Task InitializeAsync()
    {
        Database = await postgres.CreateIsolatedDatabaseAsync();
        await using var db = Database.CreateContext();
        await db.Database.MigrateAsync();
    }
    public Task DisposeAsync() => Database.DisposeAsync().AsTask();
}
