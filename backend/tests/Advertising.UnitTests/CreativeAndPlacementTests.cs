using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class CreativeAndPlacementTests
{
    [Fact]
    public void Recent_video_ranks_above_old_image_and_only_uses_facebook_video_placements()
    {
        var now = new DateTime(2026, 8, 17, 12, 0, 0, DateTimeKind.Utc);
        var video = CreativeRankingService.Rank(CreativeMediaType.Video, now.AddDays(-1), now);
        var image = CreativeRankingService.Rank(CreativeMediaType.Image, now.AddDays(-40), now);
        Assert.True(video.Score > image.Score); Assert.Equal(new[] { "feed", "facebook_reels" }, video.Placements);
        Assert.True(FacebookPlacementPolicy.IsAllowed("facebook", video.Placements));
    }

    [Fact]
    public void Image_variants_target_feed_and_story()
    {
        var result = CreativeRankingService.Rank(CreativeMediaType.Image, DateTime.UtcNow, DateTime.UtcNow);
        Assert.Equal(new[] { "feed", "story" }, result.Placements);
        Assert.DoesNotContain(result.Placements, x => x.Contains("instagram", StringComparison.OrdinalIgnoreCase));
    }
}
