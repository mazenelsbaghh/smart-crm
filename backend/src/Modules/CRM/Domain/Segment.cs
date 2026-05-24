using System;
using Shared.Domain;

namespace Modules.CRM.Domain
{
    public class Segment : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; }
        public string FilterCriteriaJson { get; set; }
    }
}
