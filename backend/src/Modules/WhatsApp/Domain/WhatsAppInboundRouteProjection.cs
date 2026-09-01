using Shared.Domain;

namespace Modules.WhatsApp.Domain;

public sealed class WhatsAppInboundRouteProjection : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid DestinationId { get; set; }
    public long DestinationVersion { get; set; }
    public string Provider { get; set; } = "MetaWhatsApp";
    public string WabaExternalId { get; set; } = string.Empty;
    public string PhoneNumberExternalId { get; set; } = string.Empty;
    public string IntegrationMode { get; set; } = string.Empty;
    public Guid SourceEventId { get; set; }
    public long SourceAggregateVersion { get; set; }
    public string State { get; set; } = "Active";
    public DateTime UpdatedAtUtc { get; set; }
}
