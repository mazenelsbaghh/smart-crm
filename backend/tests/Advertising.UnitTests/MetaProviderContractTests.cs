using Microsoft.Extensions.Options;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class MetaProviderContractTests
{
    [Fact]
    public async Task Typed_hierarchy_is_created_paused_and_validate_only_does_not_create_an_adset()
    {
        var provider = new FakeMetaAdsHandler();
        var client = Client(provider);
        var payload = Payload();

        var campaignId = await client.CreateCampaignPausedAsync("token", "act_mock_1", payload.Campaign, default);
        var validation = await client.ValidateAdSetAsync("token", "act_mock_1", campaignId, payload.AdSet, default);
        var adSetId = await client.CreateAdSetPausedAsync("token", "act_mock_1", campaignId, payload.AdSet, default);
        var creativeId = await client.CreateProviderCreativeAsync("token", "act_mock_1", payload.Creative, default);
        var adId = await client.CreateAdPausedAsync("token", "act_mock_1", adSetId, creativeId, "Ad", default);

        Assert.True(validation.Accepted);
        Assert.StartsWith("mock_campaign_", campaignId);
        Assert.StartsWith("mock_adset_", adSetId);
        Assert.StartsWith("mock_creative_", creativeId);
        Assert.StartsWith("mock_ad_", adId);
        var effective = await client.ReadObjectAsync("token", adSetId,
            "status,effective_status,optimization_goal,bid_strategy,destination_type,promoted_object,targeting", default);
        Assert.Equal("PAUSED", effective["status"]);
        Assert.Equal("WHATSAPP", effective["destination_type"]);
        Assert.Equal("CONVERSATIONS", effective["optimization_goal"]);
        Assert.Contains("advantage_audience", effective["targeting"]);
        Assert.DoesNotContain("publisher_platforms", effective["targeting"]);
    }

    [Fact]
    public async Task Provider_normalization_is_allowed_but_destination_drift_is_blocking()
    {
        var provider = new FakeMetaAdsHandler { Scenario = FakeMetaAdsHandler.FailureScenario.DriftAdSet };
        var client = Client(provider);
        var payload = Payload();
        var campaignId = await client.CreateCampaignPausedAsync("token", "act_mock_1", payload.Campaign, default);
        var adSetId = await client.CreateAdSetPausedAsync("token", "act_mock_1", campaignId, payload.AdSet, default);
        var effective = await client.ReadObjectAsync("token", adSetId,
            "status,optimization_goal,bid_strategy,destination_type,targeting", default);

        Assert.Contains(MetaProviderEquivalence.CompareWhatsAppAdSet(payload.AdSet, effective),
            finding => finding.Field == "destination_type" && finding.Severity == Modules.Advertising.Domain.InvariantSeverity.Blocking);
    }

    private static MetaAdsClient Client(HttpMessageHandler handler) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://graph.facebook.com/v26.0/") },
        Options.Create(new AdvertisingOptions { Enabled = true, Meta = new MetaOptions { UseMock = true } }));

    internal static MetaPlanPayload Payload() => new(
        new("Plan", "OUTCOME_ENGAGEMENT", "AUCTION", "PAUSED", "[]", "LOWEST_COST_WITHOUT_CAP", 100m),
        new("Audience", "CONVERSATIONS", "IMPRESSIONS", "LOWEST_COST_WITHOUT_CAP", "WHATSAPP",
            "{\"page_id\":\"page_mock_1\",\"whatsapp_phone_number\":\"phone_mock_1\"}",
            "{\"geo_locations\":{\"countries\":[\"EG\"]},\"age_min\":21,\"targeting_automation\":{\"advantage_audience\":1}}",
            "PAUSED", DateTime.UtcNow.AddMinutes(5), null),
        new("Creative", "page_mock_1", "phone_mock_1", "WHATSAPP_MESSAGE", "WHATSAPP",
            "ExistingPagePost", "page_mock_1_102", "Message", "Headline", null));
}
