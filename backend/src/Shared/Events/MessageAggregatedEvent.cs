using Shared.Events;
using Shared.Queue;
using System;

namespace Shared.Events
{
    [IntegrationEventContract("MessageAggregated.v1", 1)]
    public class MessageAggregatedEvent : IntegrationEvent
    {
        public Guid ProjectId { get; set; }
        public Guid? ConversationId { get; set; }
        public Guid? WhatsAppAccountId { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
        public string Channel { get; set; } = "WhatsApp"; // WhatsApp, Messenger, FacebookComment
        public string? ChannelMetadata { get; set; } // JSON with channel-specific data
        public DateTime? SourceMessageTimestampUtc { get; set; }
        public DateTimeOffset? RequiredWhatsAppConnectedAt { get; set; }
        public string? WhatsAppDeliveryIdempotencyKey { get; set; }
    }
}
