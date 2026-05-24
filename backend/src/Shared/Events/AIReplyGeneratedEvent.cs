using Shared.Events;
using System;

namespace Shared.Events
{
    public class AIReplyGeneratedEvent : IntegrationEvent
    {
        public Guid ProjectId { get; set; }
        public string Sender { get; set; }
        public string Content { get; set; }
    }
}
