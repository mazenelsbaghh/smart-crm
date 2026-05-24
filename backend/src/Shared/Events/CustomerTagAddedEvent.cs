using Shared.Events;
using System;

namespace Shared.Events
{
    public class CustomerTagAddedEvent : IntegrationEvent
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public string Tag { get; set; } = string.Empty;
    }
}
