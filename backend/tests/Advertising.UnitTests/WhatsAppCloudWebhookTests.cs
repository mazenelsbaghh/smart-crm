using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modules.Advertising.Services;
using Modules.WhatsApp.API;
using Modules.WhatsApp.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class WhatsAppCloudWebhookTests
{
    [Fact]
    public void Verification_challenge_requires_the_configured_constant_time_token()
    {
        var setup = Setup();

        var accepted = setup.Controller.Verify("subscribe", "verify-me", "challenge-1");
        var rejected = setup.Controller.Verify("subscribe", "wrong", "challenge-1");

        Assert.Equal("challenge-1", Assert.IsType<ContentResult>(accepted).Content);
        Assert.IsType<ForbidResult>(rejected);
    }

    [Fact]
    public async Task Raw_signature_trusted_route_and_provider_message_id_are_required_and_deduplicated()
    {
        var setup = Setup();
        setup.Db.WhatsAppInboundRouteProjections.Add(new WhatsAppInboundRouteProjection
        {
            ProjectId = setup.ProjectId, DestinationId = Guid.NewGuid(), DestinationVersion = 3,
            Provider = "MetaWhatsApp", WabaExternalId = "waba-1", PhoneNumberExternalId = "phone-1", State = "Active"
        });
        await setup.Db.SaveChangesAsync();
        var body = "{\"entry\":[{\"id\":\"waba-1\",\"changes\":[{\"value\":{\"metadata\":{\"phone_number_id\":\"phone-1\"},\"messages\":[{\"id\":\"wamid.1\",\"from\":\"201000000000\",\"timestamp\":\"1787097600\",\"type\":\"text\",\"text\":{\"body\":\"مهتم\"},\"referral\":{\"ctwa_clid\":\"click-1\",\"source_id\":\"ad-1\"}}]}}]}]}";

        SetRequest(setup.Controller, body, Signature(body, "cloud-secret"));
        Assert.Equal(1, Value(Assert.IsType<OkObjectResult>(await setup.Controller.Receive(default)), "accepted"));
        SetRequest(setup.Controller, body, Signature(body, "cloud-secret"));
        Assert.Equal(0, Value(Assert.IsType<OkObjectResult>(await setup.Controller.Receive(default)), "accepted"));
        Assert.Single(await setup.Db.IntegrationOutboxMessages.ToListAsync());

        var wrongWaba = body.Replace("waba-1", "waba-other", StringComparison.Ordinal);
        SetRequest(setup.Controller, wrongWaba, Signature(wrongWaba, "cloud-secret"));
        var conflict = Assert.IsType<ConflictObjectResult>(await setup.Controller.Receive(default));
        Assert.Equal("ADS_WHATSAPP_ROUTE_NOT_FOUND", Value(conflict, "code"));

        SetRequest(setup.Controller, body, "sha256=00");
        Assert.IsType<UnauthorizedObjectResult>(await setup.Controller.Receive(default));
    }

    private static SetupState Setup()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options, tenant, new ServiceCollection().BuildServiceProvider());
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"wa-cloud-{Guid.NewGuid():N}"));
        IAdvertisingReferralProtector protector = new AdvertisingReferralProtector(DataProtectionProvider.Create(directory));
        var controller = new WhatsAppCloudWebhookController(db, Options.Create(new AdvertisingOptions
        {
            WhatsAppCloud = new WhatsAppCloudOptions { VerifyToken = "verify-me", AppSecret = "cloud-secret" }
        }), protector) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        return new(projectId, db, controller);
    }

    private static void SetRequest(ControllerBase controller, string body, string signature)
    {
        controller.ControllerContext.HttpContext = new DefaultHttpContext();
        controller.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        controller.Request.Headers["X-Hub-Signature-256"] = signature;
    }

    private static string Signature(string body, string secret) => "sha256=" + Convert.ToHexString(
        HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();
    private static int Value(OkObjectResult result, string property) => (int)(result.Value!.GetType().GetProperty(property)!.GetValue(result.Value)!);
    private static string Value(ConflictObjectResult result, string property) => (string)(result.Value!.GetType().GetProperty(property)!.GetValue(result.Value)!);
    private sealed record SetupState(Guid ProjectId, AppDbContext Db, WhatsAppCloudWebhookController Controller);
}
