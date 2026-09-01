using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingStrategyTests
{
    [Theory]
    [InlineData("Course", "EnrollmentPaid", "MESSAGING_PURCHASE_CONVERSION")]
    [InlineData("Service", "QualifiedLead", "QUALITY_LEAD")]
    [InlineData("Product", "Conversation", "CONVERSATIONS")]
    public void Funnel_is_whatsapp_centered_and_has_a_safe_optimization_fallback(string type, string outcome, string expected)
    {
        var funnel = AdvertisingFunnelService.Infer(type, outcome);
        Assert.Equal(expected, funnel.PrimaryOptimization);
        Assert.Contains("CONVERSATIONS", funnel.FallbackOptimizations.Append(funnel.PrimaryOptimization));
    }

    [Fact]
    public void Ranking_excludes_unauthorized_or_capacity_exhausted_offer_destinations()
    {
        var allowedOffer = Guid.NewGuid();
        var ranked = AdvertisingStrategyService.Rank([
            new(allowedOffer, Guid.NewGuid(), 0.95m, 500m, 10, true, true),
            new(Guid.NewGuid(), Guid.NewGuid(), 1m, 1000m, 10, true, false),
            new(Guid.NewGuid(), Guid.NewGuid(), 1m, 1000m, 0, true, true)
        ]);
        Assert.Equal(allowedOffer, Assert.Single(ranked).OfferId);
    }
}
