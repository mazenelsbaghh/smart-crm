using System;
using Shared.Domain;

namespace Modules.Campaigns.Domain
{
    public enum RecipientStatus
    {
        Pending,
        Sent,
        Delivered,
        Read,
        Failed,
        Responded
    }

    public class CampaignRecipient : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public Guid CampaignId { get; set; }
        public Guid CustomerId { get; set; }
        public string Variant { get; set; } = "A"; // "A" or "B"
        public RecipientStatus Status { get; set; } = RecipientStatus.Pending;
        public DateTime? SentAt { get; set; }
        public DateTime? DeliveredAt { get; set; }
        public DateTime? ReadAt { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
    }
}
