using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed class AdvertisingDecisionService(AppDbContext db, AdvertisingDecisionAi ai, AdvertisingSafetyEngine safety,
    AdvertisingOwnershipPolicy ownership)
{
    public async Task<IReadOnlyList<Guid>> ProposeCanaryActivationAsync(Guid projectId, CancellationToken cancellationToken, Guid? promotionId = null,
        IReadOnlyCollection<Guid>? adIds = null)
    {
        var ads = (await ownership.ManagedAdsAsync(projectId, activeOnly: false, cancellationToken))
            .Where(ad => ad.ConfiguredStatus == ManagedDeliveryState.Paused
                && (promotionId == null || ad.PromotionId == promotionId)
                && (adIds == null || adIds.Contains(ad.Id)))
            .ToList();
        if (ads.Count == 0) return [];
        var evidence = JsonSerializer.Serialize(new { projectId, ads = ads.Select(x => new { x.Id, x.DailyBudget, x.PublisherPlatform, x.PositionsJson }), mode = "guarded_canary" });
        var evidenceHash = Hash(evidence);
        var review = await ai.ReviewCanaryAsync(projectId, evidence, cancellationToken);
        var reviewsApprove = review.StrategistVerdict == DecisionVerdict.Approve
            && review.AuditorVerdict == DecisionVerdict.Approve && review.JudgeVerdict == DecisionVerdict.Approve;
        var decision = await db.AdvertisingDecisions.IgnoreQueryFilters().OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.ActionType == "ResumeAd"
                && item.TargetType == "CanarySet" && item.EvidenceHash == evidenceHash
                && item.State == DecisionState.Waiting, cancellationToken);
        if (decision is null)
        {
            decision = new AdvertisingDecision { ProjectId = projectId, ActionType = "ResumeAd", TargetType = "CanarySet",
                EvidenceStartUtc = DateTime.UtcNow, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = evidence,
                EvidenceHash = evidenceHash, ProposedChangeJson = "{\"status\":\"ACTIVE\"}", RiskClass = "Financial" };
            db.AdvertisingDecisions.Add(decision);
        }
        decision.ReasonCodesJson = JsonSerializer.Serialize(new[] { review.Reason });
        decision.State = reviewsApprove ? DecisionState.Reviewing : DecisionState.Waiting;
        await AddReviewIfChanged(decision, "Strategist", review.StrategistVerdict, review.StrategistJson, cancellationToken);
        await AddReviewIfChanged(decision, "Auditor", review.AuditorVerdict, review.AuditorJson, cancellationToken);
        await AddReviewIfChanged(decision, "Judge", review.JudgeVerdict, review.JudgeJson, cancellationToken);
        AdvertisingAudit.Add(db, projectId, "DecisionReviewed", nameof(AdvertisingDecision), decision.Id,
            new { decision.ActionType, decision.EvidenceHash, strategist = review.StrategistVerdict.ToString(),
                auditor = review.AuditorVerdict.ToString(), judge = review.JudgeVerdict.ToString(), review.Reason });
        if (!reviewsApprove) { await db.SaveChangesAsync(cancellationToken); return []; }
        var commandIds = new List<Guid>();
        foreach (var ad in ads)
        {
            var positions = JsonSerializer.Deserialize<string[]>(ad.PositionsJson) ?? [];
            var safetyResult = await safety.EvaluateAsync(new(projectId, "ResumeAd", ad.DailyBudget,
                ad.DailyBudget, ad.PublisherPlatform, positions, ad.Id, ad.DestinationId, ad.ProviderStateHash), cancellationToken);
            db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Safety", Verdict = safetyResult.Verdict, ReasonsJson = JsonSerializer.Serialize(new { safetyResult.Code, safetyResult.Message }), EvidenceHash = Hash(evidence), ReviewedAtUtc = DateTime.UtcNow });
            AdvertisingAudit.Add(db, projectId, "DecisionSafetyEvaluated", nameof(AdvertisingDecision), decision.Id,
                new { adId = ad.Id, verdict = safetyResult.Verdict.ToString(), safetyResult.Code });
            if (safetyResult.Verdict != DecisionVerdict.Approve) continue;
            var command = new ExecutionCommand { ProjectId = projectId, DecisionId = decision.Id, IdempotencyKey = $"{decision.Id:N}:{ad.Id:N}:activate", CommandType = "SetAdActive", TargetExternalId = ad.AdExternalId, DesiredStateJson = JsonSerializer.Serialize(new { adId = ad.Id, status = "ACTIVE" }), RequestFingerprint = Hash($"{ad.AdExternalId}:ACTIVE") };
            db.AdvertisingExecutionCommands.Add(command); commandIds.Add(command.Id);
            AdvertisingAudit.Add(db, projectId, "ExecutionCommandQueued", nameof(ExecutionCommand), command.Id,
                new { command.CommandType, adId = ad.Id, command.RequestFingerprint });
        }
        decision.State = commandIds.Count > 0 ? DecisionState.Approved : DecisionState.Waiting;
        await db.SaveChangesAsync(cancellationToken);
        return commandIds;
    }

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

    private async Task AddReviewIfChanged(AdvertisingDecision decision, string reviewer,
        DecisionVerdict verdict, string reasonsJson, CancellationToken cancellationToken)
    {
        var duplicate = await db.AdvertisingDecisionReviews.IgnoreQueryFilters().AnyAsync(item =>
            item.ProjectId == decision.ProjectId && item.DecisionId == decision.Id && item.ReviewerType == reviewer
            && item.Verdict == verdict && item.ReasonsJson == reasonsJson && item.EvidenceHash == decision.EvidenceHash,
            cancellationToken);
        if (!duplicate) db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = decision.ProjectId,
            DecisionId = decision.Id, ReviewerType = reviewer, Verdict = verdict, ReasonsJson = reasonsJson,
            EvidenceHash = decision.EvidenceHash, ReviewedAtUtc = DateTime.UtcNow });
    }
}
