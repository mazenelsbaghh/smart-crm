using Shared.Domain;
using System;

namespace Modules.Workflows.Domain
{
    public class AutomationWorkflow : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string TriggerType { get; set; } = string.Empty; // e.g. CustomerTagAdded, MessageReceived
        public string FiltersJson { get; set; } = "{}";
        public string ActionsJson { get; set; } = "[]";
        public bool IsActive { get; set; } = true;
        public int Version { get; set; } = 1;
    }
}
