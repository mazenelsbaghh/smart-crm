using Shared.Domain;
using System;

namespace Modules.CRM.Domain
{
    public class CRMUpdateProposal : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CustomerId { get; set; }
        public string FieldName { get; set; } = string.Empty;
        public string SuggestedValue { get; set; } = string.Empty;
        public double ConfidenceScore { get; set; }
        public string Status { get; set; } = "PendingApproval"; // Applied, PendingApproval, Rejected
    }
}
