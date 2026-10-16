using System.Reflection;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Modules.AI.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Facebook.Domain;
using Modules.Facebook.Services;
using Modules.Facebook.Workers;
using Shared.Events;
using Shared.Infrastructure;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class FacebookReplySenderDeliveryTests
{
    // 2026-10-03: a delayed signature swallowed the customer's next scheduling question.
    [Theory]
    [InlineData("signature", 1)]
    [InlineData("newer-before-send", 0)]
    [InlineData("newer-between-parts", 1)]
    [InlineData("rejected-second-part", 2)]
    [InlineData("unknown-second-part", 1)]
    public async Task Messenger_keeps_signatures_with_the_answer_and_stops_old_parts_after_new_input(
        string scenario, int expectedSends)
    {
        var projectId = Guid.NewGuid();
        var sourceTimestamp = DateTime.UtcNow.AddMinutes(-1);
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var db = new AppDbContext(options, tenant, services);
        var customer = new Customer
        {
            ProjectId = projectId, Name = "عميل", City = "", PhoneNumber = "", FacebookPSID = "test-psid"
        };
        var conversation = new Conversation
        {
            ProjectId = projectId, CustomerId = customer.Id, Channel = "Messenger"
        };
        db.AddRange(customer, conversation, new ConnectedPage
        {
            ProjectId = projectId, FacebookPageId = "test-page", PageName = "Page",
            PageAccessToken = "test-token", IsActive = true
        });
        if (scenario == "newer-before-send") db.Messages.Add(Incoming(conversation.Id));
        await db.SaveChangesAsync();
        var sentMessages = new List<string>();
        var firstPartPersisted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var providerAttempts = 0;
        var graph = Proxy<IFacebookGraphService>((method, arguments) =>
        {
            if (method.Name != nameof(IFacebookGraphService.SendMessageAsync))
                throw new InvalidOperationException(method.Name);
            providerAttempts++;
            if (providerAttempts == 2 && scenario == "rejected-second-part")
                throw new HttpRequestException("Rate limited", null, System.Net.HttpStatusCode.TooManyRequests);
            if (providerAttempts == 2 && scenario == "unknown-second-part")
                throw new TaskCanceledException("Provider response was lost");
            sentMessages.Add(Assert.IsType<string>(arguments![3]));
            return Task.CompletedTask;
        });
        var client = Proxy<IClientProxy>((_, arguments) =>
        {
            if (arguments![0] as string == "ReceiveMessage") firstPartPersisted.TrySetResult();
            return Task.CompletedTask;
        });
        var hubClients = Proxy<IHubClients>((_, _) => client);
        var hub = Proxy<IHubContext<NotificationHub>>((_, _) => hubClients);
        var redisDatabase = Proxy<StackExchange.Redis.IDatabase>((_, _) => Task.FromResult(true));
        var redis = Proxy<IConnectionMultiplexer>((_, _) => redisDatabase);
        var sender = new FacebookReplySender(db, graph, NullLogger<FacebookReplySender>.Instance,
            hub, redis, new AIBehaviorSettingsService());
        var content = scenario == "signature"
            ? "محتاجة رقم الموبايل للحجز\n\n- شاهي ✨"
            : "الجزء الأول\n\nالجزء الثاني";

        var reply = new AIReplyGeneratedEvent
        {
            ProjectId = projectId, ConversationId = conversation.Id, Sender = customer.FacebookPSID,
            Content = content, Channel = "Messenger", ChannelMetadata = "{\"pageId\":\"test-page\"}",
            SourceMessageTimestampUtc = sourceTimestamp
        };
        var sending = sender.HandleAsync(reply);
        if (scenario == "newer-between-parts")
        {
            await firstPartPersisted.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await using var webhookDb = new AppDbContext(options, tenant, services);
            webhookDb.Messages.Add(Incoming(conversation.Id));
            await webhookDb.SaveChangesAsync();
        }
        if (scenario == "rejected-second-part")
        {
            await Assert.ThrowsAsync<HttpRequestException>(() => sending);
            await sender.HandleAsync(reply);
            Assert.Equal(3, providerAttempts);
        }
        else await sending;
        if (scenario == "unknown-second-part")
        {
            await sender.HandleAsync(reply);
            Assert.Equal(2, providerAttempts);
            Assert.Equal("Pending", conversation.Status);
            Assert.Single(await db.NotificationAlerts.ToListAsync());
        }

        Assert.Equal(expectedSends, sentMessages.Count);
        if (scenario == "signature") Assert.Equal("محتاجة رقم الموبايل للحجز\n- شاهي ✨", sentMessages[0]);
        Assert.Equal(expectedSends, await db.Messages.CountAsync(message => message.Direction == "Outgoing"));
    }

    private static Message Incoming(Guid conversationId) => new()
    {
        ConversationId = conversationId, ExternalMessageId = Guid.NewGuid().ToString(),
        Direction = "Incoming", Content = "المعاد امتى", MessageType = "Text", Timestamp = DateTime.UtcNow
    };

    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> handler) where T : class
    {
        var proxy = DispatchProxy.Create<T, BoundaryProxy>();
        ((BoundaryProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class BoundaryProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => Handler(method!, arguments);
    }
}
