using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class CreativeAndPlacementTests
{
    [Fact]
    public void Eligible_video_is_ranked_from_evidence_and_supports_dynamic_vertical_inventory()
    {
        var rank = CreativeRankingService.Rank(new CreativeRankingEvidence(
            CreativeMediaType.Video, DateTime.UtcNow.AddDays(-2), true, "PageOwned", "Allowed", 14m, 18m, true), DateTime.UtcNow);

        Assert.True(rank.Score >= 80m);
        Assert.Contains("facebook_reels", rank.Placements);
        Assert.Contains("paid=18", rank.Explanation);
    }

    [Theory]
    [InlineData("Unknown", "Allowed", true)]
    [InlineData("PageOwned", "Blocked", true)]
    [InlineData("PageOwned", "Allowed", false)]
    public void Missing_rights_policy_or_supported_source_is_blocked_without_fabrication(
        string rights, string policy, bool supported)
    {
        var rank = CreativeRankingService.Rank(new CreativeRankingEvidence(
            CreativeMediaType.Image, DateTime.UtcNow, true, rights, policy, 20m, 20m, supported), DateTime.UtcNow);

        Assert.Equal(0m, rank.Score);
        Assert.Empty(rank.Placements);
        Assert.StartsWith("Blocked", rank.Explanation);
    }
}
