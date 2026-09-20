using Microsoft.EntityFrameworkCore;
using Modules.CRM.Domain;
using Modules.GroupAppointments.Domain;
using Shared.Infrastructure;

namespace Modules.CRM.Services;

public sealed class ScheduleAvailabilityMatcher(AppDbContext db)
{
    public async Task MatchAsync(CancellationToken cancellationToken = default)
    {
        var waiting = await db.ScheduleAvailabilityPreferences.IgnoreQueryFilters()
            .Where(preference => preference.Status == "Waiting")
            .OrderBy(preference => preference.CreatedAt)
            .ToListAsync(cancellationToken);
        foreach (var projectPreferences in waiting.GroupBy(preference => preference.ProjectId))
            await MatchProjectAsync(projectPreferences.Key, projectPreferences, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task MatchProjectAsync(
        Guid projectId,
        IEnumerable<ScheduleAvailabilityPreference> preferences,
        CancellationToken cancellationToken)
    {
        var timezone = await ProjectTimezoneAsync(projectId, cancellationToken);
        var groups = await AvailableGroupsAsync(projectId, cancellationToken);
        foreach (var preference in preferences)
        {
            var matchedGroup = FirstMatchingGroup(preference, groups, timezone);
            if (matchedGroup is not null)
                await QueueNotificationAsync(preference, matchedGroup, timezone, cancellationToken);
        }
    }

    private async Task<TimeZoneInfo> ProjectTimezoneAsync(Guid projectId, CancellationToken cancellationToken)
    {
        var timezoneId = await db.ProjectSettings.IgnoreQueryFilters()
            .Where(settings => settings.ProjectId == projectId)
            .Select(settings => settings.Timezone)
            .FirstOrDefaultAsync(cancellationToken);
        return TimezoneHelper.GetTimeZone(timezoneId ?? "Africa/Cairo");
    }

    private Task<List<GroupAppointment>> AvailableGroupsAsync(Guid projectId, CancellationToken cancellationToken) =>
        db.GroupAppointments.IgnoreQueryFilters()
            .Include(group => group.Bookings)
            .Where(group => group.ProjectId == projectId
                && group.IsActive
                && group.Bookings.Count < group.Capacity)
            .OrderBy(group => group.DateTime)
            .ToListAsync(cancellationToken);

    private static GroupAppointment? FirstMatchingGroup(
        ScheduleAvailabilityPreference preference,
        IEnumerable<GroupAppointment> groups,
        TimeZoneInfo timezone)
    {
        var deadline = ScheduleAvailabilityHorizons.DeadlineUtc(
            preference.AvailabilityHorizon,
            preference.CreatedAt);
        return groups.FirstOrDefault(group =>
            (!deadline.HasValue || group.DateTime <= deadline.Value)
            && group.DateTime >= DateTime.UtcNow
            && ScheduleAvailabilityWindows.ContainsHour(
                preference.TimeWindow,
                LocalTime(group, timezone).Hour));
    }

    private async Task QueueNotificationAsync(
        ScheduleAvailabilityPreference preference,
        GroupAppointment group,
        TimeZoneInfo timezone,
        CancellationToken cancellationToken)
    {
        var slotKey = $"schedule-availability:{preference.Id:N}";
        var followUp = await db.FollowUps.IgnoreQueryFilters().FirstOrDefaultAsync(candidate =>
            candidate.ProjectId == preference.ProjectId
            && candidate.ActiveAutomationSlotKey == slotKey,
            cancellationToken);
        followUp ??= AddNotification(preference, group, timezone, slotKey);
        preference.Status = "Queued";
        preference.MatchedGroupAppointmentId = group.Id;
        preference.NotificationFollowUpId = followUp.Id;
    }

    private FollowUp AddNotification(
        ScheduleAvailabilityPreference preference,
        GroupAppointment group,
        TimeZoneInfo timezone,
        string slotKey)
    {
        var localTime = LocalTime(group, timezone);
        var followUp = new FollowUp
        {
            ProjectId = preference.ProjectId,
            CustomerId = preference.CustomerId,
            ConversationId = preference.ConversationId,
            WhatsAppAccountId = preference.WhatsAppAccountId,
            Channel = preference.Channel,
            ActiveAutomationSlotKey = slotKey,
            DueDate = DateTime.UtcNow.AddSeconds(-1),
            Type = "Nurturing",
            Tone = "Default",
            Status = "Pending",
            Notes = NotificationText(preference.TimeWindow, localTime)
        };
        db.FollowUps.Add(followUp);
        return followUp;
    }

    private static DateTime LocalTime(GroupAppointment group, TimeZoneInfo timezone) =>
        TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.SpecifyKind(group.DateTime, DateTimeKind.Utc),
            timezone);

    private static string NotificationText(string window, DateTime localTime)
    {
        var dayPeriod = localTime.Hour >= 12 ? "مساءً" : "صباحًا";
        return $"اتفتح دلوقتي موعد مناسب يوم {localTime:dd/MM/yyyy} الساعة {localTime:h:mm} {dayPeriod}، داخل الفترة اللي اخترتها ({ScheduleAvailabilityWindows.ArabicLabel(window)}). لو حابب تحجزه ابعتلنا ونأكد لك الحجز.";
    }
}
