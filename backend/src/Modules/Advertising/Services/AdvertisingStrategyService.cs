using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public sealed record OfferStrategyCandidate(Guid OfferId, Guid DestinationId, decimal EvidenceConfidence, decimal? ContributionMargin,
    int? AvailableCapacity, bool Eligible, bool Authorized);
public sealed record RankedOfferStrategy(Guid OfferId, Guid DestinationId, decimal Score, IReadOnlyList<string> Reasons);

public static class AdvertisingStrategyService
{
    public static IReadOnlyList<RankedOfferStrategy> Rank(IEnumerable<OfferStrategyCandidate> candidates) => candidates
        .Where(candidate => candidate.Eligible && candidate.Authorized && candidate.AvailableCapacity != 0)
        .Select(candidate => new RankedOfferStrategy(candidate.OfferId, candidate.DestinationId,
            decimal.Round(candidate.EvidenceConfidence * 60m + Math.Min(30m, Math.Max(0m, candidate.ContributionMargin ?? 0m) / 10m) +
                          (candidate.AvailableCapacity is null ? 5m : Math.Min(10m, candidate.AvailableCapacity.Value)), 2),
            ["AUTHORIZED_OFFER_DESTINATION", "EVIDENCE_BOUND", candidate.AvailableCapacity is null ? "CAPACITY_UNKNOWN" : "CAPACITY_AVAILABLE"]))
        .OrderByDescending(candidate => candidate.Score).ThenBy(candidate => candidate.OfferId).ToArray();
}
