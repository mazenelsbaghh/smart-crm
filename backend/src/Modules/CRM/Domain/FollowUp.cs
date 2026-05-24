using Shared.Domain;
using System;
using System.ComponentModel.DataAnnotations;

namespace Modules.CRM.Domain
{
    public class FollowUp : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public DateTime DueDate { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Done, Missed
        public string Notes { get; set; }
    }
}
