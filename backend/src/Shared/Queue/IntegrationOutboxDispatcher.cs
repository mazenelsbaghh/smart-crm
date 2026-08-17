using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Shared.Domain;
using Shared.Events;
using Shared.Infrastructure;
using Hangfire;

namespace Shared.Queue;

public static class IntegrationOutbox
{
    public static void Enqueue<T>(AppDbContext db, T message, int schemaVersion = 1) where T : IntegrationEvent
    {
        db.IntegrationOutboxMessages.Add(new IntegrationOutboxMessage
        {
            EventId = message.Id,
            EventType = typeof(T).Name,
            SchemaVersion = schemaVersion,
            PayloadJson = JsonSerializer.Serialize(message),
            OccurredAtUtc = message.OccurredOn
        });
    }
}

public sealed class IntegrationOutboxDispatcher(AppDbContext db, IEventBus eventBus, ILogger<IntegrationOutboxDispatcher> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 50)]
    public async Task DispatchAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var messages = await db.IntegrationOutboxMessages
            .Where(x => x.PublishedAtUtc == null && (x.NextAttemptAtUtc == null || x.NextAttemptAtUtc <= now))
            .OrderBy(x => x.OccurredAtUtc).Take(100).ToListAsync(cancellationToken);

        foreach (var message in messages)
        {
            try
            {
                await PublishAsync(message);
                message.PublishedAtUtc = DateTime.UtcNow;
                message.LastErrorCode = null;
            }
            catch (Exception ex)
            {
                message.AttemptCount++;
                message.LastErrorCode = ex.GetType().Name;
                message.NextAttemptAtUtc = DateTime.UtcNow.AddSeconds(Math.Min(900, Math.Pow(2, Math.Min(9, message.AttemptCount))));
                logger.LogWarning("Outbox publish failed for event {EventId} type {EventType}: {ErrorCode}", message.EventId, message.EventType, message.LastErrorCode);
            }
        }
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task PublishAsync(IntegrationOutboxMessage message) => message.EventType switch
    {
        nameof(AdvertisingProjectAssetChanged) => Publish<AdvertisingProjectAssetChanged>(message),
        nameof(AdvertisingDealOutcomeChanged) => Publish<AdvertisingDealOutcomeChanged>(message),
        nameof(AdvertisingBookingOutcomeChanged) => Publish<AdvertisingBookingOutcomeChanged>(message),
        nameof(AdvertisingQualifiedMessageChanged) => Publish<AdvertisingQualifiedMessageChanged>(message),
        nameof(AdvertisingProjectLifecycleChanged) => Publish<AdvertisingProjectLifecycleChanged>(message),
        _ => throw new InvalidOperationException($"Unsupported outbox event type '{message.EventType}'.")
    };

    private Task Publish<T>(IntegrationOutboxMessage message) where T : IntegrationEvent =>
        eventBus.PublishAsync(JsonSerializer.Deserialize<T>(message.PayloadJson) ?? throw new InvalidOperationException("Invalid outbox payload."));
}
