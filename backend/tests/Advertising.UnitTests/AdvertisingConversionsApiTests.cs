using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingConversionsApiTests
{
    [Fact]
    public void Public_api_exposes_final_webhook_business_messaging_and_tracking_routes()
    {
        var sourceRoutes = typeof(Modules.Advertising.API.AdvertisingConversionSourcesController)
            .GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>().Select(item => item.Template).ToArray();
        var conversionMethods = typeof(Modules.Advertising.API.AdvertisingConversionsController).GetMethods();
        var actionRoutes = conversionMethods.SelectMany(method => method.GetCustomAttributes(true))
            .OfType<HttpMethodAttribute>().Select(item => item.Template).Where(item => item is not null)
            .Select(item => item!).ToArray();
        var cloudRoute = Assert.Single(typeof(Modules.WhatsApp.API.WhatsAppCloudWebhookController)
            .GetCustomAttributes(typeof(RouteAttribute), true).Cast<RouteAttribute>()).Template;

        Assert.Contains("api/projects/{projectId:guid}/ad-manager/webhook-sources", sourceRoutes);
        Assert.Contains("~/api/projects/{projectId:guid}/ad-manager/business-messaging/readiness", actionRoutes);
        Assert.Contains("~/api/projects/{projectId:guid}/ad-manager/business-messaging/test", actionRoutes);
        Assert.Contains("~/api/projects/{projectId:guid}/ad-manager/tracking-health", actionRoutes);
        Assert.Equal("api/integrations/whatsapp/cloud", cloudRoute);
    }

    [Fact]
    public async Task Signing_secret_is_shown_once_rotated_with_short_overlap_and_destroyed_on_revoke()
    {
        var setup = Setup();
        var sources = new AdvertisingWebhookSourceService(setup.Db, setup.Vault);
        var created = await sources.CreateAsync(setup.ProjectId, "orders", ["Purchase", "Refund"]);
        var rotated = await sources.RotateAsync(setup.ProjectId, created.Source.Id);

        Assert.NotEqual(created.SigningSecret, rotated.SigningSecret);
        Assert.NotNull(rotated.Source.PreviousProtectedSigningSecret);
        Assert.True(rotated.Source.OverlapEndsAtUtc > DateTime.UtcNow);

        await sources.RevokeAsync(setup.ProjectId, created.Source.Id);
        Assert.False(created.Source.IsActive);
        Assert.Equal(string.Empty, created.Source.ProtectedSigningSecret);
        Assert.Null(created.Source.PreviousProtectedSigningSecret);
    }

    [Fact]
    public async Task Signed_refund_before_purchase_is_pending_then_recomputed_and_duplicate_is_idempotent()
    {
        var setup = Setup();
        var source = await new AdvertisingWebhookSourceService(setup.Db, setup.Vault)
            .CreateAsync(setup.ProjectId, "orders", ["Purchase", "Refund"]);
        var ingress = new ConversionIngressService(setup.Db, setup.Vault);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var refund = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, externalEventId = "refund-1", originalExternalEventId = "order-1",
            eventType = "Refund", occurredAtUtc = DateTime.UtcNow, value = 40m, currency = "EGP",
            customer = new { externalId = "customer-1" }, privacy = new { consentState = "Granted", legalBasis = "contract" }
        });
        var refundResult = await ingress.IngestAsync(setup.ProjectId, "orders", timestamp,
            "v1=" + ConversionSecurity.Sign(source.SigningSecret, timestamp, refund), refund, default);
        Assert.Equal(CorrectionState.PendingBase, (await setup.Db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync()).CorrectionState);

        var purchase = JsonSerializer.Serialize(new
        {
            schemaVersion = 1, externalEventId = "order-1", eventType = "Purchase",
            occurredAtUtc = DateTime.UtcNow.AddMinutes(-5), value = 100m, currency = "EGP",
            customer = new { externalId = "customer-1" }, privacy = new { consentState = "Granted", legalBasis = "contract" }
        });
        var purchaseResult = await ingress.IngestAsync(setup.ProjectId, "orders", timestamp,
            "v1=" + ConversionSecurity.Sign(source.SigningSecret, timestamp, purchase), purchase, default);
        var duplicate = await ingress.IngestAsync(setup.ProjectId, "orders", timestamp,
            "v1=" + ConversionSecurity.Sign(source.SigningSecret, timestamp, refund), refund, default);
        var conversion = await setup.Db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(refundResult.ConversionId, purchaseResult.ConversionId);
        Assert.Equal(60m, conversion.CurrentValue);
        Assert.Equal(CorrectionState.Corrected, conversion.CorrectionState);
        Assert.True(duplicate.Duplicate);
    }

    [Fact]
    public async Task Schema_v2_groups_outcomes_by_business_aggregate_and_ignores_unverified_raw_ctwa()
    {
        var setup = Setup();
        var source = await new AdvertisingWebhookSourceService(setup.Db, setup.Vault)
            .CreateAsync(setup.ProjectId, "orders-v2", ["Purchase"]);
        var ingress = new ConversionIngressService(setup.Db, setup.Vault);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var body = JsonSerializer.Serialize(new
        {
            schemaVersion = 2, externalEventId = "payment-2", eventType = "Purchase",
            businessAggregate = new { type = "Order", id = "order-42" }, journeyLocation = "WhatsAppThread",
            occurredAtUtc = DateTime.UtcNow, value = 250m, currency = "EGP",
            customer = new { externalId = "customer-2" },
            attribution = new { ctwaClid = "untrusted-click-id" },
            privacy = new { consentState = "Granted", legalBasis = "Consent" }
        });

        var result = await ingress.IngestAsync(setup.ProjectId, "orders-v2", timestamp,
            "v1=" + ConversionSecurity.Sign(source.SigningSecret, timestamp, body), body, default);
        var conversion = await setup.Db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync();

        Assert.Equal(result.ConversionId, conversion.Id);
        Assert.Equal("orders-v2:Order:order-42", conversion.CanonicalKey);
        Assert.Equal(AttributionState.Unattributed, conversion.AttributionState);
        Assert.Empty(await setup.Db.AdvertisingAttributionTouches.IgnoreQueryFilters().ToListAsync());
    }

    private static SetupState Setup()
    {
        var projectId = Guid.NewGuid(); var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant, new ServiceCollection().BuildServiceProvider());
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"conversion-api-{Guid.NewGuid():N}"));
        var vault = new AdvertisingSecretVault(DataProtectionProvider.Create(directory));
        return new(projectId, db, vault);
    }

    private sealed record SetupState(Guid ProjectId, AppDbContext Db, AdvertisingSecretVault Vault);
}
