using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public sealed record CreativeRank(decimal Score, IReadOnlyCollection<string> Placements, string Explanation);
public sealed record CreativeRankingEvidence(CreativeMediaType MediaType, DateTime? CreatedAtUtc, bool OfferRelevant,
    string RightsState, string PolicyState, decimal OrganicScore, decimal PriorPaidScore, bool FormatSupported);

public static class CreativeRankingService
{
    public static CreativeRank Rank(CreativeMediaType mediaType, DateTime? createdAtUtc, DateTime nowUtc)
        => Rank(new(mediaType, createdAtUtc, true, "PageOwned", "Allowed", 0m, 0m, true), nowUtc);

    public static CreativeRank Rank(CreativeRankingEvidence evidence, DateTime nowUtc)
    {
        if (!evidence.FormatSupported || evidence.RightsState is not ("PageOwned" or "ProjectOwned") || evidence.PolicyState == "Blocked")
            return new(0m, [], "Blocked: source rights, policy, or format is not eligible.");
        var ageDays = evidence.CreatedAtUtc is null ? 30 : Math.Max(0, (nowUtc - evidence.CreatedAtUtc.Value).TotalDays);
        var freshness = Math.Max(0m, 30m - (decimal)Math.Min(30, ageDays));
        var relevance = evidence.OfferRelevant ? 30m : 0m;
        var organic = Math.Clamp(evidence.OrganicScore, 0m, 20m);
        var paid = Math.Clamp(evidence.PriorPaidScore, 0m, 20m);
        var score = freshness + relevance + organic + paid;
        var placements = evidence.MediaType == CreativeMediaType.Video ? new[] { "feed", "facebook_reels", "story" } : new[] { "feed", "story" };
        return new(score, placements,
            $"offer={relevance:0}; freshness={freshness:0}; organic={organic:0}; paid={paid:0}; rights={evidence.RightsState}; policy={evidence.PolicyState}");
    }
}
