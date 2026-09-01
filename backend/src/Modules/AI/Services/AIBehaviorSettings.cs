using System.Collections.Generic;

namespace Modules.AI.Services
{
    public class AIBehaviorSettings
    {
        public AIIdentitySettings Identity { get; set; } = new();
        public AIToneSettings Tone { get; set; } = new();
        public CTASettings Cta { get; set; } = new();
        public FollowUpPolicySettings FollowUps { get; set; } = new();
        public ReactionPolicySettings Reactions { get; set; } = new();
        public FallbackMessageSettings Fallbacks { get; set; } = new();
        public Dictionary<string, ChannelAIBehaviorSettings> Channels { get; set; } = new();
        public string? AdvancedInstructions { get; set; }
    }

    public class ChannelAIBehaviorSettings
    {
        public AIIdentitySettings? Identity { get; set; }
        public AIToneSettings? Tone { get; set; }
        public ReactionPolicySettings? Reactions { get; set; }
        public FallbackMessageSettings? Fallbacks { get; set; }
        public string? AdditionalInstructions { get; set; }
    }

    public class AIIdentitySettings
    {
        public string[] AgentNames { get; set; } = System.Array.Empty<string>();
        public string NameSelectionMode { get; set; } = "First";
        public bool SignatureEnabled { get; set; }
        public string SignatureTemplate { get; set; } = "- {agentName}";
        public string ComplaintSignatureTemplate { get; set; } = "- {agentName}";
    }

    public class AIToneSettings
    {
        public string TonePreset { get; set; } = "egyptian-polite";
        public string? CustomTone { get; set; }
        public string TargetAudience { get; set; } = string.Empty;
        public string[] AllowedPhrases { get; set; } = System.Array.Empty<string>();
        public string[] ProhibitedPhrases { get; set; } = System.Array.Empty<string>();
        public string? BusinessInstructions { get; set; }
    }

    public class CTASettings
    {
        public bool Enabled { get; set; }
        public string? Instructions { get; set; }
        public string[] Topics { get; set; } = System.Array.Empty<string>();
    }

    public class FollowUpPolicySettings
    {
        public bool NurturingEnabled { get; set; } = true;
        public bool AppointmentRemindersEnabled { get; set; } = true;
    }

    public class ReactionPolicySettings
    {
        public bool Enabled { get; set; } = true;
        public string[] AllowedReactions { get; set; } = new[] { "👍", "❤️", "💖", "😢", "😂", "😮" };
        public bool UseAiSuggestedReaction { get; set; } = true;
        public string? Rules { get; set; }
    }

    public class FallbackMessageSettings
    {
        public string AiError { get; set; } = "أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.";
        public string InvalidAiOutput { get; set; } = "أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.";
        public string GenericCustomerService { get; set; } = "أهلاً بك! سنقوم بالرد عليك في أقرب وقت ممكن.";
        public string FacebookPublicComment { get; set; } = "تم إرسال التفاصيل في رسالة خاصة.";
        public string WhatsAppTransitionSuccess { get; set; } = "تم إرسال رسالة على واتساب ويمكننا استكمال المحادثة هناك.";
        public string WhatsAppTransitionFailure { get; set; } = "حاولنا نبعتلك على الواتساب بس غالباً الرقم غلط أو مش عليه واتساب. يا ريت تبعتلي الرقم الصح هنا عشان نتواصل هناك.";
        public string WhatsAppTransitionMessage { get; set; } = "أهلاً يا {customerName}، معاك {agentName} من {projectName}. نكمل مع حضرتك هنا على واتساب عشان نساعدك بشكل أسرع.";
        public string FollowUpDefault { get; set; } = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟";
        public string GroupReminderOnline { get; set; } = "أهلاً يا {customerName}، هذا هو رابط الجروب الذي سيرسل عليه رابط الحصة: {groupInviteLink}";
        public string GroupReminderOffline { get; set; } = "أهلاً يا {customerName}، هذا هو رابط الجروب: {groupInviteLink}. نحن بانتظاركم!";
    }
}
