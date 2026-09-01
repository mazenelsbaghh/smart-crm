using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaAdvantagePlusTests
{
    [Fact]
    public void Dynamic_targeting_has_hard_business_controls_without_stale_manual_placements()
    {
        var payload = Payload();
        Assert.Contains("geo_locations", payload.AdSet.TargetingJson);
        Assert.Contains("age_min", payload.AdSet.TargetingJson);
        Assert.Contains("advantage_audience", payload.AdSet.TargetingJson);
        Assert.DoesNotContain("publisher_platforms", payload.AdSet.TargetingJson);
        Assert.DoesNotContain("facebook_positions", payload.AdSet.TargetingJson);
    }

    [Fact]
    public void Equivalence_accepts_provider_defaults_but_rejects_critical_field_drift()
    {
        var planned = Payload().AdSet;
        var effective = new Dictionary<string, string>
        {
            ["destination_type"] = "WHATSAPP", ["optimization_goal"] = "CONVERSATIONS",
            ["bid_strategy"] = "LOWEST_COST_WITHOUT_CAP", ["status"] = "PAUSED", ["targeting"] = "{\"targeting_automation\":{}}",
            ["promoted_object"] = "{\"page_id\":\"page\",\"whatsapp_phone_number\":\"phone\"}",
            ["provider_default_inventory"] = "resolved"
        };
        Assert.Empty(MetaProviderEquivalence.CompareWhatsAppAdSet(planned, effective));
        effective["destination_type"] = "WEBSITE";
        Assert.NotEmpty(MetaProviderEquivalence.CompareWhatsAppAdSet(planned, effective));
    }

    [Fact]
    public void Production_2026_08_19_campaign_budget_bid_strategy_is_not_required_on_adset_readback()
    {
        var planned = Payload().AdSet;
        var effective = new Dictionary<string, string>
        {
            ["destination_type"] = "WHATSAPP", ["optimization_goal"] = "CONVERSATIONS",
            ["status"] = "PAUSED", ["targeting"] = "{\"targeting_automation\":{}}",
            ["promoted_object"] = "{\"page_id\":\"page\",\"whatsapp_phone_number\":\"phone\"}",
        };

        Assert.Empty(MetaProviderEquivalence.CompareWhatsAppAdSet(planned, effective));
    }

    private static MetaPlanPayload Payload()
    {
        var destinationId = Guid.NewGuid();
        return MetaCampaignPlanMapper.Map(
            new CampaignPlan { DestinationId = destinationId, Name = "Plan", Objective = "OUTCOME_ENGAGEMENT", OptimizationGoal = "CONVERSATIONS", DailyBudget = 100, StartsAtUtc = DateTime.UtcNow, PlacementMode = PlacementPolicy.DynamicEligibleMeta },
            new AudienceStrategy { IncludedGeoJson = "[\"EG\"]", ExcludedGeoJson = "[]", MinimumAge = 21, RequiredLanguagesJson = "[\"ar\"]", CustomAudienceExclusionsJson = "[]" },
            new AuthorizedWhatsAppDestination { Id = destinationId, State = AuthorizedDestinationState.Eligible, PageExternalId = "page", PhoneNumberExternalId = "phone" },
            new AdvertisingCreative { EligibilityState = CreativeEligibility.Eligible, SourceExternalId = "post" });
    }
}
