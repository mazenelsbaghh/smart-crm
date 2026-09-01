using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public enum EvidenceVerdict { Wait, Winner, Loser, Healthy, Fatigued }
public sealed record AdvertisingEvidence(EvidenceVerdict Verdict, decimal Spend, decimal Revenue, int Conversions, decimal Roas,
    decimal Cpa, decimal Ctr, decimal Frequency, string EvidenceJson, IReadOnlyList<string>? WaitReasons = null,
    string OutcomeLevel = "None");
public sealed record AdvertisingEvidencePackage(Guid ProjectId, DateTime WindowStartUtc, DateTime WindowEndUtc,
    DateTime AsOfUtc, string TruthSource, int TrackingPolicyVersion, TrackingHealthState TrackingState,
    AdvertisingEvidence Evaluation, string EvidenceJson, string EvidenceHash);

public sealed class AdvertisingEvidenceService
{
    public AdvertisingEvidencePackage BuildPackage(Guid projectId, DateTime windowStartUtc, DateTime windowEndUtc,
        IEnumerable<InsightsSnapshot> snapshots, IEnumerable<CanonicalConversion> conversions, decimal targetCpa,
        TrackingHealthSnapshot? tracking, DateTime? asOfUtc = null)
    {
        var asOf = asOfUtc ?? DateTime.UtcNow;
        if (windowStartUtc >= windowEndUtc || windowEndUtc > asOf.AddMinutes(1))
            throw new AdvertisingException("ADS_EVIDENCE_WINDOW_INVALID", "Decision evidence window is incoherent.", 422);
        var insightRows = snapshots.Where(item => item.IntervalStartUtc >= windowStartUtc
            && item.IntervalEndUtc <= windowEndUtc && item.FetchedAtUtc <= asOf).ToArray();
        var outcomeRows = conversions.Where(item => item.OccurredAtUtc >= windowStartUtc
            && item.OccurredAtUtc < windowEndUtc).ToArray();
        var evaluation = Evaluate(insightRows, outcomeRows, targetCpa, asOf,
            trackingHealthy: tracking?.State == TrackingHealthState.Healthy);
        var json = JsonSerializer.Serialize(new
        {
            projectId, windowStartUtc, windowEndUtc, asOfUtc = asOf, truthSource = "CanonicalConversionLedger",
            tracking = tracking is null ? null : new { tracking.Id, tracking.TrackingHealthPolicyVersion,
                state = tracking.State.ToString(), tracking.EvaluatedAtUtc, tracking.ReasonCodesJson },
            insights = insightRows.Select(item => new { item.Id, item.TargetId, item.Revision, item.IntervalStartUtc,
                item.IntervalEndUtc, item.FetchedAtUtc, item.Spend, item.Impressions }),
            outcomes = outcomeRows.Select(item => new { item.Id, item.EventType, item.TruthState,
                attributionState = item.AttributionState.ToString(), correctionState = item.CorrectionState.ToString(),
                item.CurrentValue, item.OccurredAtUtc }),
            evaluation = new { verdict = evaluation.Verdict.ToString(), evaluation.OutcomeLevel, evaluation.Spend,
                evaluation.Revenue, evaluation.Conversions, evaluation.Roas, evaluation.Cpa, evaluation.WaitReasons }
        });
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();
        return new(projectId, windowStartUtc, windowEndUtc, asOf, "CanonicalConversionLedger",
            tracking?.TrackingHealthPolicyVersion ?? 0, tracking?.State ?? TrackingHealthState.Unknown,
            evaluation, json, hash);
    }

