using Modules.Advertising.Domain;

namespace Modules.Advertising.Services;

public static class AdvertisingInsightWindow
{
    // Meta date_start/date_stop are account-local dates; older snapshots stored those dates with a UTC kind.
    public static DateTime StartUtc(InsightsSnapshot snapshot) => BoundaryUtc(snapshot, snapshot.IntervalStartUtc);
    public static DateTime EndUtc(InsightsSnapshot snapshot) => BoundaryUtc(snapshot, snapshot.IntervalEndUtc);

    private static DateTime BoundaryUtc(InsightsSnapshot snapshot, DateTime boundary)
    {
        if (snapshot.IntervalStartUtc.TimeOfDay != TimeSpan.Zero
            || snapshot.IntervalEndUtc != snapshot.IntervalStartUtc.AddDays(1)
            || string.IsNullOrWhiteSpace(snapshot.AccountTimezone)) return boundary;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(snapshot.AccountTimezone);
        return DayBoundaryUtc(boundary, zone);
    }

    public static DateTime DayBoundaryUtc(DateTime localBoundary, TimeZoneInfo zone)
    {
        localBoundary = DateTime.SpecifyKind(localBoundary, DateTimeKind.Unspecified);
        // Midnight clock changes must include the first valid instant, including the first occurrence of a repeated midnight.
        while (zone.IsInvalidTime(localBoundary)) localBoundary = localBoundary.AddMinutes(1);
        if (zone.IsAmbiguousTime(localBoundary))
            return new DateTimeOffset(localBoundary, zone.GetAmbiguousTimeOffsets(localBoundary).Max()).UtcDateTime;
        return TimeZoneInfo.ConvertTimeToUtc(localBoundary, zone);
    }
}
