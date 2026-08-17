using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed class AdvertisingDecisionService(AppDbContext db, AdvertisingDecisionAi ai, AdvertisingSafetyEngine safety)
{
    public async Task<IReadOnlyList<Guid>> ProposeCanaryActivationAsync(Guid projectId, CancellationToken cancellationToken, Guid? promotionId = null)
    {
        var ads = await db.ManagedAdvertisements.Where(x => x.ProjectId == projectId && x.ConfiguredStatus == ManagedDeliveryState.Paused && x.AdExternalId != null && (promotionId == null || x.PromotionId == promotionId)).ToListAsync(cancellationToken);
        if (ads.Count == 0) return [];
        var evidence = JsonSerializer.Serialize(new { projectId, ads = ads.Select(x => new { x.Id, x.DailyBudget, x.PublisherPlatform, x.PositionsJson }), mode = "guarded_canary" });
        var review = await ai.ReviewCanaryAsync(projectId, evidence, cancellationToken);
        var decision = new AdvertisingDecision { ProjectId = projectId, ActionType = "ResumeAd", TargetType = "CanarySet", EvidenceStartUtc = DateTime.UtcNow, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = evidence, ProposedChangeJson = "{\"status\":\"ACTIVE\"}", RiskClass = "Financial", State = review.AuditorVerdict == DecisionVerdict.Approve ? DecisionState.Reviewing : DecisionState.Waiting };
        db.AdvertisingDecisions.Add(decision);
        db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Strategist", Verdict = review.StrategistVerdict, ReasonsJson = review.StrategistJson, EvidenceHash = Hash(evidence) });
        db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Auditor", Verdict = review.AuditorVerdict, ReasonsJson = review.AuditorJson, EvidenceHash = Hash(evidence) });
        if (review.AuditorVerdict != DecisionVerdict.Approve) { await db.SaveChangesAsync(cancellationToken); return []; }
        var commandIds = new List<Guid>();
        foreach (var ad in ads)
        {
            var positions = JsonSerializer.Deserialize<string[]>(ad.PositionsJson) ?? [];
            var safetyResult = await safety.EvaluateAsync(new(projectId, "ResumeAd", ad.DailyBudget, ad.DailyBudget, ad.PublisherPlatform, positions), cancellationToken);
            db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Safety", Verdict = safetyResult.Verdict, ReasonsJson = JsonSerializer.Serialize(new { safetyResult.Code, safetyResult.Message }), EvidenceHash = Hash(evidence) });
            if (safetyResult.Verdict != DecisionVerdict.Approve) continue;
            var command = new ExecutionCommand { ProjectId = projectId, DecisionId = decision.Id, IdempotencyKey = $"{decision.Id:N}:{ad.Id:N}:activate", CommandType = "SetAdActive", TargetExternalId = ad.AdExternalId, DesiredStateJson = JsonSerializer.Serialize(new { adId = ad.Id, status = "ACTIVE" }), RequestFingerprint = Hash($"{ad.AdExternalId}:ACTIVE") };
            db.AdvertisingExecutionCommands.Add(command); commandIds.Add(command.Id);
        }
        decision.State = commandIds.Count > 0 ? DecisionState.Approved : DecisionState.Waiting;
        await db.SaveChangesAsync(cancellationToken);
        return commandIds;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
}
