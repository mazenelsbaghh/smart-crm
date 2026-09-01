using Shared.Domain;

namespace Modules.Conversations.Domain;

/// <summary>
/// Durable debounce state for one conversation. The row remains after dispatch so
/// duplicate webhooks can observe the exact event that was already handed off.
/// </summary>
public sealed class ConversationReplyWindow : AuditableEntity, ITenantEntity
{
    public Guid ProjectId { get; set; }
    public Guid ConversationId { get; set; }
    public Guid? WhatsAppAccountId { get; set; }
    public string Channel { get; set; } = "WhatsApp";
    public Guid LatestIncomingMessageId { get; set; }
    public long LatestIncomingVersion { get; set; } = 1;
    public DateTime LatestIncomingAtUtc { get; set; }
    public string Sender { get; set; } = string.Empty;
    public string AggregatedContent { get; set; } = string.Empty;
    public string? ChannelMetadata { get; set; }
    public DateTime DueAtUtc { get; set; }
    public DateTimeOffset? RequiredWhatsAppConnectedAt { get; set; }
    public Guid EventId { get; set; }
    public string? WhatsAppDeliveryIdempotencyKey { get; set; }
    public Guid? DispatchedEventId { get; set; }
    public DateTime? DispatchedAtUtc { get; set; }
}
