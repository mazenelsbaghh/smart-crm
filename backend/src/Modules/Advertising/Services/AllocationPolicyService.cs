using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Workers;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed class AllocationPolicyService(AppDbContext db, AdvertisingEvidenceService evidenceService, AdvertisingDecisionAi ai,
    AdvertisingSafetyEngine safety, BudgetAllocator allocator, IBackgroundJobClient jobs)
{
    public async Task<int> RebalanceAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var envelope = await db.AutonomyEnvelopes.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == EnvelopeState.Active, cancellationToken);
        if (envelope is null) return 0;
        var authorizedOwnership = await db.AdvertisingManagedOwnership.IgnoreQueryFilters()
            .Where(x => x.ProjectId == projectId && x.RevokedAtUtc == null &&
                (x.OwnershipKind == ManagedOwnershipKind.AutopilotCreated || x.OwnershipKind == ManagedOwnershipKind.ImportedWithAuthority))
            .Select(x => x.Id).ToListAsync(cancellationToken);
        var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdExternalId != null
            && x.ConfiguredStatus != ManagedDeliveryState.Archived && x.OwnershipRecordId != null
            && authorizedOwnership.Contains(x.OwnershipRecordId.Value)).ToListAsync(cancellationToken);
        var usable = allocator.Allocate(envelope.DailyCap, envelope.SafetyReservePercent, 1, true).Usable;
        var currentTotal = ads.Where(x => x.ConfiguredStatus == ManagedDeliveryState.Active)
            .GroupBy(x => x.BudgetOwnerExternalId ?? x.AdSetExternalId ?? x.Id.ToString()).Sum(group => group.Max(x => x.DailyBudget));
        var reviewedBudgetOwners = new HashSet<string>(StringComparer.Ordinal);
        var queued = 0; var windowStart = DateTime.UtcNow.AddDays(-7);
        var latestTracking = await db.AdvertisingTrackingHealthSnapshots.IgnoreQueryFilters().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.EvaluatedAtUtc).FirstOrDefaultAsync(cancellationToken);
        foreach (var ad in ads)
        {
            var snapshots = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId
                && x.TargetId == ad.Id && x.IntervalEndUtc >= windowStart && x.IsCurrent).ToListAsync(cancellationToken);
            var conversions = await db.AdvertisingConversions.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdvertisementId == ad.Id && x.OccurredAtUtc >= windowStart).ToListAsync(cancellationToken);
            var evidencePackage = evidenceService.BuildPackage(projectId, windowStart, DateTime.UtcNow,
                snapshots, conversions, Math.Max(25m, ad.DailyBudget), latestTracking);
            var evidence = evidencePackage.Evaluation;
            string? action = evidence.Verdict switch { EvidenceVerdict.Winner => "IncreaseBudget", EvidenceVerdict.Loser or EvidenceVerdict.Fatigued => "PauseAd", _ => null };
            if (action is null) continue;
            if (action == "PauseAd" && ad.ConfiguredStatus != ManagedDeliveryState.Active) continue;
            var cooldownStart = DateTime.UtcNow.AddHours(-envelope.CooldownHours);
            if (await db.AdvertisingDecisions.IgnoreQueryFilters().AnyAsync(decision => decision.ProjectId == projectId
                && decision.TargetId == ad.Id && decision.ActionType == action && decision.CreatedAt >= cooldownStart
                && (decision.State == DecisionState.Approved || decision.State == DecisionState.Executing
                    || decision.State == DecisionState.Executed),
                cancellationToken)) continue;
            decimal? proposed = null;
            if (action == "IncreaseBudget")
            {
                var budgetOwner = ad.BudgetOwnerExternalId ?? ad.AdSetExternalId ?? ad.Id.ToString();
                if (!reviewedBudgetOwners.Add(budgetOwner)) continue;
                var desired = decimal.Round(ad.DailyBudget * (1m + envelope.MaximumIncreasePercent / 100m), 2);
                proposed = Math.Min(desired, ad.DailyBudget + Math.Max(0m, usable - currentTotal));
                if (proposed <= ad.DailyBudget) continue;
            }
            var decision = await db.AdvertisingDecisions.IgnoreQueryFilters().OrderByDescending(item => item.CreatedAt)
                .FirstOrDefaultAsync(item => item.ProjectId == projectId && item.TargetId == ad.Id
                    && item.ActionType == action && item.State == DecisionState.Waiting
                    && item.CreatedAt >= DateTime.UtcNow.AddHours(-1), cancellationToken);
            if (decision is not null && action == "IncreaseBudget") proposed = ProposedBudget(decision.ProposedChangeJson);
            var reviewEvidenceJson = decision?.EvidenceJson ?? evidencePackage.EvidenceJson;
            var review = await ai.ReviewActionAsync(projectId, action, reviewEvidenceJson, cancellationToken);
            if (decision is null)
            {
                decision = new AdvertisingDecision { ProjectId = projectId, PromotionId = ad.PromotionId,
                    ActionType = action, TargetType = "Ad", TargetId = ad.Id,
                    EvidenceStartUtc = evidencePackage.WindowStartUtc, EvidenceEndUtc = evidencePackage.WindowEndUtc,
                    EvidenceJson = evidencePackage.EvidenceJson, EvidenceHash = evidencePackage.EvidenceHash,
                    ProposedChangeJson = JsonSerializer.Serialize(new { status = action == "PauseAd" ? "PAUSED" : null, dailyBudget = proposed }),
                    RiskClass = "Financial", EvaluateAfterUtc = DateTime.UtcNow.AddHours(2) };
                db.AdvertisingDecisions.Add(decision);
            }
            decision.ReasonCodesJson = JsonSerializer.Serialize(new[] { review.Reason });
            decision.State = review.StrategistVerdict == DecisionVerdict.Approve
                && review.AuditorVerdict == DecisionVerdict.Approve && review.JudgeVerdict == DecisionVerdict.Approve
                    ? DecisionState.Approved : DecisionState.Waiting;
            db.AdvertisingDecisionReviews.AddRange(
                new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Strategist", Verdict = review.StrategistVerdict, ReasonsJson = review.StrategistJson, EvidenceHash = decision.EvidenceHash, ReviewedAtUtc = DateTime.UtcNow },
                new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Auditor", Verdict = review.AuditorVerdict, ReasonsJson = review.AuditorJson, EvidenceHash = decision.EvidenceHash, ReviewedAtUtc = DateTime.UtcNow },
                new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Judge", Verdict = review.JudgeVerdict, ReasonsJson = review.JudgeJson, EvidenceHash = decision.EvidenceHash, ReviewedAtUtc = DateTime.UtcNow });
            AdvertisingAudit.Add(db, projectId, "DecisionReviewed", nameof(AdvertisingDecision), decision.Id,
                new { decision.ActionType, decision.TargetId, decision.EvidenceHash,
                    strategist = review.StrategistVerdict.ToString(), auditor = review.AuditorVerdict.ToString(),
                    judge = review.JudgeVerdict.ToString(), review.Reason });
            if (review.StrategistVerdict != DecisionVerdict.Approve || review.AuditorVerdict != DecisionVerdict.Approve
                || review.JudgeVerdict != DecisionVerdict.Approve) continue;
            var positions = JsonSerializer.Deserialize<string[]>(ad.PositionsJson) ?? [];
            var safetyResult = await safety.EvaluateAsync(new(projectId, action, ad.DailyBudget,
                proposed ?? ad.DailyBudget, ad.PublisherPlatform, positions, ad.Id, ad.DestinationId,
                ad.ProviderStateHash), cancellationToken);
            db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Safety", Verdict = safetyResult.Verdict, ReasonsJson = JsonSerializer.Serialize(safetyResult), EvidenceHash = decision.EvidenceHash, ReviewedAtUtc = DateTime.UtcNow });
            AdvertisingAudit.Add(db, projectId, "DecisionSafetyEvaluated", nameof(AdvertisingDecision), decision.Id,
                new { verdict = safetyResult.Verdict.ToString(), safetyResult.Code, adId = ad.Id });
            if (safetyResult.Verdict != DecisionVerdict.Approve) { decision.State = DecisionState.Waiting; continue; }
            var key = $"{projectId:N}:{ad.Id:N}:{action}:{DateTime.UtcNow:yyyyMMdd}";
            if (await db.AdvertisingExecutionCommands.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.IdempotencyKey == key, cancellationToken)) continue;
            var desiredJson = JsonSerializer.Serialize(new { adId = ad.Id, status = action == "PauseAd" ? "PAUSED" : null, dailyBudget = proposed });
            var command = new ExecutionCommand { ProjectId = projectId, DecisionId = decision.Id, IdempotencyKey = key, CommandType = action,
                TargetExternalId = action == "PauseAd" ? ad.AdExternalId : ad.BudgetOwnerExternalId ?? ad.AdSetExternalId,
                ExpectedStateHash = ad.ProviderStateHash, DesiredStateJson = desiredJson, RequestFingerprint = Hash(desiredJson) };
            db.AdvertisingExecutionCommands.Add(command); await db.SaveChangesAsync(cancellationToken);
            AdvertisingAudit.Add(db, projectId, "ExecutionCommandQueued", nameof(ExecutionCommand), command.Id,
                new { command.CommandType, command.RequestFingerprint, adId = ad.Id });
            jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, command.Id, CancellationToken.None));
            queued++;
            if (proposed is not null) currentTotal += proposed.Value - ad.DailyBudget;
        }
        await db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static decimal? ProposedBudget(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.TryGetProperty("dailyBudget", out var budget)
            && budget.ValueKind == JsonValueKind.Number ? budget.GetDecimal() : null;
    }
}
