using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.AI.Services;
using Modules.Conversations.API;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Conversations.Services;
using Modules.Facebook.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ConversationControllerTests
{
    [Theory]
    [InlineData(DeepLinkFilter.Conversation)]
    [InlineData(DeepLinkFilter.Customer)]
    public async Task Deep_link_filter_returns_only_the_matching_conversation_in_the_requested_channel(
        DeepLinkFilter filter)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());

        var customer = Customer(projectId, "201000000001", "العميل المطلوب");
        var otherCustomer = Customer(projectId, "201000000002", "عميل آخر");
        var target = Conversation(projectId, customer.Id, "WhatsApp");
        db.AddRange(
            customer,
            otherCustomer,
            target,
            Conversation(projectId, customer.Id, "Messenger"),
            Conversation(projectId, otherCustomer.Id, "WhatsApp"));
        await db.SaveChangesAsync();

        var response = Assert.IsType<OkObjectResult>(await Controller(db, projectId).ListConversations(projectId, new ConversationListQuery
        {
            Channel = "WhatsApp",
            ConversationId = filter == DeepLinkFilter.Conversation ? target.Id : null,
            CustomerId = filter == DeepLinkFilter.Customer ? customer.Id : null
        }));

        var conversations = JsonSerializer.SerializeToElement(
            response.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateArray().ToArray();
        var returnedConversation = Assert.Single(conversations);
        Assert.Equal(target.Id, returnedConversation.GetProperty("id").GetGuid());
        Assert.Equal(projectId, returnedConversation.GetProperty("projectId").GetGuid());
        Assert.Equal("WhatsApp", returnedConversation.GetProperty("channel").GetString());
        Assert.Equal("Open", returnedConversation.GetProperty("status").GetString());
        var returnedCustomer = returnedConversation.GetProperty("customer");
        Assert.Equal(customer.Id, returnedCustomer.GetProperty("id").GetGuid());
        Assert.Equal("العميل المطلوب", returnedCustomer.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Deep_link_filter_does_not_cross_project_boundary()
    {
        var activeProjectId = Guid.NewGuid();
        var otherProjectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(activeProjectId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());

        var otherCustomer = Customer(otherProjectId, "201000000009", "عميل مشروع آخر");
        var otherConversation = Conversation(otherProjectId, otherCustomer.Id, "WhatsApp");
        db.AddRange(otherCustomer, otherConversation);
        await db.SaveChangesAsync();

        var response = Assert.IsType<OkObjectResult>(await Controller(db, activeProjectId).ListConversations(activeProjectId, new ConversationListQuery
        {
            Channel = "WhatsApp",
            ConversationId = otherConversation.Id
        }));

        var conversations = JsonSerializer.SerializeToElement(
            response.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateArray().ToArray();
        Assert.Empty(conversations);
    }

    [Fact]
    public async Task Conversation_list_rejects_a_user_from_another_project()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());

        var response = await Controller(db, Guid.NewGuid()).ListConversations(
            projectId,
            new ConversationListQuery { ConversationId = Guid.NewGuid() });

        Assert.IsType<ForbidResult>(response);
    }

    private static ConversationController Controller(AppDbContext db, Guid authorizedProjectId)
    {
        var controller = new ConversationController(
            db,
            NoOpProxy.Create<IAssignmentEngine>(),
            NoOpProxy.Create<IEventBus>(),
            NoOpProxy.Create<IHubContext<NotificationHub>>(),
            new ConfigurationBuilder().Build(),
            RedisConnectionProxy.Create(),
            NoOpProxy.Create<IFacebookGraphService>(),
            NoOpProxy.Create<IAIBehaviorSettingsService>(),
            new ProjectAuthorizationService());
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim("ProjectId", authorizedProjectId.ToString())
                ], "Test"))
            }
        };
        return controller;
    }

    private static Customer Customer(Guid projectId, string phone, string name) => new()
    {
        ProjectId = projectId,
        PhoneNumber = phone,
        Name = name,
        City = string.Empty,
        Notes = string.Empty
    };

    private static Conversation Conversation(Guid projectId, Guid customerId, string channel) => new()
    {
        ProjectId = projectId,
        CustomerId = customerId,
        Channel = channel,
        Status = "Open",
        LastMessageTimestamp = DateTime.UtcNow
    };

    public enum DeepLinkFilter
    {
        Conversation,
        Customer
    }

    private class NoOpProxy : DispatchProxy
    {
        public static T Create<T>() where T : class => DispatchProxy.Create<T, NoOpProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            DefaultValue(targetMethod?.ReturnType);

        protected static object? DefaultValue(Type? returnType) =>
            returnType?.IsValueType == true ? Activator.CreateInstance(returnType) : null;
    }

    private class RedisConnectionProxy : NoOpProxy
    {
        public static IConnectionMultiplexer Create() =>
            DispatchProxy.Create<IConnectionMultiplexer, RedisConnectionProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod?.Name == nameof(IConnectionMultiplexer.GetDatabase)
                ? NoOpProxy.Create<IDatabase>()
                : DefaultValue(targetMethod?.ReturnType);
    }
}
