using Microsoft.EntityFrameworkCore;
using Shared.Domain;
using Shared.Infrastructure;

namespace Shared.Queue;

public enum ProjectionVersionDecision
{
    Duplicate,
    Stale,
    Apply,
    Gap,
    ApplyTombstone
}

public static class ProjectionVersionGuard
{
    public static ProjectionVersionDecision Decide(long currentVersion, long incomingVersion, bool isTombstone = false)
    {
        if (incomingVersion == currentVersion) return ProjectionVersionDecision.Duplicate;
        if (incomingVersion < currentVersion) return ProjectionVersionDecision.Stale;
        if (incomingVersion > currentVersion + 1) return ProjectionVersionDecision.Gap;
        return isTombstone ? ProjectionVersionDecision.ApplyTombstone : ProjectionVersionDecision.Apply;
    }
}

public sealed class IntegrationProjectionValidationException(string failureCode) : Exception(failureCode)
{
    public string FailureCode { get; } = failureCode;
}

public abstract class IntegrationProjectionConsumer<TEvent>(AppDbContext db) where TEvent : AdvertisingIntegrationEvent
{
    protected abstract string ConsumerName { get; }
    protected AppDbContext Db => db;

    protected async Task ConsumeAsync(TEvent message, Func<CancellationToken, Task> apply, CancellationToken cancellationToken = default)
    {
        ValidateEnvelope(message);
        if (await db.IntegrationInboxReceipts.IgnoreQueryFilters()
                .AnyAsync(x => x.EventId == message.Id && x.Consumer == ConsumerName, cancellationToken))
            return;

        var aggregateType = string.IsNullOrWhiteSpace(message.SourceAggregateType)
            ? typeof(TEvent).Name
            : message.SourceAggregateType;
        var aggregateId = message.SourceAggregateId == Guid.Empty ? message.Id : message.SourceAggregateId;
        var watermark = await db.IntegrationProjectionWatermarks.IgnoreQueryFilters().SingleOrDefaultAsync(
            x => x.ProjectId == message.ProjectId && x.Consumer == ConsumerName &&
                 x.SourceAggregateType == aggregateType && x.SourceAggregateId == aggregateId,
            cancellationToken);
        var currentVersion = watermark?.CurrentVersion ?? 0;
        var decision = ProjectionVersionGuard.Decide(currentVersion, message.SourceVersion, message.IsTombstone);
        var receipt = CreateReceipt(message, aggregateType, aggregateId);

        if (decision is ProjectionVersionDecision.Duplicate or ProjectionVersionDecision.Stale)
        {
            receipt.State = decision.ToString();
            receipt.ProcessedAtUtc = DateTime.UtcNow;
            db.IntegrationInboxReceipts.Add(receipt);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        if (watermark is null)
        {
            watermark = new IntegrationProjectionWatermark
            {
                ProjectId = message.ProjectId,
                Consumer = ConsumerName,
                SourceAggregateType = aggregateType,
                SourceAggregateId = aggregateId
            };
            db.IntegrationProjectionWatermarks.Add(watermark);
        }

        if (decision == ProjectionVersionDecision.Gap)
        {
            receipt.State = "Gap";
            receipt.FailureCode = "PROJECTION_VERSION_GAP";
            receipt.ProcessedAtUtc = DateTime.UtcNow;
            watermark.MissingFromVersion = currentVersion + 1;
            watermark.MissingToVersion = message.SourceVersion - 1;
            db.IntegrationInboxReceipts.Add(receipt);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        try
        {
            await apply(cancellationToken);
            watermark.CurrentVersion = message.SourceVersion;
            watermark.IsTombstoned = message.IsTombstone;
            watermark.MissingFromVersion = null;
            watermark.MissingToVersion = null;
            watermark.LastEventId = message.Id;
            receipt.State = "Processed";
            receipt.ProcessedAtUtc = DateTime.UtcNow;
            db.IntegrationInboxReceipts.Add(receipt);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (IntegrationProjectionValidationException ex)
        {
            db.ChangeTracker.Clear();
            receipt.State = "Poisoned";
            receipt.FailureCode = ex.FailureCode;
            receipt.ProcessedAtUtc = DateTime.UtcNow;
            db.IntegrationInboxReceipts.Add(receipt);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private IntegrationInboxReceipt CreateReceipt(TEvent message, string aggregateType, Guid aggregateId) => new()
    {
        ProjectId = message.ProjectId,
        EventId = message.Id,
        Consumer = ConsumerName,
        ReceivedAtUtc = DateTime.UtcNow,
        SourceAggregateType = aggregateType,
        SourceAggregateId = aggregateId,
        SourceVersion = message.SourceVersion
    };

    private static void ValidateEnvelope(TEvent message)
    {
        if (message.ProjectId == Guid.Empty) throw new IntegrationProjectionValidationException("PROJECT_ID_REQUIRED");
        if (message.SourceVersion <= 0) throw new IntegrationProjectionValidationException("SOURCE_VERSION_INVALID");
    }
}
