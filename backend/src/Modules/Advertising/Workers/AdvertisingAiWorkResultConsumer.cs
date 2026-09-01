using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class AdvertisingAiWorkResultConsumer(AppDbContext db, AdvertisingAuditService audit) : IIntegrationEventHandler<AdvertisingAiWorkCompleted>
{
    public async Task HandleAsync(AdvertisingAiWorkCompleted message)
    {
        var work = await db.AdvertisingAiWorkItems.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == message.ProjectId && x.Id == message.RequestId);
        if (work is null) return;
        var snapshot = new AdvertisingAiWorkItemSnapshot(work.Id, work.ProjectId, work.OwnerId, work.OwnerVersion, work.InputHash, work.State, work.DeadlineUtc);
        var completion = new AdvertisingAiWorkCompletion(message.RequestId, message.ProjectId, message.OwnerId, message.OwnerVersion, message.InputHash, message.StructuredResultJson);
        var decision = AdvertisingAiWorkResultGuard.Evaluate(snapshot, completion, DateTime.UtcNow);
        if (decision != AiWorkCompletionDecision.Accept)
        {
            if (decision == AiWorkCompletionDecision.RejectExpired) work.State = AiWorkState.Expired;
            if (decision == AiWorkCompletionDecision.RejectExpired)
            {
                work.FailureCode = decision.ToString();
                await db.SaveChangesAsync();
            }
            return;
        }
        if (string.IsNullOrWhiteSpace(message.FailureCode)
            && !AdvertisingAiResultSchema.IsValid(work.Purpose, message.StructuredResultJson))
        {
            work.State = AiWorkState.Failed;
            work.FailureCode = "ADS_AI_RESULT_SCHEMA_INVALID";
            work.CompletedAtUtc = DateTime.UtcNow;
            audit.RecordAiSchemaResult(work.ProjectId, work.Id, work.Purpose, work.InputHash,
                "Rejected", message.ModelVersion, work.FailureCode);
            await db.SaveChangesAsync();
            return;
        }
        work.State = string.IsNullOrWhiteSpace(message.FailureCode) ? AiWorkState.Completed : AiWorkState.Failed;
        work.ResultJson = message.StructuredResultJson;
        work.ModelVersion = message.ModelVersion;
        work.FailureCode = string.IsNullOrWhiteSpace(message.FailureCode) ? null : message.FailureCode;
        work.CompletedAtUtc = DateTime.UtcNow;
        audit.RecordAiSchemaResult(work.ProjectId, work.Id, work.Purpose, work.InputHash,
            work.State.ToString(), work.ModelVersion, work.FailureCode);
        await db.SaveChangesAsync();
    }
}

public static class AdvertisingAiResultSchema
{
    private static readonly HashSet<string> ReviewPurposes = new(StringComparer.Ordinal)
        { "Strategist", "Auditor", "Judge" };
    private static readonly HashSet<string> Verdicts = new(StringComparer.OrdinalIgnoreCase)
        { "APPROVE", "REJECT", "WAIT", "ESCALATE" };

    public static bool IsValid(string purpose, string json)
    {
        if (!ReviewPurposes.Contains(purpose)) return true;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return false;
            var properties = document.RootElement.EnumerateObject().ToArray();
            if (properties.Any(item => item.Name is not ("verdict" or "reasons"))) return false;
            if (!document.RootElement.TryGetProperty("verdict", out var verdict) || !Verdicts.Contains(verdict.GetString() ?? string.Empty)) return false;
            return document.RootElement.TryGetProperty("reasons", out var reasons)
                && reasons.ValueKind == System.Text.Json.JsonValueKind.Array
                && reasons.EnumerateArray().All(item => item.ValueKind == System.Text.Json.JsonValueKind.String);
        }
        catch (System.Text.Json.JsonException) { return false; }
    }
}
