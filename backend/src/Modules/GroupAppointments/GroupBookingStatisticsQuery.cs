using Microsoft.EntityFrameworkCore;
using Modules.Analytics.Application.Services;
using Shared.Infrastructure;

namespace Modules.GroupAppointments.Services;

public sealed record GroupBookingStatistics
{
    public int TotalBooked { get; init; }
    public int BookedToday { get; init; }
    public bool TodayIncludesTransfers { get; init; } = true;
    public string? CourseType { get; init; }
    public string Timezone { get; init; } = "Africa/Cairo";
    public DateTime DayStartUtc { get; init; }
    public DateTime DayEndUtc { get; init; }
    public DateTime AsOfUtc { get; init; }
}

public sealed class GroupBookingStatisticsQuery(AppDbContext db)
{
    public async Task<GroupBookingStatistics> ReadAsync(Guid projectId, string? courseType, DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        const string timezone = "Africa/Cairo";
        var localDate = TimeZoneInfo.ConvertTimeFromUtc(nowUtc, TimezoneHelper.GetTimeZone(timezone)).Date;
        var (startUtc, endUtc) = AnalyticsReportingDay.Bounds(localDate, timezone);
        var counts = await db.GroupAppointmentBookings.AsNoTracking()
            .Where(booking => booking.ProjectId == projectId && booking.GroupAppointment.ProjectId == projectId
                && (courseType == null || booking.GroupAppointment.CourseType == courseType))
            .GroupBy(_ => 1)
            .Select(bookings => new
            {
                Total = bookings.Count(),
                Today = bookings.Count(booking => booking.CreatedAt >= startUtc && booking.CreatedAt < endUtc)
            }).SingleOrDefaultAsync(cancellationToken);
        return new()
        {
            TotalBooked = counts?.Total ?? 0, BookedToday = counts?.Today ?? 0, CourseType = courseType,
            Timezone = timezone, DayStartUtc = startUtc, DayEndUtc = endUtc, AsOfUtc = nowUtc
        };
    }
}
