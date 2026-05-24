using Shared.Domain;
using System;

namespace Modules.Conversations.Domain
{
    public class Message : Entity
    {
        public Guid ConversationId { get; set; }
        public string ExternalMessageId { get; set; }
        public string Direction { get; set; } // Incoming, Outgoing
        public string Content { get; set; }
        public string MessageType { get; set; } // Text, Image, Voice, Document
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
