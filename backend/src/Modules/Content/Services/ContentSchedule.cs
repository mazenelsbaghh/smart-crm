namespace Modules.Content.Services;

public static class ContentSchedule
{
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
