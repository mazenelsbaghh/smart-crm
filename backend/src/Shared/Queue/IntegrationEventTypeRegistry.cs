using System.Reflection;
using System.Text.Json;
using Shared.Events;

namespace Shared.Queue;

public sealed class IntegrationEventTypeRegistry
{
    private readonly IReadOnlyDictionary<string, Type> _types;

    public IntegrationEventTypeRegistry()
    {
        var contractedTypes = typeof(AdvertisingIntegrationEvent).Assembly
            .GetTypes()
            .Where(type => !type.IsAbstract && typeof(IntegrationEvent).IsAssignableFrom(type))
            .Select(type => (Type: type, Contract: type.GetCustomAttribute<IntegrationEventContractAttribute>()))
            .Where(item => item.Contract is not null)
            .ToArray();
        _types = contractedTypes
            .SelectMany(item => new[]
            {
                new KeyValuePair<string, Type>(item.Contract!.Name, item.Type),
                new KeyValuePair<string, Type>(item.Type.Name, item.Type)
            })
            .GroupBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Value, StringComparer.Ordinal);
    }

    public Type Resolve(string eventType) => _types.TryGetValue(eventType, out var type)
        ? type
        : throw new InvalidOperationException($"Unsupported integration event type '{eventType}'.");

    public IntegrationEvent Deserialize(string eventType, int schemaVersion, string payloadJson)
    {
        var type = Resolve(eventType);
        var contract = type.GetCustomAttribute<IntegrationEventContractAttribute>()!;
        var isLegacyAlias = string.Equals(eventType, type.Name, StringComparison.Ordinal) && schemaVersion == 1;
        if (!isLegacyAlias && contract.SchemaVersion != schemaVersion)
            throw new InvalidOperationException($"Unsupported schema version {schemaVersion} for '{eventType}'.");

        return (IntegrationEvent?)JsonSerializer.Deserialize(payloadJson, type)
            ?? throw new InvalidOperationException($"Invalid payload for '{eventType}'.");
    }

    public static (string Name, int SchemaVersion) Describe(Type eventType)
    {
        var contract = eventType.GetCustomAttribute<IntegrationEventContractAttribute>();
        return contract is null
            ? (eventType.Name, 1)
            : (contract.Name, contract.SchemaVersion);
    }
}

public static class IntegrationEventBusExtensions
{
    private static readonly MethodInfo PublishMethod = typeof(IEventBus)
        .GetMethods()
        .Single(method => method.Name == nameof(IEventBus.PublishAsync) && method.IsGenericMethodDefinition);

    public static Task PublishAsync(this IEventBus eventBus, IntegrationEvent integrationEvent)
    {
        var task = PublishMethod.MakeGenericMethod(integrationEvent.GetType()).Invoke(eventBus, [integrationEvent]);
        return task as Task ?? throw new InvalidOperationException("The integration event publisher returned no task.");
    }
}
