using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Modules.Analytics.API;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;
using Modules.Analytics.Jobs;
using Modules.Conversations.Domain;
using Modules.CRM.Domain;
using Modules.Projects.Domain;
using Shared.Infrastructure;
using Shared.Security;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AnalyticsMetricAccuracyTests
{
    [Fact]
    public async Task Daily_sales_count_only_won_deals_with_a_recorded_closure_in_the_project_day_including_older_deals()
    {
        var projectId = Guid.NewGuid();
        await using var db = Database(projectId);
        var day = new DateTime(2026, 10, 16);
        var start = new DateTime(2026, 10, 15, 21, 0, 0, DateTimeKind.Utc);
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, Timezone = "Africa/Cairo" });
        var legacyWon = Deal(projectId, DealStatus.Won, 20, null);
        var deals = new[]
        {
            Deal(projectId, DealStatus.Won, 120, start), legacyWon,
            Deal(projectId, DealStatus.Open, 1000, null),
            Deal(projectId, DealStatus.Lost, 900, start.AddHours(1)),
            Deal(projectId, DealStatus.Won, 800, start.AddDays(1)),
            Deal(Guid.NewGuid(), DealStatus.Won, 700, start.AddHours(1))
        };
        db.Deals.AddRange(deals);
        await db.SaveChangesAsync();
        deals[0].CreatedAt = start.AddMonths(-1);
        legacyWon.CreatedAt = start.AddHours(2);
        await db.SaveChangesAsync();

        var snapshot = await new AnalyticsEngine(db).CalculateMetricAsync(projectId, "Sales", day);

        Assert.Equal(120m, snapshot.MetricValue);
        Assert.Equal(start, snapshot.SnapshotDate);
        var metadata = JsonSerializer.Deserialize<JsonElement>(snapshot.MetadataJson);
        Assert.Equal(2, metadata.GetProperty("totalDeals").GetInt32());
        Assert.Equal(1, metadata.GetProperty("wonDeals").GetInt32());
        Assert.Equal(1, metadata.GetProperty("undatedClosedDeals").GetInt32());
    }

    [Theory]
    [InlineData("2026-10-16", "2026-10-15T21:00:00Z", "2026-10-16T21:00:00Z")]
    [InlineData("2026-04-24", "2026-04-23T22:00:00Z", "2026-04-24T21:00:00Z")]
    public async Task Acquisition_uses_half_open_local_days_even_when_Cairo_midnight_is_skipped(string localDay, string startUtc, string endUtc)
    {
        var projectId = Guid.NewGuid();
        await using var db = Database(projectId);
        db.ProjectSettings.Add(new ProjectSettings { ProjectId = projectId, Timezone = "Africa/Cairo" });
        var start = DateTime.Parse(startUtc).ToUniversalTime();
        var end = DateTime.Parse(endUtc).ToUniversalTime();
        var instants = new[] { start.AddTicks(-1), start, end.AddTicks(-1), end };
        var customers = instants.Select(_ => new Customer { ProjectId = projectId, Name = "Customer", City = "", PhoneNumber = Guid.NewGuid().ToString() }).ToArray();
        db.Customers.AddRange(customers);
        await db.SaveChangesAsync();
        for (var i = 0; i < customers.Length; i++) customers[i].CreatedAt = instants[i];
        await db.SaveChangesAsync();

        var snapshot = await new AnalyticsEngine(db).CalculateMetricAsync(projectId, "Customer_Acquisition", DateTime.Parse(localDay));

        Assert.Equal(2m, snapshot.MetricValue);
        Assert.Equal(start, snapshot.SnapshotDate);
    }

    [Theory]
    [InlineData("AI_Accuracy", "accuracyAvailable")]
    [InlineData("Team_Performance", "responseTimeAvailable")]
    public async Task Unavailable_snapshots_are_null_and_excluded_from_averages_but_a_measured_zero_is_kept(string metric, string availability)
    {
        var projectId = Guid.NewGuid();
        await using var db = Database(projectId);
        var now = DateTime.UtcNow;
        db.AnalyticsSnapshots.AddRange(
            new AnalyticsSnapshot { ProjectId = projectId, MetricType = metric, MetricValue = 0, SnapshotDate = now, MetadataJson = $"{{\"{availability}\":false}}" },
            new AnalyticsSnapshot { ProjectId = projectId, MetricType = metric, MetricValue = 0, SnapshotDate = now.AddDays(-1), MetadataJson = $"{{\"{availability}\":true}}" });
        await db.SaveChangesAsync();
        var controller = Controller(db);

        var rows = Body(await controller.GetAnalytics(projectId, metric));
        Assert.Equal(JsonValueKind.Null, rows[0].GetProperty("metricValue").ValueKind);
        Assert.Equal(0m, rows[1].GetProperty("metricValue").GetDecimal());
        var report = Body(await controller.GenerateReport(projectId, new GenerateReportRequest
        { ReportType = "Summary", StartDate = now.AddDays(-2), EndDate = now.AddDays(1) }));
        Assert.Equal(1, report.GetProperty("totalSnapshotsUsed").GetInt32());
        Assert.Equal(1, report.GetProperty("unavailableSnapshots").GetInt32());
        Assert.Equal(0m, report.GetProperty("summaryMetrics")[0].GetProperty("averageValue").GetDecimal());
    }

    [Fact]
    public async Task Failed_project_save_does_not_leak_pending_snapshot_changes_into_the_next_project()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        var failure = new FailOneSnapshotSave();
        await using var db = Database(first, failure);
        db.Projects.AddRange(new Project { Id = first, Name = "First" }, new Project { Id = second, Name = "Second" });
        await db.SaveChangesAsync();

        await new DailyAnalyticsJob(db, new AnalyticsEngine(db)).ExecuteAsync();

        Assert.NotNull(failure.FailedProjectId);
        var saved = await db.AnalyticsSnapshots.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(4, saved.Count);
        Assert.All(saved, snapshot => Assert.NotEqual(failure.FailedProjectId, snapshot.ProjectId));
    }

    [Theory]
    [InlineData("Sales")]
    [InlineData("Customer_Acquisition")]
    public async Task Historical_snapshots_from_superseded_calculations_are_unavailable_until_recalculated(string metric)
    {
        var projectId = Guid.NewGuid();
        await using var db = Database(projectId);
        var now = DateTime.UtcNow;
        db.AnalyticsSnapshots.AddRange(
            new AnalyticsSnapshot { ProjectId = projectId, MetricType = metric, MetricValue = 999, SnapshotDate = now.AddDays(-1), MetadataJson = "{}" },
            new AnalyticsSnapshot { ProjectId = projectId, MetricType = metric, MetricValue = 0, SnapshotDate = now, MetadataJson = "{\"calculationVersion\":2}" });
        await db.SaveChangesAsync();

        var rows = Body(await Controller(db).GetAnalytics(projectId, metric));

        Assert.True(rows[0].GetProperty("metricAvailable").GetBoolean());
        Assert.Equal(0, rows[0].GetProperty("metricValue").GetDecimal());
        Assert.False(rows[1].GetProperty("metricAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("metricValue").ValueKind);
        Assert.Contains("recalculation", rows[1].GetProperty("unavailableReason").GetString());
    }

    [Fact]
    public async Task Recalculation_updates_today_and_yesterday_only_for_the_requested_project_without_duplicate_snapshots()
    {
        var projectId = Guid.NewGuid();
        var otherProject = Guid.NewGuid();
        await using var db = Database(projectId);
        db.Projects.AddRange(new Project { Id = projectId, Name = "Requested" }, new Project { Id = otherProject, Name = "Other" });
        db.AnalyticsSnapshots.Add(new AnalyticsSnapshot { ProjectId = otherProject, MetricType = "Sales", MetricValue = 654, SnapshotDate = DateTime.UtcNow.Date, MetadataJson = "{}" });
        await db.SaveChangesAsync();
        var controller = Controller(db);

        Assert.IsType<OkObjectResult>(await controller.RecalculateAnalytics(projectId));
        Assert.IsType<OkObjectResult>(await controller.RecalculateAnalytics(projectId));

        var all = await db.AnalyticsSnapshots.IgnoreQueryFilters().ToListAsync();
        Assert.Equal(654m, Assert.Single(all, snapshot => snapshot.ProjectId == otherProject).MetricValue);
        Assert.Equal(8, all.Count(snapshot => snapshot.ProjectId == projectId));
        Assert.Equal(2, all.Where(snapshot => snapshot.ProjectId == projectId).Select(snapshot => snapshot.SnapshotDate).Distinct().Count());
    }

    private static Deal Deal(Guid projectId, DealStatus status, decimal amount, DateTime? closedAt) => new()
    { ProjectId = projectId, Title = "Sale", Status = status, Amount = amount, ClosedAt = closedAt };

    private static AppDbContext Database(Guid projectId, SaveChangesInterceptor? interceptor = null)
    {
        var tenant = new TenantContext();
        tenant.SetProjectId(projectId);
        var options = new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString());
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new AppDbContext(options.Options,
            tenant, new ServiceCollection().BuildServiceProvider());
    }

    private sealed class FailOneSnapshotSave : SaveChangesInterceptor
    {
        public Guid? FailedProjectId { get; private set; }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            var pending = eventData.Context!.ChangeTracker.Entries<AnalyticsSnapshot>()
                .FirstOrDefault(entry => entry.State == EntityState.Added);
            if (FailedProjectId is null && pending is not null)
            {
                FailedProjectId = pending.Entity.ProjectId;
                throw new DbUpdateException("Simulated snapshot write failure.");
            }
            return ValueTask.FromResult(result);
        }
    }

    private static AnalyticsController Controller(AppDbContext db)
    {
        var engine = new AnalyticsEngine(db);
        return new AnalyticsController(db, engine, new DailyAnalyticsJob(db, engine));
    }

    private static JsonElement Body(IActionResult response) =>
        JsonSerializer.SerializeToElement(Assert.IsType<OkObjectResult>(response).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
}
