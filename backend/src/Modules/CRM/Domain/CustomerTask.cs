using Shared.Domain;
using System;

namespace Modules.CRM.Domain
{
    public class CustomerTask : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public string Title { get; set; }
        public bool IsCompleted { get; set; } = false;
        public DateTime? DueDate { get; set; }
    }
}
