using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Auth.Domain;
using Modules.AI.Services;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Jobs;
using Modules.Approvals.Domain;
using Modules.Approvals.Services;
using Modules.Conversations.Domain;
using Modules.Conversations.Hubs;
using Modules.Facebook.Services;
using Modules.Projects.Domain;
using Modules.Users.API;
using Shared.Infrastructure;
using Shared.Security;
using StackExchange.Redis;
using Xunit;

namespace Advertising.UnitTests;

public sealed class ProjectBoundarySecurityTests : IAsyncLifetime
{
    private WebApplication app = null!;
    private HttpClient client = null!;
    private readonly Guid projectId = Guid.NewGuid();
    private readonly string databaseName = Guid.NewGuid().ToString();
    private readonly IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(
        new Dictionary<string, string?> {
            ["JWT:Secret"] = "boundary-regression-tests-independent-signing-secret-123456789",
            ["FACEBOOK_APP_ID"] = "test-app", ["FACEBOOK_APP_SECRET"] = "test-secret",
            ["FACEBOOK_OAUTH_REDIRECT_URI"] = "https://app.example.test/api/facebook/oauth/callback"
        }).Build();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        { EnvironmentName = "Test", ApplicationName = typeof(UsersController).Assembly.FullName });
        builder.WebHost.UseTestServer();
        builder.Services.AddProjectSecurity(configuration);
        builder.Services.AddSignalR();
        builder.Services.AddScoped<ITenantContext, TenantContext>();
        builder.Services.AddScoped<IProjectAuthorizationService, ProjectAuthorizationService>();
        builder.Services.AddScoped<IPasswordHasher, BCryptPasswordHasher>();
        builder.Services.AddSingleton<IJwtService>(new JwtService(configuration));
        builder.Services.AddScoped<IRiskAnalyzer, RiskAnalyzer>();
        builder.Services.AddSingleton(ExternalBoundary.Create<IConnectionMultiplexer>());
        builder.Services.AddSingleton<IFacebookOAuthService>(new FacebookOAuthService(configuration,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<FacebookOAuthService>.Instance));
        builder.Services.AddSingleton(ExternalBoundary.Create<IFacebookGraphService>());
        builder.Services.AddSingleton(ExternalBoundary.Create<IGeminiClient>());
        builder.Services.AddSingleton(ExternalBoundary.Create<IProjectSecretVault>());
        builder.Services.AddScoped<IAnalyticsEngine, AnalyticsEngine>();
        builder.Services.AddScoped<DailyAnalyticsJob>();
        builder.Services.AddScoped<SalesIntelligenceService>();
        builder.Services.AddScoped<ConversationSalesAnalyzer>();
        builder.Services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        app = builder.Build();
        app.UseRouting();
        app.UseAuthentication();
        app.UseMiddleware<TenantMiddleware>();
        app.UseAuthorization();
        app.MapControllers();
        app.MapHub<NotificationHub>("/hubs/notifications");
        await app.StartAsync();
        client = app.GetTestClient();
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Projects.Add(new Project { Id = projectId, Name = "Security regression" });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData("POST", "/api/projects/{project}/users/invite")]
    [InlineData("POST", "/api/projects/{project}/pipelines/stages")]
    [InlineData("GET", "/api/projects/{project}/knowledge")]
    [InlineData("GET", "/api/projects/{project}/search?q=customer")]
    [InlineData("POST", "/hubs/notifications/negotiate?negotiateVersion=1")]
    public async Task Anonymous_requests_cannot_reach_administration_or_live_conversations(string method, string path)
    {
        using var response = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method),
            path.Replace("{project}", projectId.ToString())) { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Agent", false)]
    [InlineData("Analyst", false)]
    public async Task Only_project_administrators_can_invite_users(string role, bool allowed)
    {
        var session = await SignInAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        using var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/users/invite",
            new { email = "invited@example.test", password = "test-password", role = "Agent" });
        Assert.Equal(allowed ? HttpStatusCode.Created : HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(allowed, await db.Users.IgnoreQueryFilters().AnyAsync(user => user.Email == "invited@example.test"));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Other_project_cannot_be_selected_by_header_or_route(bool spoofHeader)
    {
        var session = await SignInAsync("Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var otherProject = Guid.NewGuid();
        if (spoofHeader) client.DefaultRequestHeaders.Add("X-Project-Id", otherProject.ToString());
        using var response = await client.GetAsync($"/api/projects/{(spoofHeader ? projectId : otherProject)}/users");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("logout")]
    [InlineData("role-changed")]
    [InlineData("user-deleted")]
    public async Task Revoked_or_changed_accounts_cannot_reuse_their_signed_access_token(string change)
    {
        var session = await SignInAsync("Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (change == "logout")
        {
            using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = session.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        }
        else
        {
            var user = await db.Users.IgnoreQueryFilters().SingleAsync(candidate => candidate.Id == session.UserId);
            if (change == "role-changed") user.Role = "Analyst";
            else db.Users.Remove(user);
            await db.SaveChangesAsync();
        }
        using var response = await client.GetAsync($"/api/projects/{projectId}/users");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Read_only_account_cannot_create_crm_records()
    {
        var session = await SignInAsync("Analyst");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        using var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/pipelines/stages", new { name = "Unauthorized" });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = app.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().PipelineStages.IgnoreQueryFilters().AnyAsync());
    }

    [Theory]
    [InlineData("Owner", true)]
    [InlineData("Admin", true)]
    [InlineData("Supervisor", true)]
    [InlineData("Agent", false)]
    [InlineData("AI Reviewer", false)]
    [InlineData("Analyst", false)]
    public async Task High_risk_approval_requires_supervisory_authority_and_records_the_authenticated_actor(string role, bool allowed)
    {
        var session = await SignInAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var customer = new Customer { ProjectId = projectId, Name = "Approval test", PhoneNumber = "approval-test", City = "Cairo", Budget = 100 };
        var approval = new ApprovalRequest { ProjectId = projectId, ActionType = "CRMUpdate", RiskLevel = "High",
            PayloadJson = JsonSerializer.Serialize(new { customerId = customer.Id, budget = 2000000 }) };
        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AddRange(customer, approval);
            await db.SaveChangesAsync();
        }

        using var response = await client.PostAsJsonAsync($"/api/approvals/{approval.Id}/approve", new { });

        Assert.Equal(allowed ? HttpStatusCode.OK : HttpStatusCode.Forbidden, response.StatusCode);
        using var verificationScope = app.Services.CreateScope();
        var persisted = verificationScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var savedApproval = await persisted.ApprovalRequests.IgnoreQueryFilters().SingleAsync(row => row.Id == approval.Id);
        var savedCustomer = await persisted.Customers.IgnoreQueryFilters().SingleAsync(row => row.Id == customer.Id);
        Assert.Equal(allowed ? "Approved" : "Pending", savedApproval.Status);
        Assert.Equal(allowed ? session.UserId.ToString() : null, savedApproval.ApprovedBy);
        Assert.Equal(allowed ? 2000000m : 100m, savedCustomer.Budget);
        if (!allowed)
        {
            using var reject = await client.PostAsJsonAsync($"/api/approvals/{approval.Id}/reject", new { });
            Assert.Equal(HttpStatusCode.Forbidden, reject.StatusCode);
        }
    }

    [Fact]
    public async Task High_risk_request_does_not_trust_a_client_supplied_operator_name()
    {
        var session = await SignInAsync("Agent");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        using var response = await client.PostAsJsonAsync($"/api/projects/{projectId}/actions/execute", new
        {
            actionType = "CRMUpdate", payloadJson = "{\"budget\":2000000}", requestedBy = "Supervisor"
        });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var scope = app.Services.CreateScope();
        var approval = await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .ApprovalRequests.IgnoreQueryFilters().SingleAsync();
        Assert.Equal(session.UserId.ToString(), approval.RequestedBy);
        Assert.Equal("Pending", approval.Status);
    }

    private async Task<(string Token, string RefreshToken, Guid UserId)> SignInAsync(string role)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = new User { ProjectId = projectId, Email = "current@example.test", Role = role, PasswordHash = "unused" };
        var refresh = new RefreshToken { UserId = user.Id, ProjectId = projectId, Token = Guid.NewGuid().ToString(), ExpiresAt = DateTime.UtcNow.AddDays(1) };
        db.AddRange(user, refresh);
        await db.SaveChangesAsync();
        return (new JwtService(configuration).GenerateToken(new(user.Id, user.Email, role, projectId, refresh.Id)), refresh.Token, user.Id);
    }

    [Theory]
    [InlineData(null, false, HttpStatusCode.Unauthorized)]
    [InlineData("Agent", false, HttpStatusCode.Forbidden)]
    [InlineData("Owner", true, HttpStatusCode.Forbidden)]
    [InlineData("Owner", false, HttpStatusCode.OK)]
    [InlineData("Admin", false, HttpStatusCode.OK)]
    public async Task Facebook_login_returns_a_navigation_url_only_to_the_authorized_project_admin(
        string? role, bool otherProject, HttpStatusCode expectedStatus)
    {
        if (role is not null)
        {
            var session = await SignInAsync(role);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        }
        using var response = await client.GetAsync($"/api/facebook/oauth/login?projectId={(otherProject ? Guid.NewGuid() : projectId)}");
        Assert.Equal(expectedStatus, response.StatusCode);
        if (expectedStatus != HttpStatusCode.OK) return;

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var url = new Uri(body.RootElement.GetProperty("authorizationUrl").GetString()!);
        Assert.Null(response.Headers.Location);
        Assert.Equal("www.facebook.com", url.Host);
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(url.Query);
        Assert.StartsWith($"{projectId}:", query["state"].ToString());
        Assert.DoesNotContain("access_token", url.Query);
    }

    [Theory]
    [InlineData("Analyst")]
    [InlineData("AI Reviewer")]
    public async Task Read_only_roles_can_generate_reports_and_reach_analyst_question_validation(string role)
    {
        var session = await SignInAsync(role);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        using var report = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports/generate", new { reportType = "weekly" });
        Assert.Equal(HttpStatusCode.OK, report.StatusCode);

        var boundary = DateTime.UtcNow.AddDays(-1);
        using var question = await client.PostAsJsonAsync($"/api/projects/{projectId}/reports/sales-intelligence/ask",
            new { question = "ما نتائج المبيعات؟", fromUtc = boundary, toUtc = boundary });
        Assert.Equal(HttpStatusCode.BadRequest, question.StatusCode);
        using var answer = JsonDocument.Parse(await question.Content.ReadAsStringAsync());
        Assert.Equal("بداية الفترة يجب أن تسبق نهايتها.", answer.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task Refresh_keeps_the_session_family_active_and_logout_revokes_the_whole_family()
    {
        var session = await SignInAsync("Owner");
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        using var refresh = await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        using var stillActive = await client.GetAsync($"/api/projects/{projectId}/users");
        Assert.Equal(HttpStatusCode.OK, stillActive.StatusCode);
        using var logout = await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = session.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, logout.StatusCode);
        using var denied = await client.GetAsync($"/api/projects/{projectId}/users");
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
    }

    [Fact]
    public async Task Authenticated_socket_cannot_subscribe_to_or_receive_another_projects_events()
    {
        var session = await SignInAsync("Owner");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var sockets = app.GetTestServer().CreateWebSocketClient();
        using var socket = await sockets.ConnectAsync(new Uri(
            $"ws://localhost/hubs/notifications?projectId={projectId}&access_token={session.Token}"), timeout.Token);
        await SendHubAsync(socket, "{\"protocol\":\"json\",\"version\":1}", timeout.Token);
        using var handshake = await ReceiveHubAsync(socket, timeout.Token);
        Assert.False(handshake.RootElement.TryGetProperty("error", out _));

        var otherProject = Guid.NewGuid();
        await SendHubAsync(socket, JsonSerializer.Serialize(new { type = 1, invocationId = "join-other",
            target = "JoinProjectGroup", arguments = new[] { otherProject.ToString() } }), timeout.Token);
        using var rejected = await ReceiveHubAsync(socket, timeout.Token);
        Assert.Equal("join-other", rejected.RootElement.GetProperty("invocationId").GetString());
        Assert.Contains("Project access denied", rejected.RootElement.GetProperty("error").GetString());

        var hub = app.Services.GetRequiredService<IHubContext<NotificationHub>>();
        await hub.Clients.Group($"project_{otherProject}").SendAsync("SecurityProbe", "other", timeout.Token);
        await hub.Clients.Group($"project_{projectId}").SendAsync("SecurityProbe", "own", timeout.Token);
        using var delivered = await ReceiveHubAsync(socket, timeout.Token);
        Assert.Equal("SecurityProbe", delivered.RootElement.GetProperty("target").GetString());
        Assert.Equal("own", delivered.RootElement.GetProperty("arguments")[0].GetString());
    }

    [Theory]
    [InlineData("</script><img src=x onerror=alert(1)>")]
    [InlineData("';window.location='https://attacker.invalid';//")]
    public async Task Facebook_callback_renders_provider_errors_without_injecting_HTML_or_JavaScript(string error)
    {
        using var response = await client.GetAsync("/api/facebook/oauth/callback?error_description=" + Uri.EscapeDataString(error));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var html = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(error, html);
        Assert.Single(Regex.Matches(html, "<script>"));
        Assert.Single(Regex.Matches(html, "</script>"));
        var payload = Regex.Match(html, @"const payload = (.+);").Groups[1].Value;
        using var parsed = JsonDocument.Parse(payload);
        Assert.Equal(error, parsed.RootElement.GetProperty("error").GetString());
    }

    private static Task SendHubAsync(WebSocket socket, string message, CancellationToken cancellationToken) =>
        socket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(message + '\u001e')),
            WebSocketMessageType.Text, true, cancellationToken);

    private static async Task<JsonDocument> ReceiveHubAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var message = new MemoryStream();
        var buffer = new byte[4096];
        WebSocketReceiveResult received;
        do
        {
            received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
            Assert.Equal(WebSocketMessageType.Text, received.MessageType);
            message.Write(buffer, 0, received.Count);
        } while (!received.EndOfMessage);
        return JsonDocument.Parse(Encoding.UTF8.GetString(message.ToArray()).Split('\u001e')[0]);
    }

    private class ExternalBoundary : DispatchProxy
    {
        public static T Create<T>() where T : class => DispatchProxy.Create<T, ExternalBoundary>();
        protected override object? Invoke(MethodInfo? method, object?[]? arguments) => method?.Name switch
        {
            nameof(IConnectionMultiplexer.GetDatabase) => Create<IDatabase>(),
            nameof(IDatabase.StringSetAsync) => Task.FromResult(true),
            _ => throw new InvalidOperationException("This scenario must not call an external provider.")
        };
    }

    public async Task DisposeAsync()
    {
        client.Dispose();
        await app.DisposeAsync();
    }
}
