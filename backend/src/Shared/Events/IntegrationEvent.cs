using System;

namespace Shared.Events
{
    public abstract class IntegrationEvent
    {
        public Guid Id { get; init; } = Guid.NewGuid();
        public DateTime OccurredOn { get; init; } = DateTime.UtcNow;
    }
}
