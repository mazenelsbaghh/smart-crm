using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.Analytics.Domain;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Jobs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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

            return Ok(snapshots);
        }

        [HttpPost("projects/{projectId}/reports/generate")]
        public async Task<IActionResult> GenerateReport(Guid projectId, [FromBody] GenerateReportRequest request)
        {
            if (request == null || string.IsNullOrEmpty(request.ReportType))
            {
                return BadRequest("ReportType is required.");
            }

            var startDate = request.StartDate ?? DateTime.UtcNow.AddDays(-7);
            var endDate = request.EndDate ?? DateTime.UtcNow;

            // Generate report content on the fly by computing current metrics or fetching historical snapshots
            var snapshots = await _context.AnalyticsSnapshots
                .IgnoreQueryFilters()
                .Where(s => s.ProjectId == projectId && s.SnapshotDate >= startDate && s.SnapshotDate <= endDate)
                .ToListAsync();

            // Return simulated PDF/JSON output containing report details
            var reportSummary = new
            {
                reportId = Guid.NewGuid(),
                projectId = projectId,
                reportType = request.ReportType,
                startDate = startDate,
                endDate = endDate,
                generatedAt = DateTime.UtcNow,
                totalSnapshotsUsed = snapshots.Count,
                summaryMetrics = snapshots.GroupBy(s => s.MetricType)
                    .Select(g => new
                    {
                        metricType = g.Key,
                        averageValue = g.Average(s => s.MetricValue),
                        maxValue = g.Max(s => s.MetricValue),
                        minValue = g.Min(s => s.MetricValue)
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
                await _dailyAnalyticsJob.ExecuteAsync();
                return Ok(new { message = "Analytics snapshots calculated successfully for all projects." });
            }
            catch (Exception ex)
            {
                return BadRequest($"Recalculation failed: {ex.Message}");
            }
        }
    }

    public class GenerateReportRequest
    {
        public string ReportType { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
    }
}
