using System;
using Shared.Domain;

namespace Modules.CRM.Domain
{
    public enum DealStatus
    {
        Open,
        Won,
        Lost
    }

    public class Deal : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public string Title { get; set; }
        public decimal Amount { get; set; }
        public Guid PipelineStageId { get; set; }
        public DealStatus Status { get; set; }
        public DateTime? ClosedAt { get; set; }
    }
}
