using Shared.Events;
using System;

namespace Shared.Events
{
    public class ConversationClosedEvent : IntegrationEvent
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid ConversationId { get; set; }
    }
}
