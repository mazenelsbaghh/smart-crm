using System.Text.Json;
using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public enum EvidenceVerdict { Wait, Winner, Loser, Healthy, Fatigued }
public sealed record AdvertisingEvidence(EvidenceVerdict Verdict, decimal Spend, decimal Revenue, int Conversions, decimal Roas,
    decimal Cpa, decimal Ctr, decimal Frequency, string EvidenceJson);

public sealed class AdvertisingEvidenceService
{
    public AdvertisingEvidence Evaluate(IEnumerable<InsightsSnapshot> snapshots, IEnumerable<CanonicalConversion> conversions, decimal targetCpa)
    {
        var rows = snapshots.ToList(); var outcomes = conversions.Where(x => x.State is not ConversionState.Suppressed).ToList();
        var spend = rows.Sum(x => x.Spend); var impressions = rows.Sum(x => x.Impressions); var clicks = rows.Sum(x => x.Clicks);
        var revenue = outcomes.Sum(x => x.CurrentValue ?? 0m); var count = outcomes.Count(x => IsStrong(x.EventType));
        var roas = spend > 0 ? revenue / spend : 0m; var cpa = count > 0 ? spend / count : 0m;
        var ctr = impressions > 0 ? (decimal)clicks / impressions * 100m : 0m; var frequency = rows.Count > 0 ? rows.Max(x => x.Frequency) : 0m;
        var enough = rows.Count >= 2 && (impressions >= 1000 || spend >= targetCpa);
        var verdict = !enough ? EvidenceVerdict.Wait
            : frequency >= 4m && ctr < 0.8m ? EvidenceVerdict.Fatigued
            : count >= 2 && roas >= 1.5m ? EvidenceVerdict.Winner
            : count == 0 && spend >= targetCpa * 2m ? EvidenceVerdict.Loser
            : EvidenceVerdict.Healthy;
        return new(verdict, spend, revenue, count, roas, cpa, ctr, frequency,
            JsonSerializer.Serialize(new { spend, revenue, count, roas, cpa, ctr, frequency, snapshots = rows.Count }));
    }

    private static bool IsStrong(string eventType) => eventType is "Purchase" or "SubscriptionStarted" or "SubscriptionRenewed" or "EnrollmentPaid" or "AttendanceConfirmed" or "DealWon";
}
