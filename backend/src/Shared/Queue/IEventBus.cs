using Shared.Events;
using System.Threading.Tasks;

namespace Shared.Queue
{
    public interface IEventBus
    {
        Task PublishAsync<T>(T @event) where T : IntegrationEvent;
        void Subscribe<T, THandler>() 
            where T : IntegrationEvent 
            where THandler : IIntegrationEventHandler<T>;
    }

    public interface IIntegrationEventHandler<in T> where T : IntegrationEvent
    {
        Task HandleAsync(T @event);
    }
}
