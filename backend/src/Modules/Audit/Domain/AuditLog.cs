using System;
using Shared.Domain;

namespace Modules.Audit.Domain
{
    public class AuditLog : Entity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid? UserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string EntityType { get; set; } = string.Empty;
        public string EntityId { get; set; } = string.Empty;
        public string? OriginalState { get; set; }
        public string? NewState { get; set; }
        public string? IPAddress { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }
}
