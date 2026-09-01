using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingExperimentTests
{
    [Fact]
    public void Evaluation_waits_for_time_volume_coverage_and_attribution_settlement()
    {
        var now = DateTime.UtcNow;
        var experiment = Experiment(now.AddHours(-12));
        var arms = new[]
        {
            new ExperimentArmEvidence(Guid.NewGuid(), 60m, 2, 30m, 100m, .95m, false),
            new ExperimentArmEvidence(Guid.NewGuid(), 60m, 2, 40m, 80m, .95m, false)
        };

        var decision = AdvertisingExperimentService.Evaluate(experiment, arms, now);

        Assert.Equal("WAIT", decision.Verdict);
        Assert.Contains("ADS_WAIT_MINIMUM_TIME", decision.Reasons);
        Assert.Contains("ADS_WAIT_ATTRIBUTION_DELAY", decision.Reasons);
    }

    [Fact]
    public void Mature_experiment_selects_only_a_materially_better_arm()
    {
        var now = DateTime.UtcNow;
        var first = Guid.NewGuid();
        var decision = AdvertisingExperimentService.Evaluate(Experiment(now.AddDays(-10)),
        [
            new(first, 100m, 10, 10m, 500m, .98m, false),
            new(Guid.NewGuid(), 100m, 8, 14m, 400m, .98m, false)
        ], now);

        Assert.Equal("WINNER", decision.Verdict);
        Assert.Equal(first, decision.WinnerArmId);
    }

    private static AdvertisingExperiment Experiment(DateTime started) => new()
    {
        StartedAtUtc = started, MinimumElapsedHours = 48, MinimumSpend = 100m,
        MinimumAttributedOutcomes = 4, MinimumAttributionCoverage = .9m,
        AttributionWindowDays = 7, CorrectionLagHours = 24
    };
}
