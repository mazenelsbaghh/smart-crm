using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record DecisionImpactEvidence(decimal BaselineMetric, decimal EvaluationMetric,
    int BaselineSample, int EvaluationSample, DateTime BaselineStartUtc, DateTime BaselineEndUtc,
    DateTime EvaluationStartUtc, DateTime EvaluationEndUtc, string Goal,
    bool HasPendingCorrection = false, string? RollbackDesiredStateJson = null);

public sealed class AdvertisingDecisionImpactService(AppDbContext db)
{
    public async Task<DecisionImpact?> EvaluateAsync(Guid projectId, Guid decisionId, DecisionImpactEvidence evidence,
        DateTime? nowUtc = null, CancellationToken cancellationToken = default)
    {
        var now = nowUtc ?? DateTime.UtcNow;
        var decision = await db.AdvertisingDecisions.IgnoreQueryFilters().SingleAsync(item =>
            item.ProjectId == projectId && item.Id == decisionId, cancellationToken);
        if (decision.State != DecisionState.Executed || decision.EvaluateAfterUtc is { } due && now < due) return null;
        var prior = await db.AdvertisingDecisionImpacts.IgnoreQueryFilters().Where(item =>
            item.ProjectId == projectId && item.DecisionId == decisionId).OrderByDescending(item => item.EvaluatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var label = Label(evidence);
        if (prior?.Label == DecisionImpactLabel.Negative && label is DecisionImpactLabel.Positive or DecisionImpactLabel.Inconclusive)
            label = DecisionImpactLabel.Reverted;
        var impact = new DecisionImpact { ProjectId = projectId, DecisionId = decisionId,
            BaselineWindowStartUtc = evidence.BaselineStartUtc, BaselineWindowEndUtc = evidence.BaselineEndUtc,
            EvaluationWindowStartUtc = evidence.EvaluationStartUtc, EvaluationWindowEndUtc = evidence.EvaluationEndUtc,
            Goal = evidence.Goal, BaselineEvidenceJson = JsonSerializer.Serialize(new { metric = evidence.BaselineMetric, sample = evidence.BaselineSample }),
            EvaluationEvidenceJson = JsonSerializer.Serialize(new { metric = evidence.EvaluationMetric, sample = evidence.EvaluationSample,
                evidence.HasPendingCorrection }), Label = label, EvaluatedAtUtc = now };
        db.AdvertisingDecisionImpacts.Add(impact);
        if (label == DecisionImpactLabel.Negative && !string.IsNullOrWhiteSpace(evidence.RollbackDesiredStateJson))
        {
            var executed = await db.AdvertisingExecutionCommands.IgnoreQueryFilters().Where(item =>
                item.ProjectId == projectId && item.DecisionId == decisionId && item.State == CommandState.Succeeded)
                .OrderByDescending(item => item.CompletedAtUtc).FirstOrDefaultAsync(cancellationToken);
            if (executed is not null)
            {
                var fingerprint = Hash(evidence.RollbackDesiredStateJson);
                var rollback = new ExecutionCommand { ProjectId = projectId, DecisionId = decisionId,
                    IdempotencyKey = $"rollback:{decisionId:N}:{fingerprint}", CommandType = "Rollback",
                    TargetExternalId = executed.TargetExternalId, ExpectedStateHash = executed.ExpectedStateHash,
                    DesiredStateJson = evidence.RollbackDesiredStateJson, RequestFingerprint = fingerprint };
                db.AdvertisingExecutionCommands.Add(rollback); impact.RollbackCommandId = rollback.Id;
            }
        }
        AdvertisingAudit.Add(db, projectId, "DecisionImpactEvaluated", nameof(AdvertisingDecision), decisionId,
            new { label = label.ToString(), evidence.Goal, evidence.BaselineSample, evidence.EvaluationSample,
                evidence.HasPendingCorrection, rollbackQueued = impact.RollbackCommandId is not null });
        await db.SaveChangesAsync(cancellationToken);
        return impact;
    }

    private static DecisionImpactLabel Label(DecisionImpactEvidence evidence)
    {
        if (evidence.HasPendingCorrection || evidence.BaselineSample < 3 || evidence.EvaluationSample < 3
            || evidence.BaselineMetric <= 0) return DecisionImpactLabel.Inconclusive;
        var change = (evidence.EvaluationMetric - evidence.BaselineMetric) / evidence.BaselineMetric;
        return change >= .05m ? DecisionImpactLabel.Positive
            : change <= -.05m ? DecisionImpactLabel.Negative : DecisionImpactLabel.Inconclusive;
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
