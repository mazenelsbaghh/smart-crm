using Shared.Domain;
using System;

namespace Modules.Approvals.Domain
{
    public class ApprovalRequest : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string ActionType { get; set; } = string.Empty; // e.g. CRMUpdate, SendDiscount
        public string PayloadJson { get; set; } = "{}";
        public string RiskLevel { get; set; } = "Low"; // Low, Medium, High, Critical
        public string Status { get; set; } = "Pending"; // Pending, Approved, Rejected
        public string RequestedBy { get; set; } = "AI_Worker";
        public string? Notes { get; set; }
        public DateTime? ExecutedAt { get; set; }
        public string? ApprovedBy { get; set; }
    }
}
