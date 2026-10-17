using System.Text.RegularExpressions;
using Modules.Conversations.Domain;
using Modules.AI.Services;

namespace Modules.TalkTips.Services;

internal static class TalkTipsFollowUpAcceptancePolicy
{
    public const string Instructions = """
        A short acceptance such as تمام، ماشي، موافق، yes or 👍 replies to the latest delivered invitation, including a quoted follow-up.
        If that invitation offered course details or a free trial, continue with the relevant verified course explanation and one useful next step.
        Do not answer with a generic 'any other questions?' or assume that acceptance chooses an appointment. Only book the exact slot the customer selected.
        If the customer declines or closes the conversation (لا تمام، شكرا، مش مهتم), respect that instead of restarting the pitch.
        """;

    public static bool AcceptsCourseInvitation(string customerMessage, IReadOnlyList<Message> history)
    {
        var normalized = Normalize(customerMessage);
        if (!Regex.IsMatch(normalized, @"^(?:تمام|ماشي|موافق|ايوه|اه|نعم|اوكي|اوك|ok|okay|yes|👍)(?:\s+(?:يا فندم|تفضل|طبعا))?[.!،\s]*$")) return false;
        var previous = history.LastOrDefault(message => message.Direction == "Outgoing" && message.MessageType != "Reaction");
        return previous is not null && !Regex.IsMatch(Normalize(previous.Content), @"الساعه\s*\p{Nd}|\p{Nd}[:：]\p{Nd}")
            && Regex.IsMatch(Normalize(previous.Content),
                @"(?:حابب|تحب|مهتم|تحضر|عايز|عاوز).*(?:كورس|محاضره|مجانيه|تفاصيل)", RegexOptions.Singleline);
    }

    public static void ContinueWithDetails(MarketingAnalysisResult analysis, string invitation)
    {
        if (analysis.RequestHuman || analysis.IsFallbackResponse) return;
        var intensive = invitation.Contains("المكثف") && !invitation.Contains("العادي");
        var studyFormat = intensive
            ? "الكورس المكثف مدته ٧٥ يوم، بسيشنين في الأسبوع وممارسة يومية ومتابعة على الويبسايت، ومكالمة أسبوعية للتدريب على الكلام."
            : "الكورس العادي مدته ٤ شهور، يومين محاضرات في الأسبوع و٥ أيام تاسكات ومتابعة على الويبسايت. بنشتغل على الإنجليزي والمحادثة ومحاكاة مواقف الكول سنتر والانترفيو.";
        var nextStep = Shared.Domain.AttendanceModes.Normalize(analysis.AttendanceMode) == Shared.Domain.AttendanceModes.Unknown
            ? "تحب الحضور أونلاين ولا في السنتر في الإسكندرية؟" : "تحب نراجع المواعيد المتاحة للحصة المجانية حسب اختيار حضرتك؟";
        analysis.ReplyContent = studyFormat + "\n\nمجموعة الدراسة من ١٥ لـ١٨ طالب، بحد أقصى ١٨. أول حصة تجربة مجانية تقدر تشوف فيها طريقة الشرح وتعرف النظام المناسب لمستواك؛ مواعيد الكورس بتتحدد في الحصة.\n\n" + nextStep;
        analysis.SuggestedGroupBookingId = null;
        analysis.SuggestedGroupBookingPeople = [];
        analysis.SuggestedGroupOfferIds = [];
        analysis.SuggestedButtons = [];
        analysis.CancelGroupBooking = false;
        UnconfirmedBookingFollowUpPolicy.Apply(analysis.SuggestedFollowUp);
    }

    private static string Normalize(string text) => Regex.Replace(text.ToLowerInvariant()
        .Replace('أ', 'ا').Replace('إ', 'ا').Replace('آ', 'ا').Replace('ة', 'ه'), @"[\u064B-\u065F\u0670]", "").Trim();
}
