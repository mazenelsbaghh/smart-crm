namespace Modules.AI.Services;

internal static class MessengerBookingPolicy
{
    public static void Apply(AIBehaviorSettings settings, string channel, MarketingAnalysisResult analysis)
    {
        if (channel != "Messenger" || !settings.MessengerWhatsAppTransitionEnabled
            || string.IsNullOrEmpty(analysis.SuggestedGroupBookingId)) return;

        // Enforce the selected route even if the model suggests booking on Messenger.
        analysis.SuggestedGroupBookingId = null;
        analysis.SuggestedGroupBookingPeople = [];
        analysis.SuggestedFollowUp = null;
        analysis.ReplyContent = "علشان نكمل الحجز على واتساب، ممكن تبعت رقم الواتساب بتاعك؟";
    }
}
