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
        public string GeminiAgentPlatformApiKey { get; set; } = string.Empty;
        public string GeminiModel { get; set; } = "gemini-3.5-flash";
        public string? TemporaryGeminiModel { get; set; }
        public DateTime? TemporaryGeminiModelExpiresAtUtc { get; set; }
        public string? GeminiEnterpriseProjectId { get; set; }
        public string CustomerReplyProvider { get; set; } = CustomerReplyProviders.Gemini;
        public string CustomerReplyOpenAiApiKey { get; set; } = string.Empty;
        public string CustomerReplyXaiApiKey { get; set; } = string.Empty;
        public string CustomerReplyModel { get; set; } = "gpt-5.6";
        public string AiTonePreference { get; set; } = "العامية المصرية المهذبة والمحترمة";
        public string AiTargetAudience { get; set; } = string.Empty;
        public int ReplyDelay { get; set; } = 3;
        public int MaxDailyMessages { get; set; } = 500;
        public bool IsGroupAppointmentsEnabled { get; set; } = false;
        public bool MessengerAiAutoReplyEnabled { get; set; } = false;
        public int MessengerReplyDelay { get; set; } = 5;
        public bool CommentsAiAutoReplyEnabled { get; set; } = false;
        public int CommentsReplyDelay { get; set; } = 10;
        public string? SystemPrompt { get; set; }
        public string? AiBehaviorSettingsJson { get; set; }
        public bool IsWhatsAppGroupAutomationEnabled { get; set; } = false;
        public string GroupAutomationManagerPhone { get; set; } = string.Empty;
        public string ActiveInstructors { get; set; } = string.Empty;
        public bool HumanTransferEnabled { get; set; } = false;
        public string? HumanTransferPhone { get; set; }
        public bool IsTalkTipsTrialGateEnabled { get; set; } = false;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        public long AdvertisingContextVersion { get; set; } = 1;

        public bool HasActiveTemporaryGeminiModel(DateTime utcNow) =>
            !string.IsNullOrWhiteSpace(TemporaryGeminiModel) &&
            TemporaryGeminiModelExpiresAtUtc.HasValue &&
            TemporaryGeminiModelExpiresAtUtc.Value > utcNow;

        public string ResolveGeminiModel(DateTime utcNow) =>
            HasActiveTemporaryGeminiModel(utcNow)
                ? TemporaryGeminiModel!
                : GeminiModel;
    }
}
