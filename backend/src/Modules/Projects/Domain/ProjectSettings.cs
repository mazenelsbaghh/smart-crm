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
        public string GeminiModel { get; set; } = "gemini-3.5-flash";
        public string AiTonePreference { get; set; } = "العامية المصرية الروشة والصايعة";
        public string AiTargetAudience { get; set; } = "طلاب كورس كول سنتر يبحثون عن عمل";
        public int ReplyDelay { get; set; } = 3;
        public int MaxDailyMessages { get; set; } = 500;
        public bool IsGroupAppointmentsEnabled { get; set; } = false;
        public bool MessengerAiAutoReplyEnabled { get; set; } = false;
        public int MessengerReplyDelay { get; set; } = 5;
        public bool CommentsAiAutoReplyEnabled { get; set; } = false;
        public int CommentsReplyDelay { get; set; } = 10;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
