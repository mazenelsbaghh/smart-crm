using Shared.Domain;
using System;

namespace Modules.Integrations.Domain
{
    public class ProjectIntegration : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string ProviderName { get; set; } = string.Empty; // e.g. CustomERP, Shopify
        public string ConfigJson { get; set; } = "{}";
        public bool IsActive { get; set; } = true;
        public int SyncIntervalMinutes { get; set; } = 60;
        public DateTime? LastSyncAt { get; set; }
    }
}
