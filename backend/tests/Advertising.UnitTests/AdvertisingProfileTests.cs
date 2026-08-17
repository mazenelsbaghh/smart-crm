using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingProfileTests
{
    [Fact]
    public void Sourced_course_offer_extracts_real_price_destination_and_funnel()
    {
        var documentId = Guid.NewGuid();
        var result = AdvertisingProfileExtractor.Extract(documentId, 4, "كورس عملي، السعر 1500 جنيه، التسجيل https://example.com/course");
        Assert.True(result.Eligible); Assert.Equal("Course", result.OfferType); Assert.Equal(1500m, result.Price); Assert.Equal("EGP", result.Currency);
        Assert.Contains($"knowledge:{documentId:N}:v4", result.SourceCitations);
        Assert.Contains("AttendanceConfirmed", AdvertisingProfileExtractor.Funnel(result.OfferType));
    }

    [Theory]
    [InlineData("منتج بسعر 200 جنيه بدون رابط", "MissingDestination")]
    [InlineData("منتج 200 جنيه و 300 جنيه https://example.com/p", "ContradictoryPriceFacts")]
    public void Missing_or_contradictory_commercial_facts_block_launch(string content, string reason)
    {
        var result = AdvertisingProfileExtractor.Extract(Guid.NewGuid(), 1, content);
        Assert.False(result.Eligible); Assert.Equal(reason, result.BlockReason);
    }
}
