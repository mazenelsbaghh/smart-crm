using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.Analytics.Domain;
using Modules.CRM.Domain;

namespace Modules.Analytics.Application.Services
{
    public interface IAnalyticsEngine
    {
        Task<AnalyticsSnapshot> CalculateMetricAsync(Guid projectId, string metricType, DateTime date);
    }

    public class AnalyticsEngine : IAnalyticsEngine
    {
        private readonly AppDbContext _dbContext;

        public AnalyticsEngine(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task<AnalyticsSnapshot> CalculateMetricAsync(Guid projectId, string metricType, DateTime date)
        {
            var timezone = await _dbContext.ProjectSettings.IgnoreQueryFilters()
                .Where(settings => settings.ProjectId == projectId).Select(settings => settings.Timezone).SingleOrDefaultAsync();
            var reportingZone = AnalyticsReportingDay.TimeZone(timezone);
            var (startOfDay, endOfDay) = AnalyticsReportingDay.Bounds(date, reportingZone.Id);

            decimal metricValue = 0;
            object metadata = new { };

            if (string.Equals(metricType, "Sales", StringComparison.OrdinalIgnoreCase))
            {
                var deals = await _dbContext.Deals
                    .IgnoreQueryFilters()
                    .Where(d => d.ProjectId == projectId && d.Status != DealStatus.Open &&
                        d.ClosedAt >= startOfDay && d.ClosedAt < endOfDay)
                    .ToListAsync();
                var undatedClosedDeals = await _dbContext.Deals.IgnoreQueryFilters()
                    .CountAsync(d => d.ProjectId == projectId && d.Status != DealStatus.Open && d.ClosedAt == null);

                var totalDeals = deals.Count;
                var wonDeals = deals.Count(d => d.Status == DealStatus.Won);
                var lostDeals = deals.Count(d => d.Status == DealStatus.Lost);
                var totalAmount = deals.Where(d => d.Status == DealStatus.Won).Sum(d => d.Amount);

                metricValue = totalAmount;
                metadata = new
                {
                    totalDeals,
                    wonDeals,
                    lostDeals,
                    conversionRate = totalDeals > 0 ? (double?)wonDeals / totalDeals : null,
                    calculationVersion = 2,
                    basis = "Won deals with a recorded closure time in the project reporting day. Undated closed deals are excluded.",
                    undatedClosedDeals,
                    timezone = reportingZone.Id
                };
            }
            else if (string.Equals(metricType, "AI_Accuracy", StringComparison.OrdinalIgnoreCase))
            {
                // Calculate AI reply stats
                var messages = await _dbContext.Messages
                    .IgnoreQueryFilters()
                    .Join(_dbContext.Conversations.IgnoreQueryFilters(),
                        m => m.ConversationId,
                        c => c.Id,
                        (m, c) => new { m, c })
                    .Where(x => x.c.ProjectId == projectId && x.m.Timestamp >= startOfDay && x.m.Timestamp < endOfDay)
                    .Select(x => x.m)
                    .ToListAsync();

                var outgoingMessages = messages.Count(m => m.Direction != null && m.Direction.Equals("Outgoing", StringComparison.OrdinalIgnoreCase));
                var totalMessages = messages.Count;

                metricValue = 0;
                metadata = new
                {
                    totalMessages,
                    outgoingMessages,
                    accuracyAvailable = false,
                    unavailableReason = "Message records do not identify the reply author or contain reviewed accuracy labels."
                };
            }
            else if (string.Equals(metricType, "Team_Performance", StringComparison.OrdinalIgnoreCase))
            {
                // Calculate average response time
                var conversations = await _dbContext.Conversations
                    .IgnoreQueryFilters()
                    .Where(c => c.ProjectId == projectId && c.UpdatedAt >= startOfDay && c.UpdatedAt < endOfDay)
                    .ToListAsync();

                metricValue = 0m;
                metadata = new
                {
                    totalConversations = conversations.Count,
                    responseTimeAvailable = false,
                    unavailableReason = "Legacy snapshots do not retain paired incoming and outgoing message timestamps.",
                    slaBreaches = (int?)null,
                    slaBreachesAvailable = false
                };
            }
            else if (string.Equals(metricType, "Customer_Acquisition", StringComparison.OrdinalIgnoreCase))
            {
                // Default: Customer acquisition metric
                var newCustomersCount = await _dbContext.Customers
                    .IgnoreQueryFilters()
                    .CountAsync(c => c.ProjectId == projectId && c.CreatedAt >= startOfDay && c.CreatedAt < endOfDay);

                metricValue = newCustomersCount;
                metadata = new
                {
                    totalCustomersCount = newCustomersCount,
                    calculationVersion = 2,
                    timezone = reportingZone.Id
                };
            }
            else
            {
                throw new ArgumentException("Unsupported analytics metric.", nameof(metricType));
            }

            return new AnalyticsSnapshot
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                SnapshotDate = startOfDay,
                MetricType = metricType,
                MetricValue = metricValue,
                MetadataJson = JsonSerializer.Serialize(metadata)
            };
        }
    }
}
