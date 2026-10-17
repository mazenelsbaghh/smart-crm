using System.Text.RegularExpressions;
using Modules.GroupAppointments.Domain;
using Modules.GroupAppointments.Services;
using Shared.Domain;

namespace Modules.AI.Services;

internal sealed record GroupAppointmentOfferContext(
    IReadOnlyList<GroupAppointment> Groups,
    TimeZoneInfo Timezone,
    string? CustomerCity);

internal static partial class GroupAppointmentOfferReplyPolicy
{
    public static void Apply(MarketingAnalysisResult analysis, GroupAppointmentOfferContext context)
    {
        if (analysis.RequestHuman || analysis.IsFallbackResponse
            || !string.IsNullOrWhiteSpace(analysis.SuggestedGroupBookingId)) return;
        if (analysis.SuggestedGroupOfferIds.Length == 0 && !ContainsFreeSessionTime(analysis.ReplyContent)) return;

        // Generated prose can mix two groups' facts; render offers only from their authoritative rows.
        analysis.ReplyContent = OfferReply(analysis, context);
        if (analysis.SuggestedFollowUp is not { Needed: true } followUp) return;
        if (string.Equals(followUp.Type, "AppointmentReminder", StringComparison.OrdinalIgnoreCase))
            followUp.Needed = false;
        else
            followUp.Notes = "تحب نساعد حضرتك في اختيار موعد مناسب للحصة المجانية؟";
    }

    private static string OfferReply(MarketingAnalysisResult analysis, GroupAppointmentOfferContext context)
    {
        var mode = AttendanceModes.Normalize(analysis.AttendanceMode);
        if (mode == AttendanceModes.Unknown) return "تحب الحضور أونلاين ولا أوفلاين في السنتر في الإسكندرية؟";
        if (mode == AttendanceModes.Offline && !GroupBookingAttendancePolicy.HasKnownCity(context.CustomerCity))
            return "الحضور في السنتر أوفلاين في الإسكندرية. هل حضرتك مقيم في الإسكندرية؟";
        if (mode == AttendanceModes.Offline && !GroupBookingAttendancePolicy.IsAlexandria(context.CustomerCity))
            return "الحضور في السنتر متاح للمقيمين في الإسكندرية. تحب نعرض لحضرتك مواعيد الأونلاين؟";

        var eligible = EligibleGroups(context, mode);
        var offered = eligible.Where(group => analysis.SuggestedGroupOfferIds.Contains(group.Id.ToString(), StringComparer.OrdinalIgnoreCase)).ToList();
        if (offered.Count == 0) offered = MatchingCourseGroups(analysis, context, eligible);
        if (offered.Count == 0) return "لا توجد حصة مجانية متاحة حاليًا لطريقة الحضور ونوع الكورس المطلوبين.";
        if (offered.Select(group => group.CourseType).Distinct().Count() > 1)
            return "تحب مواعيد الحصة المجانية للكورس العادي ولا المكثف؟";
        return "مواعيد الحصص المجانية المتاحة:\n\n" + string.Join("\n\n", offered.Take(3).Select(group => OfferDetails(group, context.Timezone)));
    }

    private static List<GroupAppointment> EligibleGroups(GroupAppointmentOfferContext context, string mode)
    {
        var now = DateTime.UtcNow;
        return context.Groups.Where(group => group.IsActive
                && (group.FreeSessionDateTime ?? group.DateTime) > now
                && group.Bookings.Count < group.Capacity
                && (mode == AttendanceModes.Either || AttendanceModes.Normalize(group.Mode) == mode)
                && GroupBookingAttendancePolicy.Rejection(group.Mode, context.CustomerCity, mode) is null)
            .OrderBy(group => group.FreeSessionDateTime ?? group.DateTime).ToList();
    }

    private static List<GroupAppointment> MatchingCourseGroups(
        MarketingAnalysisResult analysis, GroupAppointmentOfferContext context, List<GroupAppointment> eligible)
    {
        var requestedTypes = context.Groups
            .Where(group => analysis.SuggestedGroupOfferIds.Contains(group.Id.ToString(), StringComparer.OrdinalIgnoreCase))
            .Select(group => group.CourseType).Distinct().ToArray();
        if (requestedTypes.Length == 1)
            return eligible.Where(group => group.CourseType == requestedTypes[0]).ToList();
        var reply = analysis.ReplyContent;
        if (reply.Contains("المكثف") && !reply.Contains("العادي"))
            return eligible.Where(group => group.CourseType == CourseTypes.Intensive).ToList();
        if (reply.Contains("العادي") && !reply.Contains("المكثف"))
            return eligible.Where(group => group.CourseType == CourseTypes.Regular).ToList();
        return eligible;
    }

    private static string OfferDetails(GroupAppointment group, TimeZoneInfo timezone)
    {
        var mode = AttendanceModes.Normalize(group.Mode) == AttendanceModes.Online
            ? "أونلاين" : "أوفلاين (في السنتر في الإسكندرية)";
        return $"• الحضور: {mode}{FreeSessionBookingPolicy.ScheduleDetails(group, timezone)}";
    }

    private static bool ContainsFreeSessionTime(string reply) =>
        reply.Contains("مجاني") && SessionTime().IsMatch(reply);

    [GeneratedRegex(@"(?:الساعة|الساعه)\s*\p{Nd}{1,2}|\p{Nd}{1,2}[:：]\p{Nd}{1,2}")]
    private static partial Regex SessionTime();
}
