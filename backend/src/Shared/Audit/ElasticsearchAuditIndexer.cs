using Elastic.Clients.Elasticsearch;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Queue;

namespace Shared.Audit;

public sealed record AdvertisingAuditDocument(
    Guid Id, Guid ProjectId, string Category, string Action, string EntityType, string EntityId,
    string ActorType, Guid? ActorUserId, string SafeEvidenceJson, string CorrelationId, DateTime OccurredAtUtc)
{
    public static AdvertisingAuditDocument From(AdvertisingAuditRecord record) => new(
        record.Id, record.ProjectId, AdvertisingLogSanitizer.Redact(record.Category),
        AdvertisingLogSanitizer.Redact(record.Action), AdvertisingLogSanitizer.Redact(record.EntityType),
        AdvertisingLogSanitizer.Redact(record.EntityId), AdvertisingLogSanitizer.Redact(record.ActorType),
        record.ActorUserId, AdvertisingLogSanitizer.Redact(record.SafeEvidenceJson),
        AdvertisingLogSanitizer.Redact(record.CorrelationId), record.OccurredAtUtc);
}

public sealed class ElasticsearchAuditIndexer(
    AppDbContext db,
    ElasticsearchClient elasticsearch,
    ILogger<ElasticsearchAuditIndexer> logger) : IIntegrationEventHandler<AdvertisingAuditRecorded>
{
    public Task HandleAsync(AdvertisingAuditRecorded message) => IndexOneAsync(message.ProjectId, message.AuditRecordId);

    public async Task RunPendingAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var ids = await db.AdvertisingAuditRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.IndexState == "Pending" || x.IndexState == "RetryScheduled")
            .Where(x => x.NextIndexAttemptAtUtc == null || x.NextIndexAttemptAtUtc <= now)
            .OrderBy(x => x.OccurredAtUtc).Select(x => new { x.ProjectId, x.Id }).Take(100)
            .ToListAsync(cancellationToken);
        foreach (var item in ids) await IndexOneAsync(item.ProjectId, item.Id, cancellationToken);
    }

    private async Task IndexOneAsync(Guid projectId, Guid auditId, CancellationToken cancellationToken = default)
    {
        var record = await db.AdvertisingAuditRecords.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == auditId, cancellationToken);
        if (record is null || record.IndexState is "Indexed" or "DeadLetter") return;
        try
        {
            var response = await elasticsearch.IndexAsync(AdvertisingAuditDocument.From(record),
                descriptor => descriptor.Index("smart_whatsapp_advertising_audit").Id(record.Id), cancellationToken);
            if (!response.IsValidResponse) throw new InvalidOperationException("ELASTICSEARCH_REJECTED_AUDIT");
            record.IndexState = "Indexed";
            record.IndexedAtUtc = DateTime.UtcNow;
            record.LastIndexErrorCode = null;
        }
        catch (Exception ex)
        {
            record.IndexAttemptCount++;
            record.LastIndexErrorCode = ex.GetType().Name;
            record.IndexState = AdvertisingAuditIndexPolicy.ShouldDeadLetter(record.IndexAttemptCount) ? "DeadLetter" : "RetryScheduled";
            record.NextIndexAttemptAtUtc = record.IndexState == "DeadLetter"
                ? null
                : DateTime.UtcNow.Add(AdvertisingAuditIndexPolicy.RetryDelay(record.IndexAttemptCount));
            logger.LogWarning("Advertising audit indexing failed for {AuditId}: {FailureCode}", auditId, record.LastIndexErrorCode);
        }
        await db.SaveChangesAsync(cancellationToken);
    }
}
