using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Services;

public sealed record AiActivationReview(DecisionVerdict StrategistVerdict, DecisionVerdict AuditorVerdict,
    string StrategistJson, string AuditorJson, string Reason, DecisionVerdict JudgeVerdict = DecisionVerdict.Approve,
    string JudgeJson = "{}");
internal sealed record RoleReviewResponse(string Verdict, string[] Reasons);

public sealed record AdvertisingAiWorkRequestContract(
    Guid RequestId,
    Guid ProjectId,
    Guid OwnerId,
    long OwnerVersion,
    string Purpose,
    string InputHash,
    string SourcedInputJson,
    DateTime DeadlineUtc)
{
    public static AdvertisingAiWorkRequestContract Create(Guid requestId, Guid projectId, string purpose, string inputHash, string sourcedInputJson) =>
        new(requestId, projectId, requestId, 1, purpose, inputHash, sourcedInputJson, DateTime.UtcNow.AddMinutes(5));
}

public static class AdvertisingAiWorkResultGuard
{
    public static AiWorkCompletionDecision Evaluate(AdvertisingAiWorkItemSnapshot work, AdvertisingAiWorkCompletion completion, DateTime nowUtc)
    {
        if (work.State != AiWorkState.Pending) return AiWorkCompletionDecision.RejectState;
        if (work.OwnerId != completion.OwnerId || work.ProjectId != completion.ProjectId) return AiWorkCompletionDecision.RejectOwner;
        if (work.OwnerVersion != completion.OwnerVersion) return AiWorkCompletionDecision.RejectVersion;
        if (!string.Equals(work.InputHash, completion.InputHash, StringComparison.Ordinal)) return AiWorkCompletionDecision.RejectHash;
        return nowUtc > work.DeadlineUtc ? AiWorkCompletionDecision.RejectExpired : AiWorkCompletionDecision.Accept;
    }
}

public sealed class AdvertisingDecisionAi(AppDbContext db)
{
    public Task<AiActivationReview> ReviewCanaryAsync(Guid projectId, string evidenceJson, CancellationToken cancellationToken = default) =>
        ReviewAsync(projectId, "ActivateCanary", evidenceJson, cancellationToken);

    public Task<AiActivationReview> ReviewActionAsync(Guid projectId, string action, string evidenceJson, CancellationToken cancellationToken = default)
    {
        return AdvertisingDecisionPolicy.IsSupported(action)
            ? ReviewAsync(projectId, action, evidenceJson, cancellationToken)
            : Task.FromResult(new AiActivationReview(DecisionVerdict.Reject, DecisionVerdict.Reject, "{}", "{}", "UNSUPPORTED_ACTION"));
    }

    private async Task<AiActivationReview> ReviewAsync(Guid projectId, string action, string evidenceJson, CancellationToken cancellationToken)
    {
        var sourcedEvidence = JsonSerializer.Deserialize<JsonElement>(evidenceJson);
        var roles = AdvertisingDecisionPolicy.RequiresJudge(action, evidenceJson)
            ? new[] { "Strategist", "Auditor", "Judge" } : new[] { "Strategist", "Auditor" };
        var results = new Dictionary<string, (DecisionVerdict Verdict, string Json, string Reason)>(StringComparer.Ordinal);
        foreach (var role in roles)
        {
            var inputJson = JsonSerializer.Serialize(new
            {
                role, action, evidence = sourcedEvidence,
                priorReviews = results.ToDictionary(item => item.Key, item => new { verdict = item.Value.Verdict.ToString(), reason = item.Value.Reason }),
                outputContract = new { verdict = "APPROVE|REJECT|WAIT|ESCALATE", reasons = new[] { "reason_code" } },
                rules = new[] { "Use only supplied evidence", "Never invent provider state or identifiers", "Return WAIT when evidence is insufficient" }
            });
            var inputHash = Hash(inputJson);
            var ownerId = StableGuid(projectId, action, role, Hash(evidenceJson));
            var existing = await db.AdvertisingAiWorkItems.IgnoreQueryFilters()
                .Where(x => x.ProjectId == projectId && x.OwnerId == ownerId && x.Purpose == role && x.InputHash == inputHash)
                .OrderByDescending(x => x.CreatedAt).FirstOrDefaultAsync(cancellationToken);
            if (existing?.State == AiWorkState.Completed && TryRoleReview(existing.ResultJson, out var verdict, out var reason))
            {
                results[role] = (verdict, existing.ResultJson!, reason);
                if (verdict != DecisionVerdict.Approve) return BuildReview(results, reason);
                continue;
            }
            if (existing is null || existing.State is AiWorkState.Failed or AiWorkState.Expired or AiWorkState.Stale)
            {
                var work = new AdvertisingAiWorkItem { ProjectId = projectId, Purpose = role,
                    PromptVersion = $"advertising-{role.ToLowerInvariant()}.v2", InputVersion = "2",
                    InputHash = inputHash, InputJson = inputJson, OwnerId = ownerId, OwnerVersion = 1,
                    DeadlineUtc = DateTime.UtcNow.AddMinutes(5) };
                db.AdvertisingAiWorkItems.Add(work);
                IntegrationOutbox.Enqueue(db, new AdvertisingAiWorkRequested { ProjectId = projectId,
                    RequestId = work.Id, OwnerId = ownerId, OwnerVersion = 1, Purpose = role,
                    InputHash = inputHash, SourcedInputJson = inputJson, DeadlineUtc = work.DeadlineUtc,
                    SourceAggregateType = nameof(AdvertisingAiWorkItem), SourceAggregateId = work.Id, SourceVersion = 1 });
                await db.SaveChangesAsync(cancellationToken);
            }
            var pendingReason = $"AI_{role.ToUpperInvariant()}_PENDING";
            results[role] = (DecisionVerdict.Wait, "{}", pendingReason);
            return BuildReview(results, pendingReason);
        }
        return BuildReview(results, "AI_REVIEWS_APPROVED");
    }

