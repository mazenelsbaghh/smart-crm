using System;
using Shared.Domain;

namespace Modules.CRM.Domain
{
    public class PipelineStage : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; }
        public int Order { get; set; }
    }
}
