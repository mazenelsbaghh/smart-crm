using Shared.Events;

namespace Shared.Queue;

public abstract class AdvertisingIntegrationEvent : IntegrationEvent
{
    public int SchemaVersion { get; init; } = 1;
    public Guid ProjectId { get; init; }
}

public sealed class AdvertisingKnowledgeChanged : AdvertisingIntegrationEvent
{
    public Guid KnowledgeDocumentId { get; init; }
    public string State { get; init; } = string.Empty;
}

public sealed class AdvertisingProjectAssetChanged : AdvertisingIntegrationEvent
{
    public Guid AssetId { get; init; }
    public string Action { get; init; } = "Upsert";
    public string ContentType { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string RightsState { get; init; } = "Owned";
}

public sealed class AdvertisingDealOutcomeChanged : AdvertisingIntegrationEvent
{
    public Guid DealId { get; init; }
    public Guid CustomerId { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public decimal Value { get; init; }
    public string Currency { get; init; } = "EGP";
}

public sealed class AdvertisingBookingOutcomeChanged : AdvertisingIntegrationEvent
{
    public Guid BookingId { get; init; }
    public Guid CustomerId { get; init; }
    public bool IsPaid { get; init; }
    public bool IsAttended { get; init; }
    public decimal Value { get; init; }
    public string Currency { get; init; } = "EGP";
}

public sealed class AdvertisingQualifiedMessageChanged : AdvertisingIntegrationEvent
{
    public Guid ConversationId { get; init; }
    public Guid CustomerId { get; init; }
    public string Classification { get; init; } = string.Empty;
}

public sealed class AdvertisingProjectLifecycleChanged : AdvertisingIntegrationEvent
{
    public string State { get; init; } = string.Empty;
}
