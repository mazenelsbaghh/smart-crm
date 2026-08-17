namespace Shared.Events;

public sealed class KnowledgePublishedChangedEvent : IntegrationEvent
{
    public Guid ProjectId { get; init; }
    public Guid DocumentId { get; init; }
    public int Version { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Content { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
}
