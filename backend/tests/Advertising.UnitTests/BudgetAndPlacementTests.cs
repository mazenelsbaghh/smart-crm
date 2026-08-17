using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class BudgetAndPlacementTests
{
    [Fact]
    public void Allocation_never_exceeds_usable_cap()
    {
        var result = new BudgetAllocator().Allocate(1000m, 15m, creativeTests: 3, hasRetargeting: true);

        Assert.Equal(150m, result.Reserve);
        Assert.Equal(850m, result.Usable);
        Assert.True(result.Slices.Sum(x => x.Amount) <= result.Usable);
        Assert.Equal(result.Usable, result.Slices.Sum(x => x.Amount));
    }

    [Theory]
    [InlineData("instagram", "feed")]
    [InlineData("facebook", "instagram_story")]
    [InlineData("facebook", "video_feeds")]
    public void Non_facebook_or_unsupported_positions_are_rejected(string publisher, string position)
    {
        Assert.False(FacebookPlacementPolicy.IsAllowed(publisher, [position]));
    }

    [Theory]
    [InlineData("feed")]
    [InlineData("story")]
    [InlineData("facebook_reels")]
    public void Supported_facebook_positions_are_allowed(string position)
    {
        Assert.True(FacebookPlacementPolicy.IsAllowed("facebook", [position]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Invalid_cap_is_rejected(decimal cap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BudgetAllocator().Allocate(cap, 15m, 1, false));
    }
}
