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
        var ads = await db.ManagedAdvertisements.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdExternalId != null && x.ConfiguredStatus != ManagedDeliveryState.Archived).ToListAsync(cancellationToken);
        var usable = allocator.Allocate(envelope.DailyCap, envelope.SafetyReservePercent, 1, true).Usable;
        var currentTotal = ads.Where(x => x.ConfiguredStatus == ManagedDeliveryState.Active)
            .GroupBy(x => x.BudgetOwnerExternalId ?? x.AdSetExternalId ?? x.Id.ToString()).Sum(group => group.Max(x => x.DailyBudget));
        var reviewedBudgetOwners = new HashSet<string>(StringComparer.Ordinal);
        var queued = 0; var windowStart = DateTime.UtcNow.AddDays(-7);
        foreach (var ad in ads)
        {
            var snapshots = await db.AdvertisingInsights.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.TargetId == ad.Id && x.IntervalEndUtc >= windowStart).ToListAsync(cancellationToken);
            var conversions = await db.AdvertisingConversions.IgnoreQueryFilters().Where(x => x.ProjectId == projectId && x.AdvertisementId == ad.Id && x.OccurredAtUtc >= windowStart).ToListAsync(cancellationToken);
            var evidence = evidenceService.Evaluate(snapshots, conversions, Math.Max(25m, ad.DailyBudget));
            string? action = evidence.Verdict switch { EvidenceVerdict.Winner => "IncreaseBudget", EvidenceVerdict.Loser or EvidenceVerdict.Fatigued => "PauseAd", _ => null };
            if (action is null) continue;
            decimal? proposed = null;
            if (action == "IncreaseBudget")
            {
                var budgetOwner = ad.BudgetOwnerExternalId ?? ad.AdSetExternalId ?? ad.Id.ToString();
                if (!reviewedBudgetOwners.Add(budgetOwner)) continue;
                var desired = decimal.Round(ad.DailyBudget * (1m + envelope.MaximumIncreasePercent / 100m), 2);
                proposed = Math.Min(desired, ad.DailyBudget + Math.Max(0m, usable - currentTotal));
                if (proposed <= ad.DailyBudget) continue;
            }
            var review = await ai.ReviewActionAsync(projectId, action, evidence.EvidenceJson, cancellationToken);
            var decision = new AdvertisingDecision { ProjectId = projectId, PromotionId = ad.PromotionId, ActionType = action, TargetType = "Ad", TargetId = ad.Id,
                EvidenceStartUtc = windowStart, EvidenceEndUtc = DateTime.UtcNow, EvidenceJson = evidence.EvidenceJson,
                ProposedChangeJson = JsonSerializer.Serialize(new { status = action == "PauseAd" ? "PAUSED" : null, dailyBudget = proposed }), RiskClass = "Financial",
                State = review.AuditorVerdict == DecisionVerdict.Approve ? DecisionState.Approved : DecisionState.Waiting, EvaluateAfterUtc = DateTime.UtcNow.AddHours(2) };
            db.AdvertisingDecisions.Add(decision);
            db.AdvertisingDecisionReviews.AddRange(
                new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Strategist", Verdict = review.StrategistVerdict, ReasonsJson = review.StrategistJson, EvidenceHash = Hash(evidence.EvidenceJson) },
                new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Auditor", Verdict = review.AuditorVerdict, ReasonsJson = review.AuditorJson, EvidenceHash = Hash(evidence.EvidenceJson) });
            if (review.AuditorVerdict != DecisionVerdict.Approve) continue;
            var positions = JsonSerializer.Deserialize<string[]>(ad.PositionsJson) ?? [];
            var safetyResult = await safety.EvaluateAsync(new(projectId, action, ad.DailyBudget, proposed ?? ad.DailyBudget, ad.PublisherPlatform, positions), cancellationToken);
            db.AdvertisingDecisionReviews.Add(new DecisionReview { ProjectId = projectId, DecisionId = decision.Id, ReviewerType = "Safety", Verdict = safetyResult.Verdict, ReasonsJson = JsonSerializer.Serialize(safetyResult), EvidenceHash = Hash(evidence.EvidenceJson) });
            if (safetyResult.Verdict != DecisionVerdict.Approve) { decision.State = DecisionState.Waiting; continue; }
            var key = $"{projectId:N}:{ad.Id:N}:{action}:{DateTime.UtcNow:yyyyMMdd}";
            if (await db.AdvertisingExecutionCommands.IgnoreQueryFilters().AnyAsync(x => x.ProjectId == projectId && x.IdempotencyKey == key, cancellationToken)) continue;
            var desiredJson = JsonSerializer.Serialize(new { adId = ad.Id, status = action == "PauseAd" ? "PAUSED" : null, dailyBudget = proposed });
            var command = new ExecutionCommand { ProjectId = projectId, DecisionId = decision.Id, IdempotencyKey = key, CommandType = action,
                TargetExternalId = action == "PauseAd" ? ad.AdExternalId : ad.BudgetOwnerExternalId ?? ad.AdSetExternalId, DesiredStateJson = desiredJson, RequestFingerprint = Hash(desiredJson) };
            db.AdvertisingExecutionCommands.Add(command); await db.SaveChangesAsync(cancellationToken);
            jobs.Enqueue<AdvertisingCommandWorker>(worker => worker.ExecuteAsync(projectId, command.Id, CancellationToken.None));
            queued++;
            if (proposed is not null) currentTotal += proposed.Value - ad.DailyBudget;
        }
        await db.SaveChangesAsync(cancellationToken);
        return queued;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
