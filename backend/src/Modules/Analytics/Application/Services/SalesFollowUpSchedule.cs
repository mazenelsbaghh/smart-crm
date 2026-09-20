using Shared.Infrastructure;

namespace Modules.Analytics.Application.Services;

public static class SalesFollowUpSchedule
{
    public static string? ValidationError(FollowUpDispatchOptions options, int available, FollowUpPlanAction action)
    {
        var count = options.Count ?? available;
        if (count < 1 || count > 10_000 || count > available)
            return $"اختر عددًا بين 1 و{Math.Min(available, 10_000)} من العملاء المتاحين.";
        if (options.MinIntervalSeconds < 1 || options.MaxIntervalSeconds > 3600
            || options.MaxIntervalSeconds < options.MinIntervalSeconds)
            return "الفاصل يجب أن يكون بين 1 و3600 ثانية، والحد الأقصى لا يقل عن الحد الأدنى.";
        if (action == FollowUpPlanAction.SendNow) return null;
        if (options.ScheduleDays < 1 || options.ScheduleDays > Math.Min(count, 365))
            return "عدد أيام الجدولة بين 1 و365، ولا يزيد عن عدد الرسائل.";
        var largestDayCount = (int)Math.Ceiling((double)count / options.ScheduleDays);
        if ((long)(largestDayCount - 1) * options.MaxIntervalSeconds >= 23 * 3600)
            return "الرسائل لا تتسع داخل دفعة يومية. زوّد عدد أيام الجدولة أو قلّل العدد أو الفاصل.";
        return null;
    }

    public static IReadOnlyList<FollowUpSendSlot> Slots(
        FollowUpDispatchOptions options, FollowUpPlanAction action, DateTime nowUtc, string? timezoneId)
    {
        var count = options.Count!.Value;
        var days = action == FollowUpPlanAction.Schedule ? options.ScheduleDays : 1;
        var timezone = TimezoneHelper.GetTimeZone(timezoneId);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, timezone).AddDays(1);
        var slots = new List<FollowUpSendSlot>(count);
        // One message has no initial gap; remainder messages go to the earliest days.
        for (var day = 0; day < days; day++)
        {
            var dailyCount = count / days + (day < count % days ? 1 : 0);
            var dueAt = action == FollowUpPlanAction.SendNow
                ? nowUtc.AddSeconds(-1)
                : DayStartUtc(localStart.AddDays(day), timezone);
            for (var index = 0; index < dailyCount; index++)
            {
                var interval = slots.Count == 0 ? 0
                    : Random.Shared.Next(options.MinIntervalSeconds, options.MaxIntervalSeconds + 1);
                if (index > 0) dueAt = dueAt.AddSeconds(interval);
                slots.Add(new(dueAt, interval));
            }
        }
        return slots;
    }

    private static DateTime DayStartUtc(DateTime local, TimeZoneInfo timezone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (timezone.IsInvalidTime(local)) local = local.AddMinutes(1);
        return TimeZoneInfo.ConvertTimeToUtc(local, timezone);
    }
}

public sealed record FollowUpSendSlot(DateTime DueAtUtc, int IntervalSeconds);
