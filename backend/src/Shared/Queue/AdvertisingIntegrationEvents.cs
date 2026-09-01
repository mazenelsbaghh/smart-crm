using Shared.Events;

namespace Shared.Queue;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class IntegrationEventContractAttribute(string name, int schemaVersion) : Attribute
{
    public string Name { get; } = name;
    public int SchemaVersion { get; } = schemaVersion;
}

public interface IVersionedIntegrationEvent
{
    Guid ProjectId { get; }
    string SourceAggregateType { get; }
    Guid SourceAggregateId { get; }
    long SourceVersion { get; }
    bool IsTombstone { get; }
}

public abstract class AdvertisingIntegrationEvent : IntegrationEvent, IVersionedIntegrationEvent
{
    public int SchemaVersion { get; init; } = 1;
    public Guid ProjectId { get; init; }
    public string SourceAggregateType { get; init; } = string.Empty;
    public Guid SourceAggregateId { get; init; }
    public long SourceVersion { get; init; } = 1;
    public bool IsTombstone { get; init; }
    public Guid CorrelationId { get; init; }
}

[IntegrationEventContract("KnowledgePublishedChanged.v2", 2)]
public sealed class AdvertisingKnowledgeChanged : AdvertisingIntegrationEvent
{
    public Guid KnowledgeDocumentId { get; init; }
    public string State { get; init; } = string.Empty;
    public string RevisionHash { get; init; } = string.Empty;
    public string SafeFactsJson { get; init; } = "{}";
    public string AffectedOfferKeysJson { get; init; } = "[]";
}

[IntegrationEventContract("ProjectAssetChanged.v2", 2)]
public sealed class AdvertisingProjectAssetChanged : AdvertisingIntegrationEvent
{
    public Guid AssetId { get; init; }
    public string Action { get; init; } = "Upsert";
    public string ContentType { get; init; } = string.Empty;
    public string FileHash { get; init; } = string.Empty;
    public string StoragePath { get; init; } = string.Empty;
    public long FileSize { get; init; }
    public string RightsState { get; init; } = "Owned";
    public string BrandMetadataJson { get; init; } = "{}";
}

[IntegrationEventContract("ProjectAdvertisingContextChanged.v1", 1)]
public sealed class ProjectAdvertisingContextChanged : AdvertisingIntegrationEvent
{
    public string LifecycleState { get; init; } = "Active";
    public string ReportingTimezoneIana { get; init; } = "Africa/Cairo";
    public long AiConfigurationVersion { get; init; }
}

[IntegrationEventContract("ProjectAiConfigurationChanged.v1", 1)]
public sealed class ProjectAiConfigurationChanged : AdvertisingIntegrationEvent
{
    public long ConfigurationVersion { get; init; }
    public string AllowedModel { get; init; } = string.Empty;
    public string SettingsHash { get; init; } = string.Empty;
}

[IntegrationEventContract("CustomerAdvertisingConsentChanged.v1", 1)]
public sealed class CustomerAdvertisingConsentChanged : AdvertisingIntegrationEvent
{
    public Guid CustomerId { get; init; }
    public string ConsentState { get; init; } = "Unknown";
    public string LegalBasis { get; init; } = string.Empty;
    public DateTime EffectiveAtUtc { get; init; }
}

[IntegrationEventContract("OfferAvailabilityChanged.v1", 1)]
public sealed class OfferAvailabilityChanged : AdvertisingIntegrationEvent
{
    public Guid OfferId { get; init; }
    public int? DailyCapacity { get; init; }
    public int? CurrentCapacity { get; init; }
    public string AvailabilityState { get; init; } = "Available";
    public DateTime EffectiveAtUtc { get; init; }
}

[IntegrationEventContract("WhatsAppInboundMessageReceived.v1", 1)]
public sealed class WhatsAppInboundMessageReceived : AdvertisingIntegrationEvent
{
    public Guid DestinationId { get; init; }
    public long DestinationVersion { get; init; }
    public string ProviderMessageId { get; init; } = string.Empty;
    public DateTime MessageOccurredAtUtc { get; init; }
    public string ProtectedSenderReference { get; init; } = string.Empty;
    public string NormalizedContentJson { get; init; } = "{}";
    public string ProtectedReferralJson { get; init; } = "{}";
}

