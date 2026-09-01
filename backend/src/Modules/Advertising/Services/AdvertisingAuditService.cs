using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Queue;
using System.Text.Json;

namespace Modules.Advertising.Services;

public static class AdvertisingAuditIndexPolicy
{
    public const int MaximumAttempts = 10;
    public static TimeSpan RetryDelay(int attempt) => TimeSpan.FromSeconds(Math.Min(300, Math.Pow(2, Math.Max(1, attempt))));
    public static bool ShouldDeadLetter(int attempt) => attempt >= MaximumAttempts;
}

public static class AdvertisingLogSanitizer
{
    private static readonly Regex Sensitive = new(
        "(?i)[\\\"']?(email|match_data|ctwa_clid|phone|access[_-]?token|api[_-]?key|secret|password)[\\\"']?\\s*[:=]\\s*[\\\"']?[^\\\"'\\s,;}]+[\\\"']?|[a-z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-z0-9.-]+\\.[a-z]{2,}|\\+?\\d{10,15}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Redact(string? value) => string.IsNullOrWhiteSpace(value) ? string.Empty : Sensitive.Replace(value, "[redacted]");
}

public sealed record AdvertisingAuditWrite(
    Guid ProjectId,
    string Category,
    string Action,
    string EntityType,
    string EntityId,
    string ActorType,
    Guid? ActorUserId,
    string SafeEvidenceJson,
    Guid CorrelationId);

public sealed class AdvertisingAuditService(AppDbContext db)
{
    public AdvertisingAuditRecord Append(AdvertisingAuditWrite write)
    {
        var record = new AdvertisingAuditRecord
        {
            ProjectId = write.ProjectId,
            Category = AdvertisingLogSanitizer.Redact(write.Category),
            Action = AdvertisingLogSanitizer.Redact(write.Action),
            EntityType = AdvertisingLogSanitizer.Redact(write.EntityType),
            EntityId = AdvertisingLogSanitizer.Redact(write.EntityId),
            ActorType = AdvertisingLogSanitizer.Redact(write.ActorType),
            ActorUserId = write.ActorUserId,
            SafeEvidenceJson = AdvertisingLogSanitizer.Redact(write.SafeEvidenceJson),
            CorrelationId = write.CorrelationId.ToString("N"),
            OccurredAtUtc = DateTime.UtcNow
        };
        db.AdvertisingAuditRecords.Add(record);
        IntegrationOutbox.Enqueue(db, new AdvertisingAuditRecorded
        {
            ProjectId = write.ProjectId,
            AuditRecordId = record.Id,
            Action = write.Action,
            TargetType = write.EntityType,
            TargetId = Guid.TryParse(write.EntityId, out var targetId) ? targetId : null,
            CorrelationId = write.CorrelationId,
            SourceAggregateType = nameof(AdvertisingAuditRecord),
            SourceAggregateId = record.Id,
            SourceVersion = 1
        });
        return record;
    }

    public static string HashState(string json) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant();

    public async Task RecordPlanningDecisionAsync(Guid projectId, Guid offerId, string action,
        IReadOnlyCollection<string> reasons, object evidence, CancellationToken cancellationToken = default)
    {
        Append(new(projectId, "Planning", action, "AdvertisingOffer", offerId.ToString(),
            "SystemAutopilot", null, JsonSerializer.Serialize(new { reasons, evidence }), Guid.NewGuid()));
        await db.SaveChangesAsync(cancellationToken);
    }

    public AdvertisingAuditRecord RecordAiSchemaResult(Guid projectId, Guid workId, string purpose,
        string inputHash, string outcome, string? modelVersion, string? failureCode) =>
        Append(new(projectId, "AiReview", "AiSchemaResult", nameof(AdvertisingAiWorkItem), workId.ToString(),
            "SystemAutopilot", null, JsonSerializer.Serialize(new
            {
                purpose, inputHash, outcome, modelVersion, failureCode
            }), workId));
}
