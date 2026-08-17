namespace Shared.Domain;

public sealed class IntegrationInboxReceipt : AuditableEntity
{
    public Guid EventId { get; set; }
    public string Consumer { get; set; } = string.Empty;
    public DateTime ProcessedAtUtc { get; set; }
}