[IntegrationEventContract("WhatsAppAttributionObserved.v1", 1)]
public sealed class WhatsAppAttributionObserved : AdvertisingIntegrationEvent
{
    public Guid ConversationId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid DestinationId { get; init; }
    public long DestinationVersion { get; init; }
    public string MessageExternalId { get; init; } = string.Empty;
    public DateTime MessageOccurredAtUtc { get; init; }
    public string IdentifierState { get; init; } = "Missing";
    public string ProtectedCtwaClid { get; init; } = string.Empty;
    public string CtwaClidHash { get; init; } = string.Empty;
    public string OpaquePayloadHash { get; init; } = string.Empty;
    public string ProviderAdExternalId { get; init; } = string.Empty;
    public string GatewayType { get; init; } = string.Empty;
    public bool IsFirstConversationMessage { get; init; }
}

[IntegrationEventContract("AdvertisingAiWorkRequested.v1", 1)]
public sealed class AdvertisingAiWorkRequested : AdvertisingIntegrationEvent
{
    public Guid RequestId { get; init; }
    public Guid OwnerId { get; init; }
    public long OwnerVersion { get; init; }
    public string Purpose { get; init; } = string.Empty;
    public string InputHash { get; init; } = string.Empty;
    public string SourcedInputJson { get; init; } = "{}";
    public DateTime DeadlineUtc { get; init; }
}

[IntegrationEventContract("AdvertisingAiWorkCompleted.v1", 1)]
public sealed class AdvertisingAiWorkCompleted : AdvertisingIntegrationEvent
{
    public Guid RequestId { get; init; }
    public Guid OwnerId { get; init; }
    public long OwnerVersion { get; init; }
    public string InputHash { get; init; } = string.Empty;
    public string ModelVersion { get; init; } = string.Empty;
    public string PromptVersion { get; init; } = string.Empty;
    public string StructuredResultJson { get; init; } = "{}";
    public string FailureCode { get; init; } = string.Empty;
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }
}

[IntegrationEventContract("DealOutcomeChanged.v2", 2)]
public sealed class AdvertisingDealOutcomeChanged : AdvertisingIntegrationEvent
{
    public Guid DealId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid? ConversationId { get; init; }
    public string Outcome { get; init; } = string.Empty;
    public decimal Value { get; init; }
    public string Currency { get; init; } = "EGP";
    public DateTime OutcomeOccurredAtUtc { get; init; }
}

[IntegrationEventContract("BookingChanged.v2", 2)]
public sealed class AdvertisingBookingOutcomeChanged : AdvertisingIntegrationEvent
{
    public Guid BookingId { get; init; }
    public Guid CustomerId { get; init; }
    public Guid? ConversationId { get; init; }
    public string State { get; init; } = string.Empty;
    public bool IsPaid { get; init; }
    public bool IsAttended { get; init; }
    public decimal Value { get; init; }
    public string Currency { get; init; } = "EGP";
    public DateTime OutcomeOccurredAtUtc { get; init; }
}

[IntegrationEventContract("ConversationSalesClassificationChanged.v2", 2)]
public sealed class AdvertisingQualifiedMessageChanged : AdvertisingIntegrationEvent
{
    public Guid ConversationId { get; init; }
    public Guid CustomerId { get; init; }
    public string Classification { get; init; } = string.Empty;
    public decimal Confidence { get; init; }
    public string ClassifierVersion { get; init; } = string.Empty;
    public DateTime ClassifiedAtUtc { get; init; }
}

[IntegrationEventContract("ProjectLifecycleChanged.v1", 1)]
public sealed class AdvertisingProjectLifecycleChanged : AdvertisingIntegrationEvent
{
    public string State { get; init; } = string.Empty;
}

[IntegrationEventContract("AdvertisingWhatsAppDestinationChanged.v1", 1)]
public sealed class AdvertisingWhatsAppDestinationChanged : AdvertisingIntegrationEvent
{
    public Guid DestinationId { get; init; }
    public long DestinationVersion { get; init; }
    public string Provider { get; init; } = "Meta";
    public string WabaExternalId { get; init; } = string.Empty;
    public string PhoneNumberExternalId { get; init; } = string.Empty;
    public string IntegrationMode { get; init; } = string.Empty;
    public string State { get; init; } = "Active";
}

[IntegrationEventContract("AdvertisingAuditRecorded.v1", 1)]
public sealed class AdvertisingAuditRecorded : AdvertisingIntegrationEvent
{
    public Guid AuditRecordId { get; init; }
    public string Action { get; init; } = string.Empty;
    public string TargetType { get; init; } = string.Empty;
    public Guid? TargetId { get; init; }
}
