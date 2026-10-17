using System.Text.RegularExpressions;
using Modules.AI.Services;

namespace Modules.TalkTips.Services;

internal static class TalkTipsCourseGroupSizePolicy
{
    public const string Instructions = """
        CURRENT TALKTIPS STUDY GROUP SIZE: a study group has 15–18 students, maximum 18, for both online and center attendance.
        Appointment capacity and current booking counts below concern free-trial registration only; they are not study-group size.
        When asked how many students attend the lecture/group, explain the study-group size. Never claim 50 students per study group.
        """;

    public static void Apply(string customerMessage, MarketingAnalysisResult analysis)
    {
        var question = customerMessage.Replace('أ', 'ا').Replace('إ', 'ا').Replace('ة', 'ه');
        if (!Regex.IsMatch(question, @"(?:كام|عدد).*(?:طالب|طلاب|طلبه|واحد|مشترك).*(?:محاضر|مجموع|جروب|كورس)|(?:محاضر|مجموع|جروب).*(?:كام|عدد).*(?:طالب|طلاب|طلبه|واحد|مشترك)")) return;
        analysis.ReplyContent = "مجموعة الدراسة بتكون من ١٥ لـ١٨ طالب، وبحد أقصى ١٨ طالب، سواء أونلاين أو في السنتر، علشان كل واحد ياخد فرصته في الكلام والتطبيق. عدد حجوزات الحصة المجانية منفصل عن حجم مجموعة الدراسة.";
        analysis.SuggestedGroupBookingId = null;
        analysis.SuggestedGroupBookingPeople = [];
        analysis.SuggestedGroupOfferIds = [];
        analysis.SuggestedButtons = [];
        UnconfirmedBookingFollowUpPolicy.Apply(analysis.SuggestedFollowUp);
    }
}
