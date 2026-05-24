using Microsoft.Extensions.DependencyInjection;
using Modules.CRM.Services;
using Shared.Events;
using Shared.Queue;
using Shared.Security;
using System;
using System.Threading.Tasks;

namespace Modules.CRM.Workers
{
    public class CRMWorker : IIntegrationEventHandler<CRMUpdateSuggestedEvent>
    {
        private readonly IServiceProvider _serviceProvider;

        public CRMWorker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task HandleAsync(CRMUpdateSuggestedEvent @event)
        {
            Console.WriteLine($"[CRMWorker] Received CRMUpdateSuggestedEvent for Customer: {@event.CustomerId}");

            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetProjectId(@event.ProjectId);

            var autoUpdateEngine = scope.ServiceProvider.GetRequiredService<ICRMAutoUpdateEngine>();
            await autoUpdateEngine.ProcessSuggestionAsync(@event);
        }
    }
}
