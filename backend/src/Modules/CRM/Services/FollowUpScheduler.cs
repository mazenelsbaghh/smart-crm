using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Shared.Infrastructure;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Hangfire;

namespace Modules.CRM.Services
{
    public class FollowUpScheduler : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;

        public FollowUpScheduler(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // Register Hangfire recurring jobs on startup
            RecurringJob.AddOrUpdate<FollowUpScheduler>(
                "check-overdue-followups",
                s => s.CheckOverdueFollowUpsJobAsync(),
                "*/5 * * * * *"); // 5-second interval for responsive testing in Hangfire (using custom 6-field cron if supported, or fall back to minutely)
            
            RecurringJob.AddOrUpdate<FollowUpScheduler>(
                "recalculate-lead-scores",
                s => s.RecalculateLeadScoresJobAsync(),
                Cron.Minutely);

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public async Task CheckOverdueFollowUpsJobAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var now = DateTime.UtcNow;
            var overdueFollowUps = await dbContext.FollowUps
                .IgnoreQueryFilters()
                .Where(f => f.Status == "Pending" && f.DueDate < now)
                .ToListAsync();

            if (overdueFollowUps.Any())
            {
                Console.WriteLine($"[Hangfire Job] Found {overdueFollowUps.Count} overdue follow-ups. Marking as Missed.");
                foreach (var followUp in overdueFollowUps)
                {
                    followUp.Status = "Missed";
                }
                await dbContext.SaveChangesAsync();
            }
        }

        public async Task RecalculateLeadScoresJobAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var customers = await dbContext.Customers
                .IgnoreQueryFilters()
                .ToListAsync();

            foreach (var customer in customers)
            {
                var missedCount = await dbContext.FollowUps
                    .IgnoreQueryFilters()
                    .CountAsync(f => f.CustomerId == customer.Id && f.Status == "Missed");
                
                if (missedCount > 0)
                {
                    customer.LeadScore = Math.Max(0, customer.LeadScore - (missedCount * 2));
                }
            }

            await dbContext.SaveChangesAsync();
            Console.WriteLine($"[Hangfire Job] Recalculated lead scores for {customers.Count} customers.");
        }
    }
}
