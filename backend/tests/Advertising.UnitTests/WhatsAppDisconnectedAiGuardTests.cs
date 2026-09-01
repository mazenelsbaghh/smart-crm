using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Hangfire;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.AI.Services;
using Modules.Advertising.Services;
using Modules.Campaigns.Application.Services;
using Modules.Campaigns.Domain;
using Modules.Campaigns.Jobs;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.CRM.API;
using Modules.CRM.Domain;
using Modules.CRM.Services;
using Modules.Customers.Services;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppDisconnectedAiGuardTests
{
    [Fact]
    public async Task Campaign_send_2026_08_31_regression_skips_AI_while_WhatsApp_is_disconnected()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var customer = Customer(projectId);
        var campaign = new Campaign
        {
            ProjectId = projectId,
            Name = "Regression campaign",
            SegmentId = Guid.NewGuid(),
            MessageTemplateA = "أهلاً {{CustomerName}} عن {{InterestTopic}}",
            MessageTemplateB = "أهلاً {{CustomerName}}",
            Status = CampaignStatus.Running
        };
        var recipient = new CampaignRecipient
        {
            ProjectId = projectId,
            CampaignId = campaign.Id,
            CustomerId = customer.Id,
            Status = RecipientStatus.Pending
        };
        db.AddRange(customer, campaign, recipient);
        await db.SaveChangesAsync();
        var job = new CampaignSenderJob(
            db,
            Configuration(),
            new RejectingCampaignAiService(),
            DisconnectedSessionClient(),
            new TestHttpClientFactory(new ThrowingHttpHandler()),
            RejectingProxy.Create<IBackgroundJobClient>());

        await job.SendSingleMessageAsync(recipient.Id);

        Assert.Equal(CampaignStatus.Paused, campaign.Status);
        Assert.Equal(RecipientStatus.Pending, recipient.Status);
        Assert.Null(recipient.SentAt);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scheduled_follow_up_2026_09_01_regression_moves_to_the_next_day_and_waits_after_reconnect(
        bool alreadyReconnected)
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var customer = Customer(projectId);
        var followUp = FollowUp(projectId, customer.Id);
        var originalDueDate = followUp.DueDate;
        db.AddRange(customer, followUp);
        await db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IConfiguration>(Configuration())
            .AddSingleton(alreadyReconnected
                ? ConnectedSessionClient(DateTimeOffset.UtcNow)
                : DisconnectedSessionClient())
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton(new WhatsAppConversationService(db))
            .AddSingleton(RejectingProxy.Create<IHubContext<NotificationHub>>())
            .AddSingleton<IProjectSecretVault, PlaintextTestVault>()
            .AddSingleton(RejectingProxy.Create<IAIMarketingBrain>())
            .BuildServiceProvider();

        await new FollowUpScheduler(services).CheckOverdueFollowUpsJobAsync();

        Assert.Equal("Pending", followUp.Status);
        Assert.Equal(originalDueDate.AddDays(1), followUp.DueDate);

        using var reconnectedServices = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IConfiguration>(Configuration())
            .AddSingleton(ConnectedSessionClient(DateTimeOffset.UtcNow))
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton(new WhatsAppConversationService(db))
            .AddSingleton(RejectingProxy.Create<IHubContext<NotificationHub>>())
            .AddSingleton<IProjectSecretVault, PlaintextTestVault>()
            .AddSingleton(RejectingProxy.Create<IAIMarketingBrain>())
            .BuildServiceProvider();

        await new FollowUpScheduler(reconnectedServices).CheckOverdueFollowUpsJobAsync();

        Assert.Equal("Pending", followUp.Status);
        Assert.Equal(originalDueDate.AddDays(1), followUp.DueDate);
        Assert.Empty(db.Messages);
    }

    [Theory]
    [InlineData(5, 1)]
    [InlineData(1445, 2)]
    [InlineData(2885, 3)]
    public void Daily_WhatsApp_deferral_uses_the_first_future_daily_occurrence(
        int overdueMinutes,
        int expectedDays)
    {
        var originalDueDate = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        var deferredUntil = WhatsAppDailyDeliverySchedule.NextOccurrenceAfter(
            originalDueDate,
            originalDueDate.AddMinutes(overdueMinutes),
            TimeZoneInfo.Utc);

        Assert.Equal(originalDueDate.AddDays(expectedDays), deferredUntil);
    }

    [Theory]
    [InlineData(412)]
    [InlineData(503)]
    public async Task Scheduled_follow_up_defers_when_the_gateway_cannot_safely_accept_delivery(
        int gatewayStatusCode)
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var customer = Customer(projectId);
        var followUp = FollowUp(projectId, customer.Id);
        var originalDueDate = followUp.DueDate;
        var connectedAt = new DateTimeOffset(originalDueDate.AddMinutes(-5), TimeSpan.Zero);
        var sendHandler = new DeferredGatewaySendHandler((HttpStatusCode)gatewayStatusCode);
        db.AddRange(customer, followUp);
        await db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IConfiguration>(Configuration())
            .AddSingleton(ConnectedSessionClient(connectedAt))
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton(new WhatsAppConversationService(db))
            .AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(sendHandler))
            .AddSingleton(RejectingProxy.Create<IHubContext<NotificationHub>>())
            .AddSingleton<IProjectSecretVault, PlaintextTestVault>()
            .AddSingleton(RejectingProxy.Create<IAIMarketingBrain>())
            .BuildServiceProvider();

        await new FollowUpScheduler(services).CheckOverdueFollowUpsJobAsync();

        Assert.Equal("Pending", followUp.Status);
        Assert.Equal(originalDueDate.AddDays(1), followUp.DueDate);
        Assert.Empty(db.Messages);
        using var payload = JsonDocument.Parse(Assert.IsType<string>(sendHandler.RequestBody));
        Assert.Equal(
            connectedAt,
            payload.RootElement.GetProperty("expectedConnectedAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Deferred_reply_chunks_never_overtake_a_predecessor_that_was_deferred_again()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var customer = Customer(projectId);
        var first = FollowUp(projectId, customer.Id);
        first.Tone = "Exact";
        first.Notes = "الجزء الأول";
        var second = FollowUp(projectId, customer.Id);
        second.Tone = "Exact";
        second.Notes = "الجزء الثاني";
        second.DueDate = first.DueDate.AddSeconds(1);
        second.DependsOnFollowUpId = first.Id;
        var connectedAt = new DateTimeOffset(first.DueDate.AddMinutes(-5), TimeSpan.Zero);
        var sendHandler = new DeferredGatewaySendHandler(HttpStatusCode.ServiceUnavailable);
        db.AddRange(customer, first, second);
        await db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IConfiguration>(Configuration())
            .AddSingleton(ConnectedSessionClient(connectedAt))
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton(new WhatsAppConversationService(db))
            .AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(sendHandler))
            .AddSingleton(RejectingProxy.Create<IHubContext<NotificationHub>>())
            .AddSingleton<IProjectSecretVault, PlaintextTestVault>()
            .AddSingleton(RejectingProxy.Create<IAIMarketingBrain>())
            .BuildServiceProvider();

        await new FollowUpScheduler(services).CheckOverdueFollowUpsJobAsync();

        Assert.Equal(1, sendHandler.RequestCount);
        Assert.Equal("Pending", first.Status);
        Assert.Equal("Pending", second.Status);
        Assert.Equal(first.DueDate.AddSeconds(1), second.DueDate);
        Assert.Empty(db.Messages);
    }

    [Fact]
    public async Task Proactive_WhatsApp_follow_up_without_a_conversation_uses_its_selected_account()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var connectedAt = DateTimeOffset.UtcNow.AddMinutes(-10);
        await using var db = Context(projectId);
        var customer = Customer(projectId);
        var account = new WhatsAppAccount
        {
            Id = accountId,
            ProjectId = projectId,
            Name = "حساب المتابعة",
            IsDefault = false
        };
        var followUp = FollowUp(projectId, customer.Id);
        followUp.Channel = "WhatsApp";
        followUp.WhatsAppAccountId = accountId;
        followUp.Notes = "مرحباً، دي متابعة مجدولة من الحساب المختار.";
        var gateway = new AccountAffinityGatewayHandler(connectedAt);
        db.AddRange(customer, account, followUp);
        await db.SaveChangesAsync();
        using var services = new ServiceCollection()
            .AddSingleton(db)
            .AddSingleton<IConfiguration>(Configuration())
            .AddSingleton(new WhatsAppGatewaySessionClient(new HttpClient(gateway), Configuration()))
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton(new WhatsAppConversationService(db))
            .AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(gateway))
            .AddSingleton(RejectingProxy.Create<IHubContext<NotificationHub>>())
            .AddSingleton<IProjectSecretVault, PlaintextTestVault>()
            .AddSingleton(RejectingProxy.Create<IAIMarketingBrain>())
            .BuildServiceProvider();

        await new FollowUpScheduler(services).CheckOverdueFollowUpsJobAsync();

        Assert.Equal("Completed", followUp.Status);
        var conversation = Assert.Single(db.Conversations.IgnoreQueryFilters());
        Assert.Equal(projectId, conversation.ProjectId);
        Assert.Equal(customer.Id, conversation.CustomerId);
        Assert.Equal(accountId, conversation.WhatsAppAccountId);
        Assert.Equal("WhatsApp", conversation.Channel);
        var message = Assert.Single(db.Messages);
        Assert.Equal(conversation.Id, message.ConversationId);
        Assert.Equal("provider-followup-message", message.ExternalMessageId);
        Assert.NotEmpty(gateway.StatusRequestUris);
        Assert.All(gateway.StatusRequestUris, uri =>
            Assert.Contains($"whatsappAccountId={accountId}", uri.Query, StringComparison.Ordinal));
        using var payload = JsonDocument.Parse(Assert.IsType<string>(gateway.SendRequestBody));
        Assert.Equal(accountId, payload.RootElement.GetProperty("whatsappAccountId").GetGuid());
    }

    [Fact]
    public async Task Manual_follow_up_2026_08_31_regression_returns_unavailable_before_AI()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var customer = Customer(projectId);
        var followUp = FollowUp(projectId, customer.Id);
        db.AddRange(customer, followUp);
        await db.SaveChangesAsync();
        using var requestServices = new ServiceCollection()
            .AddSingleton(DisconnectedSessionClient())
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton(RejectingProxy.Create<IAIMarketingBrain>())
            .BuildServiceProvider();
        var controller = Controller(db, requestServices);

        var response = Assert.IsType<ObjectResult>(await controller.SendFollowUp(followUp.Id));

        Assert.Equal(StatusCodes.Status503ServiceUnavailable, response.StatusCode);
        Assert.Equal("Pending", followUp.Status);
    }

    [Fact]
    public async Task Bulk_follow_up_rewrite_2026_08_31_regression_skips_disconnected_accounts_before_AI()
    {
        var projectId = Guid.NewGuid();
        await using var db = Context(projectId);
        var customer = Customer(projectId);
        var followUp = FollowUp(projectId, customer.Id);
        db.AddRange(customer, followUp);
        await db.SaveChangesAsync();
        using var requestServices = new ServiceCollection()
            .AddSingleton(DisconnectedSessionClient())
            .AddSingleton(new WhatsAppAccountService(db))
            .AddSingleton(RejectingProxy.Create<IGeminiClient>())
            .BuildServiceProvider();
        var controller = Controller(db, requestServices);

        var response = await controller.ReEvaluateAllFollowUps(projectId);

        Assert.IsType<OkObjectResult>(response);
        Assert.Equal("Pending", followUp.Status);
    }

    [Fact]
    public async Task Gateway_status_2026_08_31_regression_fails_closed_on_transport_error()
    {
        var client = new WhatsAppGatewaySessionClient(
            new HttpClient(new ThrowingHttpHandler()),
            Configuration());

        var status = await client.GetAsync(Guid.NewGuid());

        Assert.False(status.Connected);
        Assert.Contains(nameof(HttpRequestException), status.Error);
    }

    [Fact]
    public async Task Gateway_status_exposes_the_current_connection_epoch_2026_09_01()
    {
        var connectedAt = DateTimeOffset.UtcNow;

        var status = await ConnectedSessionClient(connectedAt).GetAsync(Guid.NewGuid());

        Assert.True(status.Connected);
        Assert.Equal(connectedAt, status.ConnectedAt);
    }

    private static AppDbContext Context(Guid projectId)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
    }

    private static CRMController Controller(AppDbContext db, IServiceProvider requestServices)
    {
        var controller = new CRMController(
            db,
            RejectingProxy.Create<IEventBus>(),
            RejectingProxy.Create<ICustomerMemoryService>(),
            Configuration(),
            RejectingProxy.Create<IHubContext<NotificationHub>>(),
            new TenantContext(),
            new PlaintextTestVault(),
            new ProjectAuthorizationService());
        var userId = Guid.NewGuid();
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = requestServices,
                User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                {
                    new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                    new Claim(ClaimTypes.Role, "Owner"),
                    new Claim("ProjectId", db.CurrentProjectId.ToString())
                }, "Test"))
            }
        };
        return controller;
    }

    private static Customer Customer(Guid projectId) => new()
    {
        ProjectId = projectId,
        PhoneNumber = "201000000001",
        Name = "عميل",
        City = string.Empty,
        Notes = string.Empty
    };

    private static FollowUp FollowUp(Guid projectId, Guid customerId) => new()
    {
        ProjectId = projectId,
        CustomerId = customerId,
        DueDate = DateTime.UtcNow.AddMinutes(-5),
        Status = "Pending",
        Notes = "اكتب متابعة مناسبة",
        Type = "Nurturing"
    };

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["WhatsAppGateway:Url"] = "http://gateway.test"
        })
        .Build();

    private static WhatsAppGatewaySessionClient DisconnectedSessionClient() => new(
        new HttpClient(new RepeatingJsonHttpHandler(
            "{\"status\":\"Disconnected\",\"phoneNumber\":null,\"error\":null}")),
        Configuration());

    private static WhatsAppGatewaySessionClient ConnectedSessionClient(DateTimeOffset connectedAt) => new(
        new HttpClient(new RepeatingJsonHttpHandler(
            $"{{\"status\":\"Connected\",\"phoneNumber\":\"201000000000\",\"error\":null,\"connectedAt\":\"{connectedAt:O}\"}}")),
        Configuration());

    private sealed class RejectingCampaignAiService : ICampaignAIService
    {
        public Task<string> GenerateCampaignCopyAsync(string prompt, string baseTemplate, string targetContext) =>
            Task.FromException<string>(new InvalidOperationException("AI must not run while WhatsApp is disconnected."));

        public Task<string> GenerateProjectCampaignCopyAsync(
            Guid projectId,
            string prompt,
            string baseTemplate,
            string targetContext) =>
            Task.FromException<string>(new InvalidOperationException("AI must not run while WhatsApp is disconnected."));
    }

    private sealed class PlaintextTestVault : IProjectSecretVault
    {
        public bool IsProtected(string? storedValue) => false;
        public string Protect(Guid projectId, string secret) => secret;
        public string? Unprotect(Guid projectId, string? storedValue) => storedValue;
    }

    private class RejectingProxy : DispatchProxy
    {
        public static T Create<T>() where T : class => DispatchProxy.Create<T, RejectingProxy>();

        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException($"Unexpected call to {targetMethod?.DeclaringType?.Name}.{targetMethod?.Name}.");
    }

    private sealed class RepeatingJsonHttpHandler(string responseJson) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class DeferredGatewaySendHandler(HttpStatusCode responseStatus) : HttpMessageHandler
    {
        public string? RequestBody { get; private set; }
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(responseStatus)
            {
                Content = new StringContent(
                    "{\"code\":\"STALE_CONNECTION_EPOCH\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class AccountAffinityGatewayHandler(DateTimeOffset connectedAt) : HttpMessageHandler
    {
        public List<Uri> StatusRequestUris { get; } = [];
        public string? SendRequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                StatusRequestUris.Add(request.RequestUri!);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        $"{{\"status\":\"Connected\",\"phoneNumber\":\"201000000000\",\"error\":null,\"connectedAt\":\"{connectedAt:O}\"}}",
                        Encoding.UTF8,
                        "application/json")
                };
            }

            SendRequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"messageId\":\"provider-followup-message\",\"status\":\"Sent\"}",
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => throw new HttpRequestException("offline");
    }
}
