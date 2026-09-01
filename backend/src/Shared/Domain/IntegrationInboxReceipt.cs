namespace Shared.Domain;

public sealed class IntegrationInboxReceipt : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAtUtc { get; set; }
    public string State { get; set; } = "Received";
    public string? FailureCode { get; set; }
    public string SourceAggregateType { get; set; } = string.Empty;
    public Guid SourceAggregateId { get; set; }
    public long SourceVersion { get; set; }
}

public sealed class IntegrationProjectionWatermark : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public string SourceAggregateType { get; set; } = string.Empty;
    public Guid SourceAggregateId { get; set; }
    public long CurrentVersion { get; set; }
    public bool IsTombstoned { get; set; }
    public long? MissingFromVersion { get; set; }
    public long? MissingToVersion { get; set; }
    public Guid LastEventId { get; set; }
}
