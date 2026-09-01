using System.Collections.Concurrent;
using System.Net;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Modules.Advertising.Services;
using Modules.WhatsApp.API;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppEndpointSecurityTests
{
    [Fact]
    public async Task Missing_project_claim_is_rejected_without_reaching_the_gateway()
    {
        var gateway = new StubHttpClientFactory();
        var controller = Controller(
            "Development",
            new ClaimsPrincipal(new ClaimsIdentity()),
            gateway);
        var projectId = Guid.NewGuid();

        var responses = new IActionResult[]
        {
            await controller.StartSession(new StartSessionRequest { ProjectId = projectId }),
            await controller.GetQR(projectId),
            await controller.GetStatus(projectId),
            await controller.SendMessage(new SendMessageRequest
            {
                ProjectId = projectId,
                To = "+201000000000",
                Message = "test"
            }),
            await controller.MockSession(new MockSessionRequest
            {
                ProjectId = projectId,
                Status = "connected"
            }),
            await controller.GetMockSentMessages(),
            await controller.ClearMockSentMessages(),
            await controller.DisconnectSession(new DisconnectSessionRequest { ProjectId = projectId })
        };

        Assert.All(responses, response => Assert.IsType<ForbidResult>(response));
        Assert.Equal(0, gateway.RequestCount);
    }

    [Fact]
    public async Task Mock_gateway_is_hidden_outside_development()
    {
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Production", User("Owner", Guid.NewGuid()), gateway);

        var response = await controller.GetMockSentMessages();

        Assert.IsType<NotFoundResult>(response);
        Assert.Equal(0, gateway.RequestCount);
    }

    [Fact]
    public async Task Cross_project_owner_cannot_open_mock_session()
    {
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Owner", Guid.NewGuid()), gateway);

        var response = await controller.MockSession(new MockSessionRequest
        {
            ProjectId = Guid.NewGuid(),
            Status = "connected"
        });

        Assert.IsType<ForbidResult>(response);
        Assert.Equal(0, gateway.RequestCount);
    }

    [Fact]
    public async Task Cross_project_requests_are_rejected_before_reaching_the_gateway()
    {
        var claimedProjectId = Guid.NewGuid();
        var requestedProjectId = Guid.NewGuid();
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Owner", claimedProjectId), gateway);

        var responses = await Task.WhenAll(
            controller.StartSession(new StartSessionRequest { ProjectId = requestedProjectId }),
            controller.GetQR(requestedProjectId),
            controller.GetStatus(requestedProjectId),
            controller.SendMessage(new SendMessageRequest
            {
                ProjectId = requestedProjectId,
                To = "+201000000000",
                Message = "test"
            }),
            controller.DisconnectSession(new DisconnectSessionRequest { ProjectId = requestedProjectId }));

        Assert.All(responses, response => Assert.IsType<ForbidResult>(response));
        Assert.Equal(0, gateway.RequestCount);
    }

    [Fact]
    public async Task Agent_can_read_status_and_send_only_inside_the_claimed_project()
    {
        var projectId = Guid.NewGuid();
        var controller = Controller("Development", User("Agent", projectId));

        var responses = await Task.WhenAll(
            controller.GetStatus(projectId),
            controller.SendMessage(new SendMessageRequest
            {
                ProjectId = projectId,
                To = "+201000000000",
                Message = "test",
                IdempotencyKey = "agent-send",
                ExpectedConnectedAt = DateTimeOffset.UtcNow
            }));

        Assert.All(responses, response => Assert.Equal(StatusCodes.Status200OK,
            Assert.IsType<ObjectResult>(response).StatusCode));
    }

    [Fact]
    public async Task Agent_cannot_start_read_qr_or_disconnect_a_session()
    {
        var projectId = Guid.NewGuid();
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Agent", projectId), gateway);

        var responses = await Task.WhenAll(
            controller.StartSession(new StartSessionRequest { ProjectId = projectId }),
            controller.GetQR(projectId),
            controller.DisconnectSession(new DisconnectSessionRequest { ProjectId = projectId }));

        Assert.All(responses, response => Assert.IsType<ForbidResult>(response));
        Assert.Equal(0, gateway.RequestCount);
    }

    [Theory]
    [InlineData(GatewayOperation.Start)]
    [InlineData(GatewayOperation.Qr)]
    [InlineData(GatewayOperation.Status)]
    [InlineData(GatewayOperation.Send)]
    [InlineData(GatewayOperation.Mock)]
    [InlineData(GatewayOperation.Disconnect)]
    public async Task Account_from_another_project_is_rejected_before_gateway_dispatch(
        GatewayOperation operation)
    {
        var projectId = Guid.NewGuid();
        await using var db = Database();
        var foreignAccount = new WhatsAppAccount
        {
            ProjectId = Guid.NewGuid(),
            Name = "Foreign account",
            IsDefault = true
        };
        db.WhatsAppAccounts.Add(foreignAccount);
        await db.SaveChangesAsync();
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Owner", projectId), gateway, db);

        var response = await InvokeGatewayOperation(
            controller,
            operation,
            projectId,
            foreignAccount.Id);

        Assert.IsType<NotFoundObjectResult>(response);
        Assert.Equal(0, gateway.RequestCount);
    }

    [Theory]
    [InlineData(GatewayOperation.Start)]
    [InlineData(GatewayOperation.Send)]
    [InlineData(GatewayOperation.Mock)]
    [InlineData(GatewayOperation.Disconnect)]
    public async Task Account_specific_body_dispatch_uses_the_exact_gateway_field(
        GatewayOperation operation)
    {
        var projectId = Guid.NewGuid();
        await using var db = Database();
        var account = await AddAccountAsync(db, projectId, isDefault: false);
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Owner", projectId), gateway, db);

        var response = await InvokeGatewayOperation(controller, operation, projectId, account.Id);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(response).StatusCode);
        var request = Assert.Single(gateway.Requests);
        using var document = JsonDocument.Parse(Assert.IsType<string>(request.Body));
        Assert.Equal(account.Id, document.RootElement.GetProperty("whatsappAccountId").GetGuid());
        Assert.False(document.RootElement.TryGetProperty("whatsAppAccountId", out _));
    }

    [Theory]
    [InlineData(GatewayOperation.Qr)]
    [InlineData(GatewayOperation.Status)]
    public async Task Account_specific_query_dispatch_uses_the_exact_gateway_parameter(
        GatewayOperation operation)
    {
        var projectId = Guid.NewGuid();
        await using var db = Database();
        var account = await AddAccountAsync(db, projectId, isDefault: false);
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Owner", projectId), gateway, db);

        var response = await InvokeGatewayOperation(controller, operation, projectId, account.Id);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(response).StatusCode);
        var query = Assert.Single(gateway.Requests).Uri.Query;
        Assert.Contains($"whatsappAccountId={account.Id}", query, StringComparison.Ordinal);
        Assert.False(query.Contains("whatsAppAccountId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Missing_account_resolves_to_the_legacy_default_session()
    {
        var projectId = Guid.NewGuid();
        await using var db = Database();
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Owner", projectId), gateway, db);

        var startResponse = await controller.StartSession(new StartSessionRequest { ProjectId = projectId });
        var statusResponse = await controller.GetStatus(projectId);

        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(startResponse).StatusCode);
        Assert.Equal(StatusCodes.Status200OK, Assert.IsType<ObjectResult>(statusResponse).StatusCode);
        var defaultAccount = await db.WhatsAppAccounts.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(projectId, defaultAccount.Id);
        Assert.True(defaultAccount.IsDefault);
        Assert.Equal(2, gateway.Requests.Count);
        using var startBody = JsonDocument.Parse(Assert.IsType<string>(gateway.Requests[0].Body));
        Assert.Equal(JsonValueKind.Null, startBody.RootElement.GetProperty("whatsappAccountId").ValueKind);
        Assert.False(gateway.Requests[1].Uri.Query.Contains("whatsappAccountId", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Owner_can_create_list_and_promote_an_account_to_default()
    {
        var projectId = Guid.NewGuid();
        await using var db = Database();
        var gateway = new StubHttpClientFactory();
        var controller = Controller("Development", User("Owner", projectId), gateway, db);

        var created = Assert.IsType<CreatedResult>(await controller.CreateAccount(
            new CreateWhatsAppAccountRequest { ProjectId = projectId, Name = "Sales line" }));
        var newAccount = await db.WhatsAppAccounts.IgnoreQueryFilters()
            .SingleAsync(account => account.Id != projectId);
        var updated = await controller.UpdateAccount(newAccount.Id, new UpdateWhatsAppAccountRequest
        {
            ProjectId = projectId,
            Name = "VIP sales",
            IsDefault = true
        });
        var listed = Assert.IsType<OkObjectResult>(await controller.GetAccounts(projectId));

        Assert.NotNull(created.Value);
        Assert.IsType<OkObjectResult>(updated);
        Assert.Equal("VIP sales", newAccount.Name);
        Assert.True(newAccount.IsDefault);
        Assert.False(await db.WhatsAppAccounts.IgnoreQueryFilters()
            .Where(account => account.Id == projectId)
            .Select(account => account.IsDefault)
            .SingleAsync());
        var accounts = JsonSerializer.SerializeToElement(
            listed.Value,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)).EnumerateArray().ToArray();
        Assert.Equal(2, accounts.Length);
        Assert.Equal(newAccount.Id, accounts[0].GetProperty("id").GetGuid());
        Assert.True(accounts[0].GetProperty("isDefault").GetBoolean());
        Assert.Equal(0, gateway.RequestCount);
    }

    [Fact]
    public async Task Agent_can_list_accounts_but_cannot_create_or_update_them()
    {
        var projectId = Guid.NewGuid();
        await using var db = Database();
        var account = await AddAccountAsync(db, projectId, isDefault: true);
        var controller = Controller("Development", User("Agent", projectId), dbContext: db);

        var listed = await controller.GetAccounts(projectId);
        var created = await controller.CreateAccount(new CreateWhatsAppAccountRequest
        {
            ProjectId = projectId,
            Name = "Blocked"
        });
        var updated = await controller.UpdateAccount(account.Id, new UpdateWhatsAppAccountRequest
        {
            ProjectId = projectId,
            Name = "Blocked",
            IsDefault = true
        });

        Assert.IsType<OkObjectResult>(listed);
        Assert.IsType<ForbidResult>(created);
        Assert.IsType<ForbidResult>(updated);
        Assert.Equal("Account", account.Name);
        Assert.Single(await db.WhatsAppAccounts.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Session_client_keeps_legacy_status_wire_and_adds_account_specific_wire()
    {
        var projectId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var gateway = new StubHttpClientFactory();
        using var gatewayHttpClient = gateway.CreateClient(string.Empty);
        var client = new WhatsAppGatewaySessionClient(
            gatewayHttpClient,
            new ConfigurationBuilder().Build());

        var legacyStatus = await client.GetAsync(projectId);
        var accountStatus = await client.GetAsync(projectId, accountId);

        Assert.True(legacyStatus.Connected);
        Assert.True(accountStatus.Connected);
        Assert.False(gateway.Requests[0].Uri.Query.Contains("whatsappAccountId", StringComparison.Ordinal));
        Assert.Contains($"whatsappAccountId={accountId}", gateway.Requests[1].Uri.Query, StringComparison.Ordinal);
    }

    private static Task<IActionResult> InvokeGatewayOperation(
        WhatsAppController controller,
        GatewayOperation operation,
        Guid projectId,
        Guid? accountId) => operation switch
    {
        GatewayOperation.Start => controller.StartSession(new StartSessionRequest
        {
            ProjectId = projectId,
            WhatsAppAccountId = accountId
        }),
        GatewayOperation.Qr => controller.GetQR(projectId, accountId),
        GatewayOperation.Status => controller.GetStatus(projectId, accountId),
        GatewayOperation.Send => controller.SendMessage(new SendMessageRequest
        {
            ProjectId = projectId,
            WhatsAppAccountId = accountId,
            To = "+201000000000",
            Message = "test",
            IdempotencyKey = "account-send",
            ExpectedConnectedAt = DateTimeOffset.UtcNow
        }),
        GatewayOperation.Mock => controller.MockSession(new MockSessionRequest
        {
            ProjectId = projectId,
            WhatsAppAccountId = accountId,
            Status = "Connected"
        }),
        GatewayOperation.Disconnect => controller.DisconnectSession(new DisconnectSessionRequest
        {
            ProjectId = projectId,
            WhatsAppAccountId = accountId
        }),
        _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, null)
    };

    private static async Task<WhatsAppAccount> AddAccountAsync(
        AppDbContext db,
        Guid projectId,
        bool isDefault)
    {
        var account = new WhatsAppAccount
        {
            ProjectId = projectId,
            Name = "Account",
            IsDefault = isDefault
        };
        db.WhatsAppAccounts.Add(account);
        await db.SaveChangesAsync();
        return account;
    }

    private static WhatsAppController Controller(
        string environmentName,
        ClaimsPrincipal user,
        StubHttpClientFactory? httpClientFactory = null,
        AppDbContext? dbContext = null)
    {
        dbContext ??= Database();
        var controller = new WhatsAppController(
            new ConfigurationBuilder().Build(),
            new TestHostEnvironment { EnvironmentName = environmentName },
            new ProjectAuthorizationService(),
            httpClientFactory ?? new StubHttpClientFactory(),
            new WhatsAppAccountService(dbContext));
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };
        return controller;
    }

    private static AppDbContext Database()
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(Guid.NewGuid());
        return new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options,
            tenant,
            new ServiceCollection().BuildServiceProvider());
    }

    private static ClaimsPrincipal User(string role, Guid projectId) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.Role, role),
        new Claim("ProjectId", projectId.ToString())
    ], "test"));

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Advertising.UnitTests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    public enum GatewayOperation
    {
        Start,
        Qr,
        Status,
        Send,
        Mock,
        Disconnect
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly JsonResponseHandler _handler = new();

        public int RequestCount => _handler.RequestCount;
        public IReadOnlyList<RecordedRequest> Requests => _handler.Requests;

        public HttpClient CreateClient(string name) => new(_handler, disposeHandler: false);
    }

    private sealed class JsonResponseHandler : HttpMessageHandler
    {
        private int _requestCount;
        private readonly ConcurrentQueue<RecordedRequest> _requests = new();

        public int RequestCount => _requestCount;
        public IReadOnlyList<RecordedRequest> Requests => _requests.ToArray();

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requestCount);
            var body = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            _requests.Enqueue(new(request.Method, request.RequestUri!, body));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"status\":\"Connected\",\"phoneNumber\":\"201000000000\"}")
            };
        }
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Body);
}
