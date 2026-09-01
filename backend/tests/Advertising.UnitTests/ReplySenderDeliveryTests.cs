using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Projects.Domain;
using Modules.WhatsApp.Services;
using Modules.WhatsApp.Workers;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ReplySenderDeliveryTests
{
    [Fact]
    public async Task Definitely_unsent_chunk_remainder_is_deferred_once_in_order_on_the_originating_account()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var conversationId = Guid.NewGuid();
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-2);
        var databaseRoot = new InMemoryDatabaseRoot();
        var databaseName = Guid.NewGuid().ToString();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var gateway = new OrderedGatewayHandler();

        using var services = new ServiceCollection()
            .AddSingleton<ITenantContext>(tenant)
            .AddSingleton<IConnectionMultiplexer>(NoOpRedis())
            .AddSingleton(NoOpHub())
            .AddScoped(provider => new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseInMemoryDatabase(databaseName, databaseRoot)
                    .Options,
                provider.GetRequiredService<ITenantContext>(),
                provider))
            .BuildServiceProvider();

        await using (var scope = services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, Timezone = "UTC" });
            db.Customers.Add(new Customer
            {
                Id = customerId,
                ProjectId = projectId,
                PhoneNumber = "201111111111",
                Name = "عميل",
                City = "القاهرة"
            });
            db.Conversations.Add(new Conversation
            {
                Id = conversationId,
                ProjectId = projectId,
                CustomerId = customerId,
                WhatsAppAccountId = accountId,
                Channel = "WhatsApp",
                Status = "Open",
                LastMessageTimestamp = DateTime.UtcNow.AddMinutes(-1)
            });
            db.Messages.Add(new Message
            {
                ConversationId = conversationId,
                ExternalMessageId = "incoming-for-chunk-test",
                Direction = "Incoming",
                Content = "محتاج تفاصيل",
                MessageType = "Text",
                Timestamp = DateTime.UtcNow.AddMinutes(-1)
            });
            await db.SaveChangesAsync();
        }

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["WhatsAppGateway:Url"] = "http://gateway.test"
            })
            .Build();
        var sender = new ReplySender(
            new HttpClient(gateway),
            configuration,
            new HumanMessagingEngine(configuration, services),
            services);
        var generatedEvent = new AIReplyGeneratedEvent
        {
            Id = Guid.NewGuid(),
            OccurredOn = DateTime.UtcNow.AddMinutes(-1),
            ProjectId = projectId,
            ConversationId = conversationId,
            WhatsAppAccountId = accountId,
            Sender = "201111111111",
            Content = "الجزء الأول\n\nالجزء الثاني\n\nالجزء الثالث",
            Channel = "WhatsApp",
            RequiredWhatsAppConnectedAt = connectedAt,
            WhatsAppDeliveryIdempotencyKey = "reply:stable-operation"
        };

        await sender.HandleAsync(generatedEvent);

        Assert.Equal(2, gateway.Requests.Count);
        Assert.All(gateway.Requests, request =>
        {
            Assert.Equal(projectId, request.GetProperty("projectId").GetGuid());
            Assert.Equal(accountId, request.GetProperty("whatsappAccountId").GetGuid());
            Assert.Equal(connectedAt, request.GetProperty("expectedConnectedAt").GetDateTimeOffset());
        });
        Assert.Equal("reply:stable-operation:0", gateway.Requests[0].GetProperty("idempotencyKey").GetString());
        Assert.Equal("reply:stable-operation:1", gateway.Requests[1].GetProperty("idempotencyKey").GetString());

        await using var verificationScope = services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var sent = Assert.Single(await verificationDb.Messages
            .Where(message => message.Direction == "Outgoing")
            .ToListAsync());
        Assert.Equal("الجزء الأول", sent.Content);

        var deferred = await verificationDb.FollowUps
            .OrderBy(followUp => followUp.DueDate)
            .ToListAsync();
        Assert.Collection(
            deferred,
            first => AssertDeferred(first, projectId, customerId, conversationId, accountId, "الجزء الثاني"),
            second => AssertDeferred(second, projectId, customerId, conversationId, accountId, "الجزء الثالث"));
        Assert.Null(deferred[0].DependsOnFollowUpId);
        Assert.Equal(deferred[0].Id, deferred[1].DependsOnFollowUpId);
        Assert.InRange(deferred[0].DueDate, DateTime.UtcNow.AddHours(23), DateTime.UtcNow.AddHours(25));
        Assert.Equal(TimeSpan.FromSeconds(1), deferred[1].DueDate - deferred[0].DueDate);
    }

    private static void AssertDeferred(
        Modules.CRM.Domain.FollowUp followUp,
        Guid projectId,
        Guid customerId,
        Guid conversationId,
        Guid accountId,
        string expectedContent)
    {
        Assert.Equal(projectId, followUp.ProjectId);
        Assert.Equal(customerId, followUp.CustomerId);
        Assert.Equal(conversationId, followUp.ConversationId);
        Assert.Equal(accountId, followUp.WhatsAppAccountId);
        Assert.Equal("WhatsApp", followUp.Channel);
        Assert.Equal("Pending", followUp.Status);
        Assert.Equal("DeferredReplyChunk", followUp.Type);
        Assert.Equal("Exact", followUp.Tone);
        Assert.Equal(expectedContent, followUp.Notes);
    }

    private sealed class OrderedGatewayHandler : HttpMessageHandler
    {
        public List<JsonElement> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Requests.Add(JsonDocument.Parse(body).RootElement.Clone());
            return Requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"messageId\":\"provider-first-chunk\"}", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.PreconditionFailed)
                {
                    Content = new StringContent("{\"code\":\"STALE_CONNECTION_EPOCH\"}", Encoding.UTF8, "application/json")
                };
        }
    }

    private static IHubContext<NotificationHub> NoOpHub()
    {
        var client = Proxy<IClientProxy>((method, _) =>
            method.Name == nameof(IClientProxy.SendCoreAsync)
                ? Task.CompletedTask
                : Default(method.ReturnType));
        var clients = Proxy<IHubClients>((method, _) =>
            method.Name == nameof(IHubClients.Group)
                ? client
                : Default(method.ReturnType));
        var groups = Proxy<IGroupManager>((method, _) => Default(method.ReturnType));
        return Proxy<IHubContext<NotificationHub>>((method, _) => method.Name switch
        {
            "get_Clients" => clients,
            "get_Groups" => groups,
            _ => Default(method.ReturnType)
        });
    }

    private static IConnectionMultiplexer NoOpRedis()
    {
        var database = Proxy<StackExchange.Redis.IDatabase>((method, _) => method.Name switch
        {
            nameof(StackExchange.Redis.IDatabase.StringSetAsync) => Task.FromResult(true),
            nameof(StackExchange.Redis.IDatabase.KeyDeleteAsync) => Task.FromResult(true),
            _ => Default(method.ReturnType)
        });
        return Proxy<IConnectionMultiplexer>((method, _) =>
            method.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? database
                : Default(method.ReturnType));
    }

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, InterfaceProxy>();
        ((InterfaceProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private static object? Default(Type type)
    {
        if (type == typeof(Task)) return Task.CompletedTask;
        return type.IsValueType ? Activator.CreateInstance(type) : null;
    }

    private class InterfaceProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = (_, _) => null;

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod is null ? null : Handler(targetMethod, args);
    }
}
