using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.Analytics.Application.Services;
using Modules.Analytics.Domain;

namespace Modules.Analytics.Jobs
{
    public class DailyAnalyticsJob
    {
        private readonly AppDbContext _dbContext;
        private readonly IAnalyticsEngine _analyticsEngine;

        public DailyAnalyticsJob(AppDbContext dbContext, IAnalyticsEngine analyticsEngine)
        {
            _dbContext = dbContext;
            _analyticsEngine = analyticsEngine;
        }

        public async Task ExecuteAsync()
        {
            // We ignore global query filter here to get all projects in the system
            var projects = await _dbContext.Projects
                .IgnoreQueryFilters()
                .ToListAsync();

            var yesterday = DateTime.UtcNow.AddDays(-1);

            foreach (var project in projects)
            {
                try
                {
                    // Compute metrics
                    var salesSnapshot = await _analyticsEngine.CalculateMetricAsync(project.Id, "Sales", yesterday);
                    var aiSnapshot = await _analyticsEngine.CalculateMetricAsync(project.Id, "AI_Accuracy", yesterday);
                    var teamSnapshot = await _analyticsEngine.CalculateMetricAsync(project.Id, "Team_Performance", yesterday);
                    var customerSnapshot = await _analyticsEngine.CalculateMetricAsync(project.Id, "Customer_Acquisition", yesterday);

                    // Clear old snapshots for this date and project to prevent duplicates
                    var oldSnapshots = await _dbContext.AnalyticsSnapshots
                        .IgnoreQueryFilters()
                        .Where(s => s.ProjectId == project.Id && s.SnapshotDate.Date == yesterday.Date)
                        .ToListAsync();
                    
                    _dbContext.AnalyticsSnapshots.RemoveRange(oldSnapshots);

                    // Add new snapshots
                    _dbContext.AnalyticsSnapshots.Add(salesSnapshot);
                    _dbContext.AnalyticsSnapshots.Add(aiSnapshot);
                    _dbContext.AnalyticsSnapshots.Add(teamSnapshot);
                    _dbContext.AnalyticsSnapshots.Add(customerSnapshot);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error running DailyAnalyticsJob for project {project.Id}: {ex.Message}");
                }
            }

            await _dbContext.SaveChangesAsync();
        }
    }
}
