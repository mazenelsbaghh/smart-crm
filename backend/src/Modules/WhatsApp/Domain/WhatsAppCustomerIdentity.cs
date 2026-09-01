using Shared.Domain;

namespace Modules.WhatsApp.Domain;

/// <summary>Maps an account-scoped WhatsApp JID/LID to the shared project customer.</summary>
public sealed class WhatsAppCustomerIdentity : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid WhatsAppAccountId { get; set; }
    public Guid CustomerId { get; set; }
    public string ExternalId { get; set; } = string.Empty;
    public string Kind { get; set; } = "Lid";
}
