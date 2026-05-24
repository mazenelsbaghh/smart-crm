using Shared.Domain;
using System;

namespace Modules.Conversations.Domain
{
    public class NotificationAlert : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid UserId { get; set; }
        public string Message { get; set; } = string.Empty;
        public string Type { get; set; } = "VIP"; // VIP, SLA_Breach, Complaint
        public bool IsRead { get; set; } = false;
    }
}
