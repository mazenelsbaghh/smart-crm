using Shared.Domain;

namespace Modules.WhatsApp.Domain;

/// <summary>Maps one normalized project-wide WhatsApp phone to its canonical CRM customer.</summary>
public sealed class WhatsAppPhoneCustomerIdentity : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid CustomerId { get; set; }
    public string NormalizedPhone { get; set; } = string.Empty;
}
