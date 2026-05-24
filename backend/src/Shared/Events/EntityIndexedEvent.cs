using System;

namespace Shared.Events
{
    public class EntityIndexedEvent : IntegrationEvent
    {
        public Guid EntityId { get; set; }
        public string EntityType { get; set; } // e.g. "Message", "Customer", "Conversation"
        public Guid ProjectId { get; set; }
        public string Action { get; set; } // "Upsert" or "Delete"
        public string ContentJson { get; set; }
    }
}
