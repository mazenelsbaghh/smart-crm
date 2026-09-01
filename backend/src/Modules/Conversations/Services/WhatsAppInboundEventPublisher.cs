using Modules.Advertising.Domain;
using Shared.Infrastructure;
using Shared.Queue;
using Shared.Security;

namespace Modules.Conversations.Services;

public sealed record InboundAdvertisingContext(string IdentifierState, string? CtwaClid, string? ProviderAdId,
    string? OpaquePayloadHash, string GatewayType);

public sealed class WhatsAppInboundEventPublisher(AppDbContext db, IAdvertisingReferralProtector protector)
{
    public void PublishObservation(Guid projectId, Guid conversationId, Guid customerId, Guid destinationId,
        long destinationVersion, string messageExternalId, DateTime occurredAtUtc, InboundAdvertisingContext context,
        bool isFirstConversationMessage)
    {
        if (!isFirstConversationMessage && string.Equals(context.IdentifierState, "Missing", StringComparison.OrdinalIgnoreCase)) return;
        var hasIdentifier = string.Equals(context.IdentifierState, nameof(ReferralIdentifierState.CtwaClid), StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(context.CtwaClid);
        IntegrationOutbox.Enqueue(db, new WhatsAppAttributionObserved
        {
            ProjectId = projectId, SourceAggregateType = "WhatsAppMessage", SourceAggregateId = DeterministicGuid(messageExternalId),
            SourceVersion = 1, CorrelationId = conversationId, ConversationId = conversationId, CustomerId = customerId,
            DestinationId = destinationId, DestinationVersion = destinationVersion, MessageExternalId = messageExternalId,
            MessageOccurredAtUtc = occurredAtUtc, IdentifierState = hasIdentifier ? nameof(ReferralIdentifierState.CtwaClid) : context.IdentifierState,
            ProtectedCtwaClid = hasIdentifier ? protector.ProtectIdentifier(context.CtwaClid!) : string.Empty,
            CtwaClidHash = hasIdentifier ? protector.Hash(context.CtwaClid!) : string.Empty,
            OpaquePayloadHash = context.OpaquePayloadHash ?? string.Empty,
            ProviderAdExternalId = context.ProviderAdId ?? string.Empty, GatewayType = context.GatewayType,
            IsFirstConversationMessage = isFirstConversationMessage
        });
    }

    private static Guid DeterministicGuid(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes.AsSpan(0, 16));
    }
}
