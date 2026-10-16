using Shared.Domain;

namespace Modules.GroupAppointments.Domain;

public sealed class GroupStaffNotification : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid GroupAppointmentId { get; set; }
    public Guid BookingId { get; set; }
    public Guid? WhatsAppAccountId { get; set; }
    public string RecipientPhone { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? ProviderMessageId { get; set; }
    public string? LastError { get; set; }
}
