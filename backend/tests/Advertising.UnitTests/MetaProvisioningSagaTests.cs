using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaProvisioningSagaTests
{
    [Fact]
    public async Task Complete_readback_marks_delivery_verified_paused_and_is_idempotent()
    {
        var setup = await SetupAsync(FakeMetaAdsHandler.FailureScenario.None);
        var actor = Guid.NewGuid();

        var first = await setup.Service.ProvisionPausedAsync(
            setup.ProjectId, setup.PlanId, setup.CreativeId, null, actor, "complete-once", default);
        var second = await setup.Service.ProvisionPausedAsync(
            setup.ProjectId, setup.PlanId, setup.CreativeId, null, actor, "complete-once", default);

        Assert.Equal(nameof(ProviderReconciliationState.VerifiedPaused), first.State);
        Assert.Equal(first.CampaignId, second.CampaignId);
        Assert.Single(await setup.Db.AdvertisingManagedCampaigns.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await setup.Db.AdvertisingManagedAdSets.IgnoreQueryFilters().ToListAsync());
        Assert.Single(await setup.Db.ManagedAdvertisements.IgnoreQueryFilters().ToListAsync());
        var advertisement = await setup.Db.ManagedAdvertisements.IgnoreQueryFilters().SingleAsync();
        Assert.Equal("Meta", advertisement.PublisherPlatform);
        Assert.Equal("[]", advertisement.PositionsJson);
        Assert.Equal(4, await setup.Db.AdvertisingProviderObjectSnapshots.IgnoreQueryFilters().CountAsync());
        Assert.All(await setup.Db.AdvertisingManagedProviderCreatives.IgnoreQueryFilters().ToListAsync(),
            creative => Assert.Equal(ProviderCreativeVerificationState.Verified, creative.VerificationState));
    }

    [Fact]
    public async Task Deterministic_provider_rejection_is_failed_not_unknown()
    {
        var setup = await SetupAsync(FakeMetaAdsHandler.FailureScenario.RejectAdSet);

        var error = await Assert.ThrowsAsync<AdvertisingException>(() => setup.Service.ProvisionPausedAsync(
            setup.ProjectId, setup.PlanId, setup.CreativeId, null, Guid.NewGuid(), "reject-once", default));

        Assert.Equal("ADS_PROVIDER_REJECTED", error.Code);
        Assert.DoesNotContain(await setup.Db.AdvertisingProviderOperations.IgnoreQueryFilters().ToListAsync(),
            operation => operation.State == ProviderOperationState.Unknown);
    }

    [Fact]
    public async Task Ambiguous_timeout_is_recorded_as_unknown_and_never_blindly_retried()
    {
        var setup = await SetupAsync(FakeMetaAdsHandler.FailureScenario.TimeoutAfterCampaign);

        var error = await Assert.ThrowsAsync<AdvertisingException>(() => setup.Service.ProvisionPausedAsync(
            setup.ProjectId, setup.PlanId, setup.CreativeId, null, Guid.NewGuid(), "provision-once", default));

        Assert.Equal("ADS_PROVIDER_RESULT_UNKNOWN", error.Code);
        var operations = await setup.Db.AdvertisingProviderOperations.IgnoreQueryFilters().OrderBy(x => x.CreatedAt).ToListAsync();
        Assert.Single(operations, x => x.OperationType == "CreateCampaign" && x.State == ProviderOperationState.Succeeded);
        Assert.Single(operations, x => x.OperationType == "CreateAdSet" && x.State == ProviderOperationState.Unknown);
        Assert.Equal("ReconciliationRequired", (await setup.Db.AdvertisingCampaignPlans.IgnoreQueryFilters().SingleAsync()).State);
    }

    [Fact]
    public async Task Critical_readback_drift_keeps_every_delivery_object_unverified_and_paused()
    {
        var setup = await SetupAsync(FakeMetaAdsHandler.FailureScenario.DriftAdSet);

        var result = await setup.Service.ProvisionPausedAsync(
            setup.ProjectId, setup.PlanId, setup.CreativeId, null, Guid.NewGuid(), "drift-once", default);

        Assert.Equal(nameof(ProviderReconciliationState.Drifted), result.State);
        Assert.All(await setup.Db.ManagedAdvertisements.IgnoreQueryFilters().ToListAsync(), ad =>
        {
            Assert.Equal(ManagedDeliveryState.Paused, ad.ConfiguredStatus);
            Assert.Equal(ProviderReconciliationState.Drifted, ad.ReconciliationState);
        });
        Assert.Contains(await setup.Db.AdvertisingProviderValidationFindings.IgnoreQueryFilters().ToListAsync(),
            finding => finding.Code == "ADS_PROVIDER_FIELD_DRIFT" && finding.Field == "destination_type");
    }

    private static async Task<Setup> SetupAsync(FakeMetaAdsHandler.FailureScenario scenario)
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant, new ServiceCollection().BuildServiceProvider());
        var directory = new DirectoryInfo(Path.Combine(Path.GetTempPath(), $"ads-provisioning-{Guid.NewGuid():N}"));
        var vault = new AdvertisingSecretVault(DataProtectionProvider.Create(directory));
        var provider = new FakeMetaAdsHandler { Scenario = scenario };
        var client = new MetaAdsClient(new HttpClient(provider) { BaseAddress = new Uri("https://graph.facebook.com/v26.0/") },
            Options.Create(new AdvertisingOptions { Enabled = true, Meta = new MetaOptions { UseMock = true } }));

        var connection = new AdvertisingConnection
        {
            ProjectId = projectId, AdAccountExternalId = "act_mock_1", PageExternalId = "page_mock_1",
            ProtectedAccessToken = vault.Protect("token"), State = AdvertisingConnectionState.Ready
        };
        var destination = new AuthorizedWhatsAppDestination
        {
            ProjectId = projectId, ConnectionId = connection.Id, WabaExternalId = "waba_mock_1",
            PhoneNumberExternalId = "phone_mock_1", PageExternalId = "page_mock_1", DatasetExternalId = "dataset_mock_1",
            State = AuthorizedDestinationState.Eligible
        };
        var capability = new AdvertisingCapabilitySnapshot
        {
            ProjectId = projectId, ConnectionId = connection.Id, DestinationId = destination.Id,
            State = AdvertisingCapabilityState.Healthy, CheckedAtUtc = DateTime.UtcNow, ExpiresAtUtc = DateTime.UtcNow.AddHours(6),
            OptimizationGoalsJson = "[\"CONVERSATIONS\"]",
            PlacementEligibilityJson = "{\"whatsappDestinationEligible\":true,\"automatic\":true}"
        };
        var audience = new AudienceStrategy
        {
            ProjectId = projectId, IncludedGeoJson = "[\"EG\"]", ExcludedGeoJson = "[]", MinimumAge = 21,
            RequiredLanguagesJson = "[\"ar\"]", CustomAudienceExclusionsJson = "[]"
        };
        var plan = new CampaignPlan
        {
            ProjectId = projectId, ConnectionId = connection.Id, DestinationId = destination.Id,
            CapabilitySnapshotId = capability.Id, AudienceStrategyId = audience.Id, Name = "WhatsApp Plan",
            Objective = "OUTCOME_ENGAGEMENT", OptimizationGoal = "CONVERSATIONS", DailyBudget = 100m,
            StartsAtUtc = DateTime.UtcNow.AddMinutes(5), State = "Ready"
        };
        var creative = new AdvertisingCreative
        {
            ProjectId = projectId, SourceType = CreativeSourceType.ExistingPagePost,
            SourceExternalId = "page_mock_1_102", MediaType = CreativeMediaType.Video,
            EligibilityState = CreativeEligibility.Eligible
        };
        db.AddRange(connection, destination, capability, audience, plan, creative);
        await db.SaveChangesAsync();
        var reconciliation = new MetaProviderReconciliationService(db, client, vault);
        var service = new CampaignProvisioningService(db, client, vault, reconciliation, new AdvertisingAuditService(db));
        return new(projectId, plan.Id, creative.Id, db, service);
    }

    private sealed record Setup(Guid ProjectId, Guid PlanId, Guid CreativeId, AppDbContext Db, CampaignProvisioningService Service);
}
