using Shared.Domain;
using System;

namespace Modules.Conversations.Domain
{
    public class Conversation : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public Guid? AssignedUserId { get; set; }
        public string Status { get; set; } = "Open"; // Open, Pending, Resolved, Closed
        public DateTime LastMessageTimestamp { get; set; } = DateTime.UtcNow;
    }
}
