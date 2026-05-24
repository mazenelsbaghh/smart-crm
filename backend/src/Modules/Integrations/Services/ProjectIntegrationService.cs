using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Integrations.Domain;
using Shared.Infrastructure;
using System;
using System.Threading.Tasks;

namespace Modules.Integrations.Services
{
    public interface IProjectIntegrationService
    {
        Task<ProjectIntegration> ConfigureIntegrationAsync(Guid projectId, string providerName, string configJson, bool isActive, int syncIntervalMinutes);
        Task TriggerSyncAsync(Guid projectId, Guid integrationId);
        Task PerformSyncAsync(Guid projectId, Guid integrationId);
    }

    public class ProjectIntegrationService : IProjectIntegrationService
    {
        private readonly AppDbContext _dbContext;
        private readonly IBackgroundJobClient _backgroundJobs;

        public ProjectIntegrationService(AppDbContext dbContext, IBackgroundJobClient backgroundJobs)
        {
            _dbContext = dbContext;
            _backgroundJobs = backgroundJobs;
        }

        public async Task<ProjectIntegration> ConfigureIntegrationAsync(Guid projectId, string providerName, string configJson, bool isActive, int syncIntervalMinutes)
        {
            var integration = new ProjectIntegration
            {
                ProjectId = projectId,
                ProviderName = providerName,
                ConfigJson = configJson,
                IsActive = isActive,
                SyncIntervalMinutes = syncIntervalMinutes,
                LastSyncAt = null
            };

            _dbContext.ProjectIntegrations.Add(integration);
            await _dbContext.SaveChangesAsync();

            // Set up a recurring sync schedule or a background run if needed
            return integration;
        }

        public async Task TriggerSyncAsync(Guid projectId, Guid integrationId)
        {
            var integration = await _dbContext.ProjectIntegrations.FindAsync(integrationId);
            if (integration == null || integration.ProjectId != projectId)
            {
                throw new ArgumentException("Integration not found.");
            }

            if (!integration.IsActive)
            {
                throw new InvalidOperationException("Integration is inactive.");
            }

            // Enqueue Hangfire background job
            _backgroundJobs.Enqueue<IProjectIntegrationService>(x => x.PerformSyncAsync(projectId, integrationId));
            await Task.CompletedTask;
        }

        public async Task PerformSyncAsync(Guid projectId, Guid integrationId)
        {
            var integration = await _dbContext.ProjectIntegrations.FindAsync(integrationId);
            if (integration == null) return;

            Console.WriteLine($"[ProjectIntegrationService] Started sync for provider: {integration.ProviderName}, Project: {projectId}");

            // Simulate network latency or actual API calls
            await Task.Delay(500);

            // Update status
            integration.LastSyncAt = DateTime.UtcNow;
            await _dbContext.SaveChangesAsync();

            Console.WriteLine($"[ProjectIntegrationService] Finished sync for provider: {integration.ProviderName}, Project: {projectId}");
        }
    }
}