    public AdvertisingEvidence Evaluate(IEnumerable<InsightsSnapshot> snapshots, IEnumerable<CanonicalConversion> conversions, decimal targetCpa,
        DateTime? nowUtc = null, int attributionDelayHours = 24, bool trackingHealthy = true)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var rows = snapshots.Where(x => x.IsCurrent).ToList();
        var outcomes = conversions.Where(x => x.State is not ConversionState.Suppressed).ToList();
        var spend = rows.Sum(x => x.Spend); var impressions = rows.Sum(x => x.Impressions); var clicks = rows.Sum(x => x.Clicks);
        var paid = outcomes.Where(x => IsPaid(x.EventType) && x.CurrentValue > 0).ToList();
        var qualified = outcomes.Where(x => x.EventType == "QualifiedLead").ToList();
        var verifiedLeads = outcomes.Where(x => x.EventType is "Lead" or "BookingConfirmed").ToList();
        var revenue = paid.Sum(x => x.CurrentValue ?? 0m);
        var messageStarts = rows.Sum(WhatsAppMessageStarts);
        var outcomeLevel = paid.Count > 0 ? "PaidPurchase" : qualified.Count > 0 ? "QualifiedWhatsAppLead" : verifiedLeads.Count > 0 ? "VerifiedLead" : "NewMessagingConversation";
        var count = paid.Count > 0 ? paid.Count : qualified.Count > 0 ? qualified.Count : verifiedLeads.Count > 0 ? verifiedLeads.Count : messageStarts;
        var roas = spend > 0 ? revenue / spend : 0m; var cpa = count > 0 ? spend / count : 0m;
        var ctr = impressions > 0 ? (decimal)clicks / impressions * 100m : 0m; var frequency = rows.Count > 0 ? rows.Max(x => x.Frequency) : 0m;
        var waitReasons = new List<string>();
        if (rows.Count < 2) waitReasons.Add("ADS_WAIT_INSUFFICIENT_SNAPSHOTS");
        if (spend < targetCpa && impressions < 1000) waitReasons.Add("ADS_WAIT_INSUFFICIENT_VOLUME");
        if (rows.Count > 0 && now - rows.Max(x => x.IntervalEndUtc) < TimeSpan.FromHours(attributionDelayHours)) waitReasons.Add("ADS_WAIT_ATTRIBUTION_DELAY");
        if (rows.Any(x => x.LearningStatus?.Contains("LEARNING", StringComparison.OrdinalIgnoreCase) == true)) waitReasons.Add("ADS_WAIT_LEARNING");
        if (outcomes.Any(x => x.CorrectionState == CorrectionState.PendingBase)) waitReasons.Add("ADS_WAIT_PENDING_CORRECTION");
        if (!trackingHealthy) waitReasons.Add("ADS_WAIT_TRACKING_UNSAFE");
        var verdict = waitReasons.Count > 0 ? EvidenceVerdict.Wait
            : frequency >= 4m && ctr < 0.8m ? EvidenceVerdict.Fatigued
            : count >= 2 && roas >= 1.5m ? EvidenceVerdict.Winner
            : count == 0 && spend >= targetCpa * 2m ? EvidenceVerdict.Loser
            : EvidenceVerdict.Healthy;
        return new(verdict, spend, revenue, count, roas, cpa, ctr, frequency,
            JsonSerializer.Serialize(new { spend, revenue, count, paidPurchases = paid.Count, qualifiedLeads = qualified.Count,
                verifiedLeads = verifiedLeads.Count, messageStarts, outcomeLevel, roas, cpa, ctr, frequency,
                snapshots = rows.Count, waitReasons }), waitReasons, outcomeLevel);
    }

    private static int WhatsAppMessageStarts(InsightsSnapshot snapshot)
    {
        using var document = JsonDocument.Parse(snapshot.ProviderActionsJson);
        if (!document.RootElement.TryGetProperty("Actions", out var actions) || actions.ValueKind != JsonValueKind.Object) return 0;
        return actions.EnumerateObject().Where(action => action.Name.Contains("messaging", StringComparison.OrdinalIgnoreCase)
                || action.Name.Contains("conversation", StringComparison.OrdinalIgnoreCase))
            .Sum(action => decimal.ToInt32(action.Value.GetDecimal()));
    }

    private static bool IsPaid(string eventType) => eventType is "Purchase" or "SubscriptionStarted" or "SubscriptionRenewed" or "EnrollmentPaid" or "DealWon";
}
