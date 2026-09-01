using Shared.Domain;

namespace Modules.WhatsApp.Domain;

/// <summary>
/// A project-scoped Baileys account. The account whose Id equals ProjectId is
/// the legacy/default slot so existing credentials remain usable after upgrade.
/// </summary>
public sealed class WhatsAppAccount : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public string Name { get; set; } = "واتساب الرئيسي";
    public bool IsDefault { get; set; }
}
