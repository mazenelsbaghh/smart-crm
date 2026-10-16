using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Services;
using Modules.WhatsApp.Domain;
using Modules.WhatsApp.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppBookingNumberVerifierTests
{
    [Theory]
    [InlineData("registered", WhatsAppNumberVerification.Registered)]
    [InlineData("absent", WhatsAppNumberVerification.NotRegistered)]
    [InlineData("unknown", WhatsAppNumberVerification.Unavailable)]
    [InlineData("offline", WhatsAppNumberVerification.Unavailable)]
    public async Task Registration_uses_a_connected_project_account_and_never_confuses_outages_with_an_absent_number(
        string scenario, WhatsAppNumberVerification expected)
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, new TenantContext(), services);
        var projectId = Guid.NewGuid();
        var disconnectedDefault = new WhatsAppAccount { ProjectId = projectId, IsDefault = true, Name = "default" };
        var connectedAccount = new WhatsAppAccount { ProjectId = projectId, Name = "connected" };
        var anotherProject = new WhatsAppAccount { ProjectId = Guid.NewGuid(), Name = "other" };
        db.AddRange(disconnectedDefault, connectedAccount, anotherProject);
        await db.SaveChangesAsync();
        using var boundary = new GatewayBoundary(scenario, connectedAccount.Id);
        using var statusHttp = new HttpClient(boundary, disposeHandler: false);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["WhatsAppGateway:Url"] = "http://gateway.test" }).Build();
        var verifier = new WhatsAppBookingNumberVerifier(new(db), new(statusHttp, configuration),
            new GatewayClients(boundary), configuration);

        var result = await verifier.VerifyAsync(projectId, "201033334444");

        Assert.Equal(expected, result);
        Assert.Equal(scenario == "offline" ? 0 : 1, boundary.Checks.Count);
        if (boundary.Checks.Count == 1)
        {
            using var payload = JsonDocument.Parse(boundary.Checks[0]);
            Assert.Equal(projectId.ToString(), payload.RootElement.GetProperty("projectId").GetString());
            Assert.Equal(connectedAccount.Id.ToString(), payload.RootElement.GetProperty("whatsappAccountId").GetString());
            Assert.Equal("201033334444", payload.RootElement.GetProperty("phone").GetString());
        }
    }

    private sealed class GatewayClients(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class GatewayBoundary(string scenario, Guid connectedAccount) : HttpMessageHandler
    {
        public List<string> Checks { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method == HttpMethod.Get)
            {
                var connected = scenario != "offline" && request.RequestUri!.Query.Contains(connectedAccount.ToString());
                return Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
                { status = connected ? "Connected" : "Disconnected", phoneNumber = connected ? "201100000000" : null, connectedAt = connected ? (DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(-1) : null }));
            }
            Checks.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return scenario == "unknown" ? Json(HttpStatusCode.ServiceUnavailable, "{}")
                : Json(HttpStatusCode.OK, JsonSerializer.Serialize(new { exists = scenario == "registered" }));
        }
        private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
        { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
