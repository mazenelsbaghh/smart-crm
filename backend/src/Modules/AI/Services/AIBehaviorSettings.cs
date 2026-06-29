using System.Collections.Generic;

namespace Modules.AI.Services
{
    public class AIBehaviorSettings
    {
        public AIIdentitySettings Identity { get; set; } = new();
        public AIToneSettings Tone { get; set; } = new();
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
        public string NameSelectionMode { get; set; } = "HourlyRotation";
        public bool SignatureEnabled { get; set; } = true;
        public string SignatureTemplate { get; set; } = "- {agentName} ✨";
        public string ComplaintSignatureTemplate { get; set; } = "- {agentName}";
    }

    public class AIToneSettings
    {
        public string TonePreset { get; set; } = "egyptian-slang-sales";
        public string? CustomTone { get; set; }
        public string TargetAudience { get; set; } = "طلاب كورس كول سنتر يبحثون عن عمل";
        public string[] AllowedPhrases { get; set; } = System.Array.Empty<string>();
        public string[] ProhibitedPhrases { get; set; } = System.Array.Empty<string>();
        public string? BusinessInstructions { get; set; }
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
        public string FacebookPublicComment { get; set; } = "تم الرد في الخاص يا فندم! ❤️";
        public string WhatsAppTransitionSuccess { get; set; } = "أنا بعتلك رسالة على الواتساب، خلينا نتواصل هناك. ✨";
        public string WhatsAppTransitionFailure { get; set; } = "حاولنا نبعتلك على الواتساب بس غالباً الرقم غلط أو مش عليه واتساب. يا ريت تبعتلي الرقم الصح هنا عشان نتواصل هناك.";
        public string WhatsAppTransitionMessage { get; set; } = "أهلاً يا {customerName}، منورنا يا فندم! 😊 معاك {agentName}.. زي ما اتفقنا على ماسنجر، هنكمل كلامنا هنا على واتساب عشان نتابع مع بعض أسرع ونبعتلك كل التفاصيل بسهولة. وحابب أفكرك إن أول جلسة ليك معانا مجانية تماماً! لو تحب تحجزها دلوقتي، قولي الميعاد المناسب ليك وهسجلك فيه فوراً.";
        public string FollowUpDefault { get; set; } = "مرحباً يا فندم، حابين نطمن على تفاصيل الحجز ونعرف لو في أي استفسار آخر؟";
        public string GroupReminderOnline { get; set; } = "أهلاً يا {customerName}، هذا هو رابط الجروب الذي سيرسل عليه رابط الحصة: {groupInviteLink}";
        public string GroupReminderOffline { get; set; } = "أهلاً يا {customerName}، هذا هو رابط الجروب: {groupInviteLink}. نحن بانتظاركم!";
    }
}
