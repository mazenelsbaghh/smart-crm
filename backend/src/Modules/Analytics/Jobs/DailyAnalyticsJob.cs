using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.Analytics.Application.Services;

namespace Modules.Analytics.Jobs;

public class DailyAnalyticsJob(AppDbContext dbContext, IAnalyticsEngine analyticsEngine)
{
    private static readonly string[] MetricTypes = ["Sales", "AI_Accuracy", "Team_Performance", "Customer_Acquisition"];

    public async Task ExecuteAsync()
    {
        var projects = await dbContext.Projects.IgnoreQueryFilters().Select(project => project.Id).ToListAsync();
        foreach (var projectId in projects)
        {
            try
            {
                var (today, timezone) = await ReportingDateAsync(projectId);
                await ReplaceDayAsync(projectId, today.AddDays(-1), timezone);
                await dbContext.SaveChangesAsync();
            }
            catch (Exception exception)
            {
                dbContext.ChangeTracker.Clear();
                Console.WriteLine($"Error running DailyAnalyticsJob for project {projectId}: {exception.Message}");
            }
        }
    }

    public async Task ExecuteProjectAsync(Guid projectId)
    {
        var (today, timezone) = await ReportingDateAsync(projectId);
        await ReplaceDayAsync(projectId, today.AddDays(-1), timezone);
        await ReplaceDayAsync(projectId, today, timezone);
        await dbContext.SaveChangesAsync();
    }

    private async Task<(DateTime Date, string? Timezone)> ReportingDateAsync(Guid projectId)
    {
        var timezone = await dbContext.ProjectSettings.IgnoreQueryFilters()
            .Where(settings => settings.ProjectId == projectId).Select(settings => settings.Timezone).SingleOrDefaultAsync();
        return (TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, AnalyticsReportingDay.TimeZone(timezone)).Date, timezone);
    }

    private async Task ReplaceDayAsync(Guid projectId, DateTime day, string? timezone)
    {
        var snapshots = new List<Modules.Analytics.Domain.AnalyticsSnapshot>();
        foreach (var metricType in MetricTypes)
            snapshots.Add(await analyticsEngine.CalculateMetricAsync(projectId, metricType, day));
        var (start, end) = AnalyticsReportingDay.Bounds(day, timezone);
        var existing = await dbContext.AnalyticsSnapshots.IgnoreQueryFilters()
            .Where(snapshot => snapshot.ProjectId == projectId && snapshot.SnapshotDate >= start && snapshot.SnapshotDate < end)
            .ToListAsync();
        dbContext.AnalyticsSnapshots.RemoveRange(existing);
        dbContext.AnalyticsSnapshots.AddRange(snapshots);
    }
}
