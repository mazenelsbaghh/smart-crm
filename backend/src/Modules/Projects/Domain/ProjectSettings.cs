using Shared.Domain;
using System;
using System.ComponentModel.DataAnnotations;

namespace Modules.Projects.Domain
{
    public class ProjectSettings : ITenantEntity
    {
        [Key]
        public Guid ProjectId { get; set; }
        public bool AiAutoReplyEnabled { get; set; } = false;
        public string Timezone { get; set; } = "UTC";
        public string GeminiApiKey { get; set; } = string.Empty;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
