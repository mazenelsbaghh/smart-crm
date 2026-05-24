using System;
using Shared.Domain;

namespace Modules.Campaigns.Domain
{
    public enum CampaignStatus
    {
        Draft,
        Scheduled,
        Running,
        Paused,
        Completed,
        Cancelled
    }

    public class Campaign : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string Name { get; set; }
        public Guid SegmentId { get; set; }
        public string MessageTemplateA { get; set; }
        public string MessageTemplateB { get; set; }
        public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
        public DateTime? ScheduledAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int SentCount { get; set; } = 0;
        public int DeliveredCount { get; set; } = 0;
        public int ReadCount { get; set; } = 0;
        public int ResponseCount { get; set; } = 0;
    }
}
