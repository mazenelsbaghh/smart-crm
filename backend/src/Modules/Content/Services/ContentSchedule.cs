namespace Modules.Content.Services;

public static class ContentSchedule
{
    public static DateTime DailySlotUtc(DateTime localDate, TimeSpan firstPublishTime, string timezoneId, int slotIndex)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        return ConvertToUtc(LocalCandidate(localDate.Date, firstPublishTime.Add(TimeSpan.FromHours(4 * slotIndex))), timezone);
    }

    public static DateTime NextSlotUtc(DateTime afterUtc, TimeSpan firstPublishTime, string timezoneId, int postsPerDay)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), timezone);
        for (var slot = 0; slot < postsPerDay; slot++)
        {
            var candidate = DailySlotUtc(localNow.Date, firstPublishTime, timezoneId, slot);
            if (candidate > afterUtc) return candidate;
        }
        return DailySlotUtc(localNow.Date.AddDays(1), firstPublishTime, timezoneId, 0);
    }

    internal static IReadOnlyList<DateTime> NextWeekUtc(
        DateTime afterUtc,
        TimeSpan localPublishTime,
        string timezoneId)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), timezone);
        return Enumerable.Range(1, 7)
            .Select(day => ConvertToUtc(LocalCandidate(localNow.Date.AddDays(day), localPublishTime), timezone))
            .ToArray();
    }

    public static DateTime NextUtc(
        DateTime afterUtc,
        TimeSpan localPublishTime,
        string timezoneId)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), timezone);
        var localCandidate = LocalCandidate(localNow.Date, localPublishTime);
        if (localCandidate <= localNow) localCandidate = localCandidate.AddDays(1);
        return ConvertToUtc(localCandidate, timezone);
    }

    public static DateTime NextDayUtc(
        DateTime afterUtc,
        TimeSpan localPublishTime,
        string timezoneId)
    {
        var timezone = TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(afterUtc, DateTimeKind.Utc), timezone);
        return ConvertToUtc(LocalCandidate(localNow.Date.AddDays(1), localPublishTime), timezone);
    }

    private static DateTime ConvertToUtc(DateTime localCandidate, TimeZoneInfo timezone)
    {
        if (timezone.IsInvalidTime(localCandidate))
        {
            localCandidate = localCandidate.AddHours(1);
        }

        if (timezone.IsAmbiguousTime(localCandidate))
        {
            var offset = timezone.GetAmbiguousTimeOffsets(localCandidate).Max();
            return new DateTimeOffset(localCandidate, offset).UtcDateTime;
        }

        return TimeZoneInfo.ConvertTimeToUtc(localCandidate, timezone);
    }

    private static DateTime LocalCandidate(DateTime localDate, TimeSpan localPublishTime) =>
        DateTime.SpecifyKind(localDate.Add(localPublishTime), DateTimeKind.Unspecified);
}
