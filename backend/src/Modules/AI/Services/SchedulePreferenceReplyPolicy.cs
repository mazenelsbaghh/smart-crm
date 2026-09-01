using System.Text.RegularExpressions;

namespace Modules.AI.Services;

public static partial class SchedulePreferenceReplyPolicy
{
    private static readonly string[] ScheduleWords =
        ["موعد", "مواعيد", "ميعاد", "معاد", "الموعد", "الميعاد", "المعاد", "مواعيدكم"];
    private static readonly string[] RejectionPhrases =
    [
        "مش مناسب", "مش مناسبه", "غير مناسب", "غير مناسبه", "مش هينفع",
        "مينفعش", "ما ينفعش", "لا يناسب", "ميناسبنيش", "مش نافع", "مش ظابط"
    ];
    private static readonly string[] AlternativeCues =
    [
        "ينفع", "يناسبني", "مناسب ليا", "مناسبه ليا", "افضل", "عايز",
        "عايزه", "ممكن بدل", "البديل", "اقدر", "متاح", "فاضي"
    ];
    private static readonly string[] ScheduleTokens =
    [
        "السبت", "الاحد", "الاثنين", "الثلاثاء", "الاربعاء", "الخميس", "الجمعه",
        "الصبح", "الظهر", "العصر", "المساء", "بالليل", "بكره", "غدا", "الساعه"
    ];

    public static void Apply(
        string customerMessage,
        MarketingAnalysisResult analysis,
        string channel,
        string agentName)
    {
        if (!SupportsPrivateReply(channel) || !NeedsSchedulePreference(customerMessage)) return;
        analysis.ReplyStyle = "Support";
        analysis.Label = "موعد بديل";
        analysis.ReplyContent =
            $"أكيد يا فندم، قولي إيه المواعيد المناسبة مع حضرتك، الأيام والأوقات، علشان أسجل طلبك ونبلغك أول ما يتوفر موعد مناسب.\n\n- {agentName} ✨";
        analysis.SuggestedGroupBookingId = null;
        analysis.SuggestedGroupBookingPeople = [];
        if (analysis.SuggestedFollowUp is not null) analysis.SuggestedFollowUp.Needed = false;
    }

    private static bool NeedsSchedulePreference(string message)
    {
        var normalized = Normalize(message);
        var rejectsSchedule = ScheduleWords.Any(normalized.Contains)
            && RejectionPhrases.Any(normalized.Contains);
        return rejectsSchedule && !HasExplicitAlternative(normalized);
    }

    private static bool HasExplicitAlternative(string normalizedMessage) =>
        AlternativeCues.Any(normalizedMessage.Contains)
        && (ScheduleTokens.Any(normalizedMessage.Contains) || TimePattern().IsMatch(normalizedMessage));

    private static bool SupportsPrivateReply(string channel) =>
        channel.Equals("WhatsApp", StringComparison.OrdinalIgnoreCase)
        || channel.Equals("Messenger", StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string text) => text.Trim().ToLowerInvariant()
        .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ى', 'ي')
        .Replace('ة', 'ه').Replace("ـ", string.Empty, StringComparison.Ordinal);

    [GeneratedRegex(@"(?:^|\s)[0-9٠-٩]{1,2}(?::[0-9٠-٩]{1,2})?(?:\s|$)")]
    private static partial Regex TimePattern();
}
