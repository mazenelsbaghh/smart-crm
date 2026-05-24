using Shared.Domain;
using System;

namespace Modules.Customers.Domain
{
    public class CustomerMemory : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public string FactsJson { get; set; } = "[]";
        public string TriggersJson { get; set; } = "[]";
        public string ObjectionsJson { get; set; } = "[]";
        public string LongTermSummary { get; set; } = string.Empty;
        public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
