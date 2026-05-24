using Shared.Domain;
using System;

namespace Modules.Workflows.Domain
{
    public class WorkflowExecutionLog : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid AutomationWorkflowId { get; set; }
        public Guid CustomerId { get; set; }
        public string TriggerType { get; set; } = string.Empty;
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string ActionsExecutedJson { get; set; } = "[]";
        public DateTime ExecutedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual AutomationWorkflow? AutomationWorkflow { get; set; }
    }
}
