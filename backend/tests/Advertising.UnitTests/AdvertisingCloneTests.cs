using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingCloneTests
{
    [Fact]
    public async Task Creative_clone_preserves_delivery_invariants_and_changes_only_selected_creative()
    {
        var projectId = Guid.NewGuid();
        var tenant = new TenantContext(); tenant.SetProjectId(projectId);
        await using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            tenant, new ServiceCollection().BuildServiceProvider());
        var audience = new AudienceStrategy
        {
            ProjectId = projectId, IncludedGeoJson = "[\"EG\"]", ExcludedGeoJson = "[]", MinimumAge = 21,
            RequiredLanguagesJson = "[\"ar\"]", CustomAudienceExclusionsJson = "[\"customers\"]",
            AudienceSuggestionsJson = "{\"interests\":[\"education\"]}"
        };
        var plan = new CampaignPlan
        {
            ProjectId = projectId, AudienceStrategyId = audience.Id, DestinationId = Guid.NewGuid(), ConnectionId = Guid.NewGuid(),
            EnvelopeId = Guid.NewGuid(), OfferId = Guid.NewGuid(), CapabilitySnapshotId = Guid.NewGuid(), Name = "Control",
            Objective = "OUTCOME_ENGAGEMENT", OptimizationGoal = "CONVERSATIONS", BidStrategy = "LOWEST_COST_WITHOUT_CAP",
            DailyBudget = 100m, PlacementMode = PlacementPolicy.DynamicEligibleMeta, State = "Ready"
        };
        var oldCreative = Guid.NewGuid();
        var replacement = Guid.NewGuid();
        db.AddRange(audience, plan, new CampaignPlanCreative
        {
            ProjectId = projectId, PlanId = plan.Id, CreativeId = oldCreative, Role = "Control",
            PlacementCompatibilityJson = "{\"dynamic\":true}"
        });
        await db.SaveChangesAsync();

        var clone = await new AdvertisingCloneService(db).CloneAsync(projectId, plan.Id,
            AdvertisingCloneVariable.Creative, replacement, null);
        var cloneAudience = await db.AdvertisingAudienceStrategies.SingleAsync(item => item.Id == clone.AudienceStrategyId);
        var cloneCreative = await db.AdvertisingCampaignPlanCreatives.SingleAsync(item => item.PlanId == clone.Id);

        Assert.Equal(plan.DestinationId, clone.DestinationId);
        Assert.Equal(plan.Objective, clone.Objective);
        Assert.Equal(plan.OptimizationGoal, clone.OptimizationGoal);
        Assert.Equal(plan.DailyBudget, clone.DailyBudget);
        Assert.Equal(plan.PlacementMode, clone.PlacementMode);
        Assert.Equal(audience.IncludedGeoJson, cloneAudience.IncludedGeoJson);
        Assert.Equal(audience.MinimumAge, cloneAudience.MinimumAge);
        Assert.Equal(replacement, cloneCreative.CreativeId);
        Assert.NotEqual(plan.PlanHash, clone.PlanHash);

        var retry = await new AdvertisingCloneService(db).CloneAsync(projectId, plan.Id,
            AdvertisingCloneVariable.Creative, replacement, null);
        Assert.Equal(clone.Id, retry.Id);
        Assert.Equal(1, await db.AdvertisingCampaignPlans.CountAsync(item => item.Id == clone.Id));
    }
}
