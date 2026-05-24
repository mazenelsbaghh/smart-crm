using Shared.Domain;
using System;

namespace Modules.Conversations.Domain
{
    public class Customer : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string PhoneNumber { get; set; }
        public string Name { get; set; }
        public string City { get; set; }
        public int LeadScore { get; set; } = 0;
        public string[] Tags { get; set; } = Array.Empty<string>();
        public string Notes { get; set; } = string.Empty;
        public decimal? Budget { get; set; }
        public string[] Interests { get; set; } = Array.Empty<string>();
    }
}
