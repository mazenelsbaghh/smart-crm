using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public sealed record PortfolioCandidate(Guid TargetId, BudgetPurpose Purpose, decimal EvidenceScore, decimal CurrentBudget,
    int HistoricalOutcomes, bool StableCost, DateTime? LastChangedAtUtc);
public sealed record PortfolioAllocation(Guid TargetId, BudgetPurpose Purpose, decimal Amount, string BidStrategy);

public static class PortfolioAllocationService
{
    public static IReadOnlyList<PortfolioAllocation> Allocate(decimal usableCap, decimal targetCpa,
        IReadOnlyList<PortfolioCandidate> candidates, DateTime nowUtc)
    {
        if (usableCap <= 0 || candidates.Count == 0) return [];
        var ordered = candidates.OrderByDescending(candidate => candidate.EvidenceScore).ThenBy(candidate => candidate.TargetId).ToArray();
        var maxTargets = Math.Max(1, Math.Min(ordered.Length, (int)Math.Floor(usableCap / Math.Max(targetCpa, 1m))));
        var selected = ordered.Take(maxTargets).ToArray();
        var weights = selected.Select(candidate => candidate.Purpose switch
        {
            BudgetPurpose.Winner => 4m,
            BudgetPurpose.CreativeTest or BudgetPurpose.AudienceTest => 1m,
            BudgetPurpose.Retargeting => .5m,
            _ => 1m
        }).ToArray();
        var totalWeight = weights.Sum();
        return selected.Select((candidate, index) => new PortfolioAllocation(candidate.TargetId, candidate.Purpose,
            decimal.Round(usableCap * weights[index] / totalWeight, 2, MidpointRounding.ToZero),
            candidate.HistoricalOutcomes >= 50 && candidate.StableCost ? "COST_CAP_ELIGIBLE" : "LOWEST_COST_WITHOUT_CAP")).ToArray();
    }

    public static decimal GradualScale(PortfolioCandidate candidate, decimal desiredBudget, AutonomyEnvelope envelope, DateTime nowUtc)
    {
        if (candidate.LastChangedAtUtc is { } changed && nowUtc - changed < TimeSpan.FromHours(envelope.CooldownHours))
            return candidate.CurrentBudget;
        var maximum = candidate.CurrentBudget * (1m + envelope.MaximumIncreasePercent / 100m);
        return Math.Min(desiredBudget, decimal.Round(maximum, 2, MidpointRounding.ToZero));
    }
}
