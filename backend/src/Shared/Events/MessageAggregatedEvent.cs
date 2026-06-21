using Shared.Events;
using System;

namespace Shared.Events
{
    public class MessageAggregatedEvent : IntegrationEvent
    {
        public Guid ProjectId { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
        public string Channel { get; set; } = "WhatsApp"; // WhatsApp, Messenger, FacebookComment
        public string? ChannelMetadata { get; set; } // JSON with channel-specific data
    }
}
