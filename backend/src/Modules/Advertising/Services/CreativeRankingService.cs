using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public sealed record CreativeRank(decimal Score, IReadOnlyCollection<string> Placements, string Explanation);

public static class CreativeRankingService
{
    public static CreativeRank Rank(CreativeMediaType mediaType, DateTime? createdAtUtc, DateTime nowUtc)
    {
        var ageDays = createdAtUtc is null ? 30 : Math.Max(0, (nowUtc - createdAtUtc.Value).TotalDays);
        var score = Math.Max(30m, 90m - (decimal)Math.Min(60, ageDays));
        var placements = mediaType == CreativeMediaType.Video ? new[] { "feed", "facebook_reels" } : new[] { "feed", "story" };
        return new(score, placements, $"Source freshness: {Math.Round(ageDays)} days");
    }
}