    private static bool TryRoleReview(string? raw, out DecisionVerdict verdict, out string reason)
    {
        verdict = DecisionVerdict.Wait; reason = "AI_SCHEMA_INVALID";
        if (string.IsNullOrWhiteSpace(raw)) return false;
        try
        {
            var parsed = JsonSerializer.Deserialize<RoleReviewResponse>(StripFence(raw), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (parsed is null) return false;
            verdict = ParseVerdict(parsed.Verdict); reason = string.Join(',', parsed.Reasons ?? []);
            return true;
        }
        catch (JsonException) { return false; }
    }

    private static DecisionVerdict ParseVerdict(string? value) => value?.ToUpperInvariant() switch
    {
        "APPROVE" => DecisionVerdict.Approve,
        "REJECT" => DecisionVerdict.Reject,
        "ESCALATE" => DecisionVerdict.Escalate,
        _ => DecisionVerdict.Wait
    };

    private static string StripFence(string raw) => raw.Trim().Replace("```json", "", StringComparison.OrdinalIgnoreCase).Replace("```", "", StringComparison.Ordinal).Trim();

    private static AiActivationReview BuildReview(
        IReadOnlyDictionary<string, (DecisionVerdict Verdict, string Json, string Reason)> results, string reason)
    {
        var strategist = results.TryGetValue("Strategist", out var strategistResult)
            ? strategistResult : (DecisionVerdict.Wait, "{}", reason);
        var auditor = results.TryGetValue("Auditor", out var auditorResult)
            ? auditorResult : (DecisionVerdict.Wait, "{}", reason);
        var judge = results.TryGetValue("Judge", out var judgeResult)
            ? judgeResult : (DecisionVerdict.Approve, "{}", reason);
        return new(strategist.Item1, auditor.Item1, strategist.Item2, auditor.Item2, reason, judge.Item1, judge.Item2);
    }

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private static Guid StableGuid(Guid projectId, string action, string role, string evidenceHash)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{projectId:N}:{action}:{role}:{evidenceHash}"));
        return new Guid(bytes.AsSpan(0, 16));
    }
}

public static class AdvertisingDecisionPolicy
{
    private static readonly HashSet<string> Supported = Enum.GetNames<AutonomousActionType>()
        .Concat(["IncreaseBudget", "DecreaseBudget", "PauseAd", "ResumeAd", "CreateCampaign", "CreateTest", "Retargeting", "Rebalance", "ActivateCanary"])
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static bool IsSupported(string action) => Supported.Contains(action);

    public static bool RequiresJudge(string action, string evidenceJson) => action is "ActivateCanary" or "ActivatePlan"
        or "ChangeOptimizationOutcome" or "ResumeAd" or "ResumeDelivery" or "IncreaseBudget" or "ScaleWinner"
        || action is "PauseAd" or "PauseDelivery" && evidenceJson.Contains("valueProducing", StringComparison.OrdinalIgnoreCase);
}
