using Shared.Queue;

namespace Shared.Events;

[IntegrationEventContract("BookingLastMessageReadRequested.v1", 1)]
public sealed class BookingLastMessageReadRequested : IntegrationEvent
{
    public Guid ProjectId { get; init; }
    public Guid BookingId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid ConversationId { get; init; }
    public Guid MessageId { get; init; }
}
