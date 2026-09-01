using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingEvidenceTests
{
    [Fact]
    public void Paid_purchase_truth_outranks_leads_and_provider_conversations()
    {
        var now = DateTime.UtcNow;
        var rows = Rows(now.AddDays(-4), now.AddDays(-2), 100m, 5000, 100);
        var outcomes = new[]
        {
            new CanonicalConversion { EventType = "QualifiedLead", State = ConversionState.Verified },
            new CanonicalConversion { EventType = "Purchase", CurrentValue = 250m, State = ConversionState.Verified }
        };

        var result = new AdvertisingEvidenceService().Evaluate(rows, outcomes, 50m, now, 24, true);

        Assert.Equal("PaidPurchase", result.OutcomeLevel);
        Assert.Equal(1, result.Conversions);
        Assert.Equal(250m, result.Revenue);
    }

    [Fact]
    public void Sparse_learning_or_unsafe_tracking_returns_declared_wait_reasons_not_a_loser()
    {
        var now = DateTime.UtcNow;
        var rows = Rows(now.AddHours(-4), now.AddHours(-1), 200m, 200, 1);
        rows[0].LearningStatus = "LEARNING";

        var result = new AdvertisingEvidenceService().Evaluate(rows, [], 50m, now, 24, false);

        Assert.Equal(EvidenceVerdict.Wait, result.Verdict);
        Assert.Contains("ADS_WAIT_ATTRIBUTION_DELAY", result.WaitReasons!);
        Assert.Contains("ADS_WAIT_LEARNING", result.WaitReasons!);
        Assert.Contains("ADS_WAIT_TRACKING_UNSAFE", result.WaitReasons!);
    }

    [Fact]
    public void Evidence_package_pins_window_truth_tracking_policy_and_hash()
    {
        var now = DateTime.UtcNow;
        var projectId = Guid.NewGuid();
        var rows = Rows(now.AddDays(-4), now.AddDays(-2), 100m, 5000, 100);
        rows.ForEach(item => { item.ProjectId = projectId; item.FetchedAtUtc = now.AddDays(-1); item.IsCurrent = true; item.Revision = 2; });
        var outcomes = new[] { new CanonicalConversion { ProjectId = projectId, EventType = "Purchase",
            OccurredAtUtc = now.AddDays(-2), CurrentValue = 250m, State = ConversionState.Verified,
            TruthState = "Verified", AttributionState = AttributionState.Attributed } };
        var tracking = new TrackingHealthSnapshot { ProjectId = projectId, TrackingHealthPolicyVersion = 4,
            State = TrackingHealthState.Healthy, EvaluatedAtUtc = now.AddMinutes(-5) };

        var package = new AdvertisingEvidenceService().BuildPackage(projectId, now.AddDays(-7), now,
            rows, outcomes, 50m, tracking, now);

        Assert.Equal("CanonicalConversionLedger", package.TruthSource);
        Assert.Equal(4, package.TrackingPolicyVersion);
        Assert.Equal(TrackingHealthState.Healthy, package.TrackingState);
        Assert.Equal("PaidPurchase", package.Evaluation.OutcomeLevel);
        Assert.NotEmpty(package.EvidenceHash);
        Assert.Contains("TrackingHealthPolicyVersion", package.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public void Fatigue_requires_all_sufficient_declining_signals()
    {
        var fatigue = CreativeFatigueService.Evaluate(
            new(5000, 2m, 2m, .08m, 20m), new(5000, 4m, 1.2m, .05m, 28m));
        var early = CreativeFatigueService.Evaluate(
            new(500, 2m, 2m, .08m, 20m), new(500, 4m, 1.2m, .05m, 28m));

        Assert.Equal("FATIGUED", fatigue.State);
        Assert.Equal("WAIT", early.State);
    }

    [Fact]
    public void Cost_cap_requires_credible_history_and_scaling_respects_cooldown_and_maximum_step()
    {
        var now = DateTime.UtcNow;
        var proven = new PortfolioCandidate(Guid.NewGuid(), BudgetPurpose.Winner, 1m, 100m, 50, true, now.AddDays(-2));
        var unproven = new PortfolioCandidate(Guid.NewGuid(), BudgetPurpose.CreativeTest, .5m, 50m, 2, false, null);
        var allocations = PortfolioAllocationService.Allocate(150m, 30m, [proven, unproven], now);
        var envelope = new AutonomyEnvelope { MaximumIncreasePercent = 20m, CooldownHours = 24 };

        Assert.Equal("COST_CAP_ELIGIBLE", allocations.Single(item => item.TargetId == proven.TargetId).BidStrategy);
        Assert.Equal("LOWEST_COST_WITHOUT_CAP", allocations.Single(item => item.TargetId == unproven.TargetId).BidStrategy);
        Assert.Equal(120m, PortfolioAllocationService.GradualScale(proven, 200m, envelope, now));
        Assert.Equal(100m, PortfolioAllocationService.GradualScale(proven with { LastChangedAtUtc = now.AddHours(-2) }, 200m, envelope, now));
    }

    private static List<InsightsSnapshot> Rows(DateTime start, DateTime end, decimal spend, long impressions, long clicks) =>
    [
        new() { IntervalStartUtc = start, IntervalEndUtc = start.AddHours(12), Spend = spend / 2, Impressions = impressions / 2, Clicks = clicks / 2, ProviderActionsJson = "{}" },
        new() { IntervalStartUtc = start.AddHours(12), IntervalEndUtc = end, Spend = spend / 2, Impressions = impressions / 2, Clicks = clicks / 2, ProviderActionsJson = "{}" }
    ];
}
