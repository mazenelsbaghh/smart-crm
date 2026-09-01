using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.Advertising.Services;
using Modules.AI.Services;
using Modules.AI.Workers;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Facebook.Domain;
using Modules.Facebook.Services;
using Modules.Projects.Domain;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AIReplyWorkerDeliveryTests
{
    [Fact]
    public async Task Reaction_persistence_is_conversation_scoped_and_replay_broadcasts_once()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using var db = Context(projectId);
        var otherConversation = Conversation(projectId, accountId);
        var targetConversation = Conversation(projectId, accountId);
        const string providerMessageId = "provider-reaction-id";
        db.Conversations.AddRange(otherConversation, targetConversation);
        db.Messages.Add(new Message
        {
            ConversationId = otherConversation.Id,
            ExternalMessageId = providerMessageId,
            Direction = "Outgoing",
            Content = "[تفاعل] 👍",
            MessageType = "Reaction"
        });
        await db.SaveChangesAsync();
        var broadcasts = new ConcurrentBag<string>();
        var worker = Worker();
        var reaction = new AIReplyWorker.WhatsAppReactionPersistence(
            targetConversation,
            accountId,
            providerMessageId,
            "❤️");

        var created = await worker.PersistWhatsAppReactionAsync(db, Hub(broadcasts), reaction);
        var replayCreated = await worker.PersistWhatsAppReactionAsync(db, Hub(broadcasts), reaction);

        Assert.True(created);
        Assert.False(replayCreated);
        var persisted = await db.Messages
            .Where(message => message.ExternalMessageId == providerMessageId)
            .ToListAsync();
        Assert.Equal(2, persisted.Count);
        var targetReaction = Assert.Single(persisted, message => message.ConversationId == targetConversation.Id);
        Assert.Equal(
            AIReplyWorker.CreateWhatsAppReactionMessageId(projectId, accountId, providerMessageId),
            targetReaction.Id);
        Assert.Single(broadcasts, method => method == "ReceiveMessage");

        Assert.NotEqual(
            targetReaction.Id,
            AIReplyWorker.CreateWhatsAppReactionMessageId(projectId, Guid.NewGuid(), providerMessageId));
        Assert.NotEqual(
            targetReaction.Id,
            AIReplyWorker.CreateWhatsAppReactionMessageId(Guid.NewGuid(), accountId, providerMessageId));
        Assert.NotEqual(
            targetReaction.Id,
            AIReplyWorker.CreateWhatsAppReactionMessageId(projectId, accountId, "another-provider-id"));
    }

    [Fact]
    public async Task Transition_send_uses_the_exact_connected_account_epoch_and_caller_key_once()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var gateway = new GatewayHandler(HttpStatusCode.OK, connectedAt);
        using var services = TransitionServices(gateway);
        const string idempotencyKey = "messenger-transition:stable-event-id";

        var outcome = await AIReplyWorker.SendWhatsAppTransitionMessageAsync(
            services,
            new AIReplyWorker.WhatsAppTransitionMessage(
                projectId,
                accountId,
                "201111111111",
                "نكمل على واتساب",
                idempotencyKey));

        Assert.Equal(AIReplyWorker.WhatsAppTransitionDeliveryOutcome.Sent, outcome);
        Assert.Equal(1, gateway.SendCount);
        Assert.Contains(
            $"whatsappAccountId={accountId}",
            Assert.IsType<Uri>(gateway.StatusRequestUri).Query,
            StringComparison.OrdinalIgnoreCase);
        using var payload = JsonDocument.Parse(Assert.IsType<string>(gateway.SendRequestBody));
        Assert.Equal(projectId, payload.RootElement.GetProperty("projectId").GetGuid());
        Assert.Equal(accountId, payload.RootElement.GetProperty("whatsappAccountId").GetGuid());
        Assert.Equal(idempotencyKey, payload.RootElement.GetProperty("idempotencyKey").GetString());
        Assert.Equal(connectedAt, payload.RootElement.GetProperty("expectedConnectedAt").GetDateTimeOffset());
    }

    [Theory]
    [InlineData(412, "DefinitelyNotSent")]
    [InlineData(503, "DefinitelyNotSent")]
    [InlineData(409, "DeliveryUnknown")]
    [InlineData(500, "DeliveryUnknown")]
    [InlineData(502, "DeliveryUnknown")]
    public async Task Transition_send_distinguishes_definitely_unsent_from_ambiguous_gateway_results(
        int statusCode,
        string expected)
    {
        var gateway = new GatewayHandler((HttpStatusCode)statusCode, DateTimeOffset.UtcNow.AddMinutes(-1));
        using var services = TransitionServices(gateway);

        var outcome = await AIReplyWorker.SendWhatsAppTransitionMessageAsync(
            services,
            new AIReplyWorker.WhatsAppTransitionMessage(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "201111111111",
                "رسالة انتقال",
                "transition:one-logical-message"));

        Assert.Equal(expected, outcome.ToString());
        Assert.Equal(1, gateway.SendCount);
    }

    [Fact]
    public async Task Transition_send_requires_connected_at_and_treats_transport_failure_as_unknown()
    {
        var noEpochGateway = new GatewayHandler(HttpStatusCode.OK, connectedAt: null);
        using var noEpochServices = TransitionServices(noEpochGateway);
        var transition = new AIReplyWorker.WhatsAppTransitionMessage(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "201111111111",
            "رسالة انتقال",
            "transition:stable");

        var noEpochOutcome = await AIReplyWorker.SendWhatsAppTransitionMessageAsync(
            noEpochServices,
            transition);

        Assert.Equal(AIReplyWorker.WhatsAppTransitionDeliveryOutcome.DefinitelyNotSent, noEpochOutcome);
        Assert.Equal(0, noEpochGateway.SendCount);

        var transportGateway = new GatewayHandler(
            HttpStatusCode.OK,
            DateTimeOffset.UtcNow.AddMinutes(-1),
            throwOnSend: true);
        using var transportServices = TransitionServices(transportGateway);

        var transportOutcome = await AIReplyWorker.SendWhatsAppTransitionMessageAsync(
            transportServices,
            transition);

        Assert.Equal(AIReplyWorker.WhatsAppTransitionDeliveryOutcome.DeliveryUnknown, transportOutcome);
        Assert.Equal(1, transportGateway.SendCount);
    }

    [Fact]
    public async Task Messenger_transition_records_neither_success_nor_failure_when_delivery_is_unknown()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        await using var db = Context(projectId);
        var settings = new ProjectSettings { ProjectId = projectId };
        var customer = new Customer
        {
            ProjectId = projectId,
            Name = "عميل",
            PhoneNumber = "old-phone",
            FacebookPSID = "sender-psid",
            City = string.Empty
        };
        var conversation = Conversation(projectId, null, customer.Id, "Messenger");
        db.AddRange(
            new Project { Id = projectId, Name = "المشروع" },
            settings,
            customer,
            conversation,
            new ConnectedPage
            {
                ProjectId = projectId,
                FacebookPageId = "page-id",
                PageName = "Page",
                PageAccessToken = "mock_token",
                IsActive = true
            },
            new WhatsAppAccount
            {
                Id = accountId,
                ProjectId = projectId,
                Name = "الحساب الرئيسي",
                IsDefault = true
            });
        await db.SaveChangesAsync();
        var gateway = new GatewayHandler(HttpStatusCode.Conflict, DateTimeOffset.UtcNow.AddMinutes(-1));
        var facebook = new RecordingFacebookGraphService();
        var broadcasts = new ConcurrentBag<string>();
        using var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton(Configuration())
            .AddSingleton(new WhatsAppGatewaySessionClient(new HttpClient(gateway), Configuration()))
            .AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(gateway))
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton<IAIBehaviorSettingsService, AIBehaviorSettingsService>()
            .AddSingleton<IFacebookGraphService>(facebook)
            .AddSingleton(Hub(broadcasts))
            .AddSingleton(Redis())
            .BuildServiceProvider();

        await Worker().HandleMessengerToWhatsAppTransitionAsync(
            db,
            services,
            new AIReplyWorker.MessengerWhatsAppTransition(
                customer,
                "201111111111",
                settings,
                "page-id",
                "sender-psid",
                "messenger-transition:stable-event"));

        Assert.Equal("old-phone", customer.PhoneNumber);
        Assert.Empty(db.Messages);
        Assert.Empty(db.FollowUps);
        Assert.Equal(0, facebook.SendCount);
        Assert.DoesNotContain("ReceiveMessage", broadcasts);
        using var payload = JsonDocument.Parse(Assert.IsType<string>(gateway.SendRequestBody));
        Assert.Equal(
            "messenger-transition:stable-event",
            payload.RootElement.GetProperty("idempotencyKey").GetString());
    }

    private static AIReplyWorker Worker() => new(
        new ServiceCollection().BuildServiceProvider(),
        RejectingProxy.Create<IAIMarketingBrain>(),
        RejectingProxy.Create<IEventBus>(),
        NullLogger<AIReplyWorker>.Instance);

    private static ServiceProvider TransitionServices(GatewayHandler gateway)
    {
        var configuration = Configuration();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton(new WhatsAppGatewaySessionClient(new HttpClient(gateway), configuration))
            .AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(gateway))
            .BuildServiceProvider();
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsAppGateway:Url"] = "http://gateway.test"
        })
        .Build();

    private static AppDbContext Context(Guid projectId)
    {
        var tenant = Tenant(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
    }

    private static TenantContext Tenant(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return tenant;
    }

    private static Conversation Conversation(
        Guid projectId,
        Guid? accountId,
        Guid? customerId = null,
        string channel = "WhatsApp") => new()
        {
            ProjectId = projectId,
            CustomerId = customerId ?? Guid.NewGuid(),
            WhatsAppAccountId = accountId,
            Channel = channel,
            Status = "Open"
        };

    private static IHubContext<NotificationHub> Hub(ConcurrentBag<string> sentMethods)
    {
        var client = Proxy<IClientProxy>((method, arguments) =>
        {
            if (method.Name == nameof(IClientProxy.SendCoreAsync))
            {
                sentMethods.Add(Assert.IsType<string>(arguments![0]));
            }
            return Task.CompletedTask;
        });
        var clients = Proxy<IHubClients>((method, _) =>
            method.Name == nameof(IHubClients.Group)
                ? client
                : throw new InvalidOperationException($"Unexpected hub client call: {method.Name}"));
        var groups = Proxy<IGroupManager>((method, _) =>
            method.ReturnType == typeof(Task)
                ? Task.CompletedTask
                : throw new InvalidOperationException($"Unexpected group call: {method.Name}"));
        return Proxy<IHubContext<NotificationHub>>((method, _) => method.Name switch
        {
            "get_Clients" => clients,
            "get_Groups" => groups,
            _ => throw new InvalidOperationException($"Unexpected hub call: {method.Name}")
        });
    }

    private static IConnectionMultiplexer Redis()
    {
        var database = Proxy<StackExchange.Redis.IDatabase>((method, _) =>
            method.Name == nameof(StackExchange.Redis.IDatabase.KeyDeleteAsync)
                ? Task.FromResult(true)
                : throw new InvalidOperationException($"Unexpected Redis database call: {method.Name}"));
        return Proxy<IConnectionMultiplexer>((method, _) =>
            method.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? database
                : throw new InvalidOperationException($"Unexpected Redis connection call: {method.Name}"));
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is null ? null : Handler(targetMethod, args);
    }

    private class RejectingProxy : DispatchProxy
    {
        public static T Create<T>() where T : class => DispatchProxy.Create<T, RejectingProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected call to {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}.");
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class GatewayHandler(
        HttpStatusCode sendStatus,
        DateTimeOffset? connectedAt,
        bool throwOnSend = false) : HttpMessageHandler
    {
        public Uri? StatusRequestUri { get; private set; }
        public string? SendRequestBody { get; private set; }
        public int SendCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                StatusRequestUri = request.RequestUri;
                var body = JsonSerializer.Serialize(new
                {
                    status = "Connected",
                    phoneNumber = "201000000000",
                    connectedAt,
                    error = (string?)null
                });
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body, Encoding.UTF8, "application/json")
                };
            }

            SendCount++;
            SendRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (throwOnSend)
            {
                throw new HttpRequestException("Connection dropped after dispatch started.");
            }

            return new HttpResponseMessage(sendStatus)
            {
                Content = new StringContent(
                    sendStatus is >= HttpStatusCode.OK and < HttpStatusCode.MultipleChoices
                        ? "{\"messageId\":\"provider-transition-message\"}"
                        : "{}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class RecordingFacebookGraphService : IFacebookGraphService
    {
        public int SendCount { get; private set; }

        public Task SendMessageAsync(string pageId, string pageAccessToken, string recipientPSID, string message)
        {
            SendCount++;
            return Task.CompletedTask;
        }

        public Task ReplyToCommentAsync(string pageAccessToken, string commentId, string message) =>
            Task.CompletedTask;

        public Task ReactToCommentAsync(string pageAccessToken, string commentId, string reactionType = "LOVE") =>
            Task.CompletedTask;

        public Task SendPrivateReplyAsync(string pageId, string pageAccessToken, string commentId, string message) =>
            Task.CompletedTask;

        public Task SubscribePageToAppAsync(string pageId, string pageAccessToken) => Task.CompletedTask;

        public Task<List<FacebookPageInfo>> GetUserPagesAsync(string userAccessToken) =>
            Task.FromResult(new List<FacebookPageInfo>());

        public Task<string?> GetMessengerProfileNameAsync(string psid, string pageAccessToken) =>
            Task.FromResult<string?>(null);
    }

}
