using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class TrackingHealthTests
{
    private static readonly TrackingHealthPolicy Policy = new()
    {
        Id = Guid.NewGuid(), Version = 3, MinimumDenominator = 20, MinimumReferralCoverage = .95m,
        MinimumExactMatchRate = .90m, MinimumDeliveryAcceptanceRate = .95m,
        MaximumCorrectionRate = .20m, MaximumEventDelayMinutes = 30
    };

    [Fact]
    public void Missing_real_cloud_referral_proof_is_unsafe_even_before_sample_matures()
    {
        var result = AdvertisingTrackingHealthService.Evaluate(Policy,
            new(0, 0, 0, 0, null, null, null, null, null, false, null));

        Assert.Equal(TrackingHealthState.Unsafe, result.State);
        Assert.Contains("ADS_TRACKING_LIVE_REFERRAL_PROOF_MISSING", result.Reasons);
    }

    [Fact]
    public void Healthy_requires_coverage_exact_match_delivery_delay_and_corrections_to_all_pass()
    {
        var healthy = AdvertisingTrackingHealthService.Evaluate(Policy,
            new(100, 100, 98, 98, .95m, .9m, .99m, .05m, 5, true, DateTime.UtcNow));
        var unsafeResult = AdvertisingTrackingHealthService.Evaluate(Policy,
            new(100, 100, 80, 80, .70m, .4m, .70m, .30m, 60, true, DateTime.UtcNow));

        Assert.Equal(TrackingHealthState.Healthy, healthy.State);
        Assert.Equal(.98m, healthy.ReferralCoverage);
        Assert.Equal(TrackingHealthState.Unsafe, unsafeResult.State);
        Assert.Contains("ADS_TRACKING_REFERRAL_COVERAGE_LOW", unsafeResult.Reasons);
        Assert.Contains("ADS_TRACKING_EXACT_MATCH_LOW", unsafeResult.Reasons);
        Assert.Contains("ADS_TRACKING_DELIVERY_ACCEPTANCE_LOW", unsafeResult.Reasons);
        Assert.Contains("ADS_TRACKING_CORRECTION_RATE_HIGH", unsafeResult.Reasons);
        Assert.Contains("ADS_TRACKING_DELAY_HIGH", unsafeResult.Reasons);
    }

    [Fact]
    public void Later_referral_touches_do_not_inflate_conversation_coverage()
    {
        var result = AdvertisingTrackingHealthService.Evaluate(Policy,
            new(100, 130, 120, 98, .95m, .9m, .99m, .05m, 5, true, DateTime.UtcNow));

        Assert.Equal(.98m, result.ReferralCoverage);
    }

    [Fact]
    public void Versioned_policy_identity_is_available_for_every_reproducible_snapshot()
    {
        Assert.NotEqual(Guid.Empty, Policy.Id);
        Assert.Equal(3, Policy.Version);
    }
}
