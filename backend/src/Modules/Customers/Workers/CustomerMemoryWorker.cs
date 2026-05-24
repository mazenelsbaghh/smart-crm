using Microsoft.Extensions.DependencyInjection;
using Modules.Customers.Services;
using Shared.Events;
using Shared.Queue;
using Shared.Security;
using System;
using System.Threading.Tasks;

namespace Modules.Customers.Workers
{
    public class CustomerMemoryWorker : IIntegrationEventHandler<ConversationClosedEvent>
    {
        private readonly IServiceProvider _serviceProvider;

        public CustomerMemoryWorker(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task HandleAsync(ConversationClosedEvent @event)
        {
            Console.WriteLine($"[CustomerMemoryWorker] Received ConversationClosedEvent for Conversation: {@event.ConversationId}");

            using var scope = _serviceProvider.CreateScope();
            var tenantContext = scope.ServiceProvider.GetRequiredService<ITenantContext>();
            tenantContext.SetProjectId(@event.ProjectId);

            var memoryService = scope.ServiceProvider.GetRequiredService<ICustomerMemoryService>();
            await memoryService.UpdateMemoryFromConversationAsync(@event.ProjectId, @event.CustomerId, @event.ConversationId);
        }
    }
}
