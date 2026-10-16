namespace Modules.Analytics.Application.Services;

internal static class AnalyticsReportingDay
{
    public static TimeZoneInfo TimeZone(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return TimeZoneInfo.Utc;
        try { return TimeZoneInfo.FindSystemTimeZoneById(name); }
        catch (TimeZoneNotFoundException) { return TimeZoneInfo.Utc; }
        catch (InvalidTimeZoneException) { return TimeZoneInfo.Utc; }
    }

    public static (DateTime StartUtc, DateTime EndUtc) Bounds(DateTime localDate, string? timezone)
    {
        var zone = TimeZone(timezone);
        var start = DateTime.SpecifyKind(localDate.Date, DateTimeKind.Unspecified);
        return (DayBoundary(start, zone), DayBoundary(start.AddDays(1), zone));
    }

    private static DateTime DayBoundary(DateTime localMidnight, TimeZoneInfo zone)
    {
        // Some project zones start daylight saving at midnight; use the first valid instant of that day.
        while (zone.IsInvalidTime(localMidnight)) localMidnight = localMidnight.AddMinutes(1);
        if (zone.IsAmbiguousTime(localMidnight))
            return new DateTimeOffset(localMidnight, zone.GetAmbiguousTimeOffsets(localMidnight).Max()).UtcDateTime;
        return TimeZoneInfo.ConvertTimeToUtc(localMidnight, zone);
    }
}
