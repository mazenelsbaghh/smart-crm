using System.Text.RegularExpressions;
using Shared.Domain;

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
        if (!SupportsPrivateReply(channel)) return;
        if (!NeedsSchedulePreference(customerMessage))
        {
            AskForUnknownAttendanceMode(customerMessage, analysis);
            return;
        }
        analysis.ReplyStyle = "Support";
        analysis.Label = "موعد بديل";
        analysis.ReplyContent =
            $"أكيد يا فندم، اختار الفترة الأنسب لحضرتك:\nمن ١٢ ظهرًا لـ٤ عصرًا\nمن ٤ عصرًا لـ٨ مساءً\nمن ٨ مساءً لـ١٢ منتصف الليل\n\nوكمان تحب الموعد يكون الأسبوع الجاي، الشهر الجاي، خلال ٣ شهور، ولا أي وقت؟\n{AttendancePreferenceText(analysis.AttendanceMode)}\nهنبلغك أول ما يتوفر موعد مناسب.\n\n- {agentName} ✨";
        analysis.SuggestedGroupBookingId = null;
        analysis.SuggestedGroupBookingPeople = [];
        if (analysis.SuggestedFollowUp is not null) analysis.SuggestedFollowUp.Needed = false;
    }

    private static string AttendancePreferenceText(string mode) => AttendanceModes.Normalize(mode) switch
    {
        AttendanceModes.Online => "الحضور المطلوب: أونلاين.",
        AttendanceModes.Offline => "الحضور المطلوب: أوفلاين في السنتر.",
        AttendanceModes.Either => "الحضور المناسب لحضرتك: أونلاين أو أوفلاين في السنتر.",
        _ => "تحب الحضور أونلاين ولا أوفلاين في السنتر؟"
    };

    private static void AskForUnknownAttendanceMode(string message, MarketingAnalysisResult analysis)
    {
        var normalized = Normalize(message);
        if (AttendanceModes.Normalize(analysis.AttendanceMode) != AttendanceModes.Unknown
            || analysis.RequestHuman || analysis.IsFallbackResponse || analysis.SuggestedGroupBookingId is not null
            || !analysis.Intent.Equals("inquiry", StringComparison.OrdinalIgnoreCase)
            || !ScheduleWords.Any(normalized.Contains)
            || !new[] { "ايه", "امتي", "متي", "هل", "؟", "?", "عايز اعرف", "ممكن اعرف" }.Any(normalized.Contains)) return;
        var reply = Normalize(analysis.ReplyContent);
        if (reply.Contains("اونلاين ولا") || reply.Contains("اوفلاين ولا")) return;
        analysis.ReplyContent += $"\n\n{AttendancePreferenceText(AttendanceModes.Unknown)}";
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
