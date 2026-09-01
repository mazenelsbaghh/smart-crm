using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Shared.Events;

namespace Shared.Queue;

/// <summary>Development/test transport that preserves the same handler boundary without external RabbitMQ.</summary>
public sealed class InMemoryEventBus(IServiceProvider services) : IEventBus
{
    private readonly ConcurrentDictionary<Type, ConcurrentDictionary<Type, byte>> _subscriptions = new();

    public void Subscribe<T, THandler>(int consumerCount = 1)
        where T : IntegrationEvent
        where THandler : IIntegrationEventHandler<T> =>
        _subscriptions.GetOrAdd(typeof(T), _ => new()).TryAdd(typeof(THandler), 0);

    public async Task PublishAsync<T>(T @event) where T : IntegrationEvent
    {
        if (!_subscriptions.TryGetValue(typeof(T), out var handlers)) return;
        foreach (var handlerType in handlers.Keys)
        {
            await using var scope = services.CreateAsyncScope();
            var handler = (IIntegrationEventHandler<T>)scope.ServiceProvider.GetRequiredService(handlerType);
            await handler.HandleAsync(@event);
        }
    }
}
