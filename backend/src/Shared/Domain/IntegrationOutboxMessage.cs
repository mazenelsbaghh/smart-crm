namespace Shared.Domain;

public sealed class IntegrationOutboxMessage : AuditableEntity
{
    public Guid EventId { get; set; }
    public string EventType { get; set; } = string.Empty;
    public int SchemaVersion { get; set; } = 1;
    public string PayloadJson { get; set; } = "{}";
    public DateTime OccurredAtUtc { get; set; }
    public DateTime? PublishedAtUtc { get; set; }
    public int AttemptCount { get; set; }
    public DateTime? NextAttemptAtUtc { get; set; }
    public string? LastErrorCode { get; set; }
}
