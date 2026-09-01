using Shared.Queue;
using Shared.Events;
using Xunit;

namespace Advertising.UnitTests;

public sealed class AdvertisingIntegrationMessagingTests
{
    [Theory]
    [InlineData(4, 4, ProjectionVersionDecision.Duplicate)]
    [InlineData(4, 3, ProjectionVersionDecision.Stale)]
    [InlineData(4, 5, ProjectionVersionDecision.Apply)]
    [InlineData(4, 7, ProjectionVersionDecision.Gap)]
    public void Projection_versions_are_monotonic(
        long currentVersion,
        long incomingVersion,
        ProjectionVersionDecision expected)
    {
        Assert.Equal(expected, ProjectionVersionGuard.Decide(currentVersion, incomingVersion));
    }

    [Fact]
    public void A_tombstone_applies_once_and_blocks_an_older_replay()
    {
        Assert.Equal(
            ProjectionVersionDecision.ApplyTombstone,
            ProjectionVersionGuard.Decide(currentVersion: 8, incomingVersion: 9, isTombstone: true));
        Assert.Equal(
            ProjectionVersionDecision.Stale,
            ProjectionVersionGuard.Decide(currentVersion: 9, incomingVersion: 8));
    }

    [Fact]
    public void Unknown_event_types_are_not_silently_ignored()
    {
        var registry = new IntegrationEventTypeRegistry();

        var error = Assert.Throws<InvalidOperationException>(() => registry.Resolve("UnknownEvent.v1"));

        Assert.Contains("UnknownEvent.v1", error.Message);
    }

    [Fact]
    public async Task Deserialized_outbox_event_is_published_as_its_runtime_contract_type()
    {
        var bus = new RecordingEventBus();
        IntegrationEvent message = new AdvertisingKnowledgeChanged();

        await IntegrationEventBusExtensions.PublishAsync(bus, message);

        Assert.Equal(typeof(AdvertisingKnowledgeChanged), bus.PublishedType);
    }

    private sealed class RecordingEventBus : IEventBus
    {
        public Type? PublishedType { get; private set; }

        public Task PublishAsync<T>(T @event) where T : IntegrationEvent
        {
            PublishedType = typeof(T);
            return Task.CompletedTask;
        }

        public void Subscribe<T, THandler>(int consumerCount = 1)
            where T : IntegrationEvent
            where THandler : IIntegrationEventHandler<T>
        {
        }
    }
}
