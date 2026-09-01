using Shared.Domain;
using System;

namespace Modules.Conversations.Domain
{
    public class Conversation : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid? WhatsAppAccountId { get; set; }
        public Guid? WhatsAppDestinationId { get; set; }
        public Guid? AssignedUserId { get; set; }
        public string Status { get; set; } = "Open"; // Open, Pending, Resolved, Closed
        public string Channel { get; set; } = "WhatsApp"; // WhatsApp, Messenger, FacebookComment
        public DateTime LastMessageTimestamp { get; set; } = DateTime.UtcNow;
        public DateTime? LastUnansweredRecoveryAttemptAt { get; set; }
        public DateTime? WhatsAppDeliveryUnknownAt { get; set; }
        public string? WhatsAppDeliveryUnknownKey { get; set; }
    }
}
