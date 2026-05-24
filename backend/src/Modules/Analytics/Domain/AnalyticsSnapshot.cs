using System;
using Shared.Domain;

namespace Modules.Analytics.Domain
{
    public class AnalyticsSnapshot : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public DateTime SnapshotDate { get; set; }
        public string MetricType { get; set; } // e.g. "Acquisition", "Conversion", "ResponseTime", "AI_Accuracy"
        public decimal MetricValue { get; set; }
        public string MetadataJson { get; set; }
    }
}
