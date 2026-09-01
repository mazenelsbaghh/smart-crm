using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class DecisionPipelineTests
{
    [Fact]
    public async Task Unsupported_financial_action_is_rejected_before_ai_work_is_created()
    {
        await using var context = Context();
        var result = await new AdvertisingDecisionAi(context)
            .ReviewActionAsync(Guid.NewGuid(), "DeleteCampaign", "{}", CancellationToken.None);

        Assert.Equal(DecisionVerdict.Reject, result.AuditorVerdict);
        Assert.Empty(context.AdvertisingAiWorkItems);
    }

    [Fact]
    public async Task Pending_independent_review_waits_instead_of_spending()
    {
        await using var context = Context();
        var result = await new AdvertisingDecisionAi(context)
            .ReviewActionAsync(Guid.NewGuid(), "IncreaseBudget", "{\"roas\":2.4}", CancellationToken.None);

        Assert.Equal(DecisionVerdict.Wait, result.StrategistVerdict);
        Assert.Equal(DecisionVerdict.Wait, result.AuditorVerdict);
        Assert.Single(await context.AdvertisingAiWorkItems.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public void Closed_catalog_contains_every_declared_autonomous_action_and_rejects_hallucinated_actions()
    {
        Assert.All(Enum.GetNames<AutonomousActionType>(), action => Assert.True(AdvertisingDecisionPolicy.IsSupported(action)));
        Assert.False(AdvertisingDecisionPolicy.IsSupported("DeleteEverythingAndDoubleSpend"));
    }

    [Fact]
    public void Sparse_evidence_returns_exact_wait_reasons_before_any_ai_review()
    {
        var result = new AdvertisingEvidenceService().Evaluate([], [], 50m, DateTime.UtcNow,
            attributionDelayHours: 24, trackingHealthy: false);

        Assert.Equal(EvidenceVerdict.Wait, result.Verdict);
        Assert.Equal(new[] { "ADS_WAIT_INSUFFICIENT_SNAPSHOTS", "ADS_WAIT_INSUFFICIENT_VOLUME", "ADS_WAIT_TRACKING_UNSAFE" },
            result.WaitReasons);
    }

    [Fact]
    public async Task Manual_unowned_ad_cannot_enter_the_activation_review_pipeline()
    {
        await using var context = Context();
        var projectId = Guid.NewGuid();
        context.AdvertisingManagedOwnership.Add(new ManagedOwnershipRecord
        {
            ProjectId = projectId,
            OwnershipKind = ManagedOwnershipKind.ManualUnowned,
            ProviderCampaignExternalId = "campaign-manual"
        });
        await context.SaveChangesAsync();
        var ownershipId = await context.AdvertisingManagedOwnership.IgnoreQueryFilters().Select(item => item.Id).SingleAsync();
        context.ManagedAdvertisements.Add(new ManagedAdvertisement
        {
            ProjectId = projectId,
            OwnershipRecordId = ownershipId,
            AdExternalId = "ad-manual",
            ConfiguredStatus = ManagedDeliveryState.Paused,
            DestinationType = "WHATSAPP"
        });
        await context.SaveChangesAsync();
        var service = new AdvertisingDecisionService(context, new AdvertisingDecisionAi(context),
            new AdvertisingSafetyEngine(context), new AdvertisingOwnershipPolicy(context));

        var commands = await service.ProposeCanaryActivationAsync(projectId, CancellationToken.None);

        Assert.Empty(commands);
        Assert.Empty(await context.AdvertisingDecisions.IgnoreQueryFilters().ToListAsync());
        Assert.Empty(await context.AdvertisingAiWorkItems.IgnoreQueryFilters().ToListAsync());
    }

    private static AppDbContext Context() => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
        new TenantContext(),
        new ServiceCollection().BuildServiceProvider());
}
