using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Shared.Security;
using Modules.Analytics.Domain;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Text.Json;

namespace Modules.Analytics.API
{
    [ApiController]
    [Route("api")]
    public class AnalyticsController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly IAnalyticsEngine _analyticsEngine;
        private readonly DailyAnalyticsJob _dailyAnalyticsJob;

        public AnalyticsController(AppDbContext context, IAnalyticsEngine analyticsEngine, DailyAnalyticsJob dailyAnalyticsJob)
        {
            _context = context;
            _analyticsEngine = analyticsEngine;
            _dailyAnalyticsJob = dailyAnalyticsJob;
        }

        [HttpGet("projects/{projectId}/analytics/{type}")]
        public async Task<IActionResult> GetAnalytics(Guid projectId, string type)
        {
            var snapshots = await _context.AnalyticsSnapshots
                .IgnoreQueryFilters()
                .Where(s => s.ProjectId == projectId && s.MetricType.ToLower() == type.ToLower())
                .OrderByDescending(s => s.SnapshotDate)
                .Take(30) // last 30 snapshots
                .ToListAsync();

            return Ok(snapshots.Select(snapshot =>
            {
                var value = ReportedValue(snapshot);
                return new
                {
                    snapshot.Id, snapshot.ProjectId, snapshot.SnapshotDate, snapshot.MetricType,
                    metricValue = value,
                    metricAvailable = value.HasValue,
                    unavailableReason = value.HasValue ? null : "This snapshot needs a supported measurement or recalculation with the current reporting rules.",
                    snapshot.MetadataJson, snapshot.CreatedAt, snapshot.UpdatedAt
                };
            }));
        }

        [HttpPost("projects/{projectId}/reports/generate")]
        [ProjectPermission("read")]
        public async Task<IActionResult> GenerateReport(Guid projectId, [FromBody] GenerateReportRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ReportType))
            {
                return BadRequest("ReportType is required.");
            }

            var startDate = request.StartDate ?? DateTime.UtcNow.AddDays(-7);
            var endDate = request.EndDate ?? DateTime.UtcNow;
            if (endDate <= startDate) return BadRequest("EndDate must be after StartDate.");

            // Generate report content on the fly by computing current metrics or fetching historical snapshots
            var snapshots = await _context.AnalyticsSnapshots
                .IgnoreQueryFilters()
                .Where(s => s.ProjectId == projectId && s.SnapshotDate >= startDate && s.SnapshotDate <= endDate)
                .ToListAsync();

            var measuredSnapshots = snapshots.Select(snapshot => new { snapshot.MetricType, Value = ReportedValue(snapshot) }).ToList();
            var reportSummary = new
            {
                reportId = Guid.NewGuid(),
                projectId = projectId,
                reportType = request.ReportType,
                startDate = startDate,
                endDate = endDate,
                generatedAt = DateTime.UtcNow,
                totalSnapshotsUsed = measuredSnapshots.Count(snapshot => snapshot.Value.HasValue),
                unavailableSnapshots = measuredSnapshots.Count(snapshot => !snapshot.Value.HasValue),
                summaryMetrics = measuredSnapshots.GroupBy(s => s.MetricType)
                    .Select(g => new
                    {
                        metricType = g.Key,
                        measuredSnapshots = g.Count(s => s.Value.HasValue),
                        averageValue = g.Average(s => s.Value),
                        maxValue = g.Max(s => s.Value),
                        minValue = g.Min(s => s.Value)
                    }).ToList()
            };

            return Ok(reportSummary);
        }

        // Diagnostic endpoint to trigger daily snapshots run manually
        [HttpPost("projects/{projectId}/analytics/recalculate")]
        public async Task<IActionResult> RecalculateAnalytics(Guid projectId)
        {
            try
            {
                if (!await _context.Projects.AnyAsync(project => project.Id == projectId)) return NotFound();
                await _dailyAnalyticsJob.ExecuteProjectAsync(projectId);
                return Ok(new { message = "Analytics snapshots updated for the selected project." });
            }
            catch (Exception ex)
            {
                return BadRequest($"Recalculation failed: {ex.Message}");
            }
        }

        private static decimal? ReportedValue(AnalyticsSnapshot snapshot)
        {
            var type = snapshot.MetricType.ToLowerInvariant();
            var availability = type switch
            {
                "ai_accuracy" => "accuracyAvailable",
                "team_performance" => "responseTimeAvailable",
                _ => null
            };
            var requiresCurrentCalculation = type is "sales" or "customer_acquisition";
            if (availability is null && !requiresCurrentCalculation) return snapshot.MetricValue;
            try
            {
                using var metadata = JsonDocument.Parse(snapshot.MetadataJson ?? "{}");
                if (metadata.RootElement.ValueKind != JsonValueKind.Object) return null;
                if (requiresCurrentCalculation)
                    return metadata.RootElement.TryGetProperty("calculationVersion", out var version)
                        && version.ValueKind == JsonValueKind.Number && version.TryGetInt32(out var value) && value == 2
                        ? snapshot.MetricValue : null;
                return metadata.RootElement.TryGetProperty(availability!, out var known) && known.ValueKind == JsonValueKind.True
                    ? snapshot.MetricValue : null;
            }
            catch (JsonException) { return null; }
        }
    }

    public class GenerateReportRequest
    {
        public string ReportType { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
