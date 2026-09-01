using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record AdvertisingReportingWindow(DateTime StartUtc, DateTime EndUtc,
    IReadOnlyList<InsightsSnapshot> Insights, IReadOnlyList<CanonicalConversion> Outcomes, DateTime AsOfUtc);

public sealed class AdvertisingReportingWindowService(AppDbContext db)
{
    public async Task<AdvertisingReportingWindow> BuildAsync(Guid projectId, DateTime startUtc, DateTime endUtc,
        Guid? targetId = null, CancellationToken cancellationToken = default)
    {
        if (startUtc >= endUtc) throw new AdvertisingException("ADS_REPORTING_WINDOW_INVALID", "Reporting window must have a positive duration.", 422);
        var insights = await db.AdvertisingInsights.AsNoTracking().Where(snapshot => snapshot.ProjectId == projectId && snapshot.IsCurrent
            && snapshot.IntervalStartUtc >= startUtc && snapshot.IntervalEndUtc <= endUtc
            && (targetId == null || snapshot.TargetId == targetId)).ToListAsync(cancellationToken);
        var outcomes = await db.AdvertisingConversions.AsNoTracking().Where(conversion => conversion.ProjectId == projectId
            && conversion.OccurredAtUtc >= startUtc && conversion.OccurredAtUtc < endUtc
            && (targetId == null || conversion.AdvertisementId == targetId)).ToListAsync(cancellationToken);
        return new(startUtc, endUtc, insights, outcomes, DateTime.UtcNow);
    }
}
