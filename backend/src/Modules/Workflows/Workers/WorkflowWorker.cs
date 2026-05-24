using Microsoft.Extensions.DependencyInjection;
using Modules.Workflows.Services;
using Shared.Events;
using Shared.Queue;
using Shared.Security;
using System;
using System.Threading.Tasks;

namespace Modules.Workflows.Workers
{
    public class WorkflowWorker : IIntegrationEventHandler<CustomerTagAddedEvent>
    {
        private readonly IServiceProvider _serviceProvider;

        public WorkflowWorker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task HandleAsync(CustomerTagAddedEvent @event)
        {
            Console.WriteLine($"[WorkflowWorker] Received CustomerTagAddedEvent for Customer: {@event.CustomerId}, Tag: {@event.Tag}");

            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetProjectId(@event.ProjectId);

            var workflowEngine = scope.ServiceProvider.GetRequiredService<IWorkflowEngine>();
            await workflowEngine.ProcessEventAsync(@event.ProjectId, "CustomerTagAdded", @event.CustomerId, @event);
        }
    }
}
