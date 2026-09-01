using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class WhatsAppAttributionObservationConsumer(AppDbContext db) : IIntegrationEventHandler<WhatsAppAttributionObserved>
{
    private const string Consumer = nameof(WhatsAppAttributionObservationConsumer);

    public async Task HandleAsync(WhatsAppAttributionObserved message)
    {
        if (await db.IntegrationInboxReceipts.AnyAsync(item => item.EventId == message.Id && item.Consumer == Consumer)) return;
        var identifierState = Enum.TryParse<ReferralIdentifierState>(message.IdentifierState, true, out var parsed)
            ? parsed : ReferralIdentifierState.Invalid;
        var observation = await db.AdvertisingAttributionObservations.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.ProjectId == message.ProjectId && item.DestinationId == message.DestinationId
            && item.MessageExternalId == message.MessageExternalId, CancellationToken.None);
        if (observation is null)
        {
            observation = new WhatsAppAttributionObservation
            {
                ProjectId = message.ProjectId, ConversationId = message.ConversationId, CustomerId = message.CustomerId,
                JourneyKey = message.CustomerId.ToString("N"), MessageExternalId = message.MessageExternalId,
                MessageOccurredAtUtc = message.MessageOccurredAtUtc, DestinationId = message.DestinationId,
                DestinationVersion = message.DestinationVersion, ReceivingIdentityExternalId = message.DestinationId.ToString("N"),
                IdentifierState = identifierState, ProtectedCtwaClid = NullIfEmpty(message.ProtectedCtwaClid),
                ProtectionPurpose = identifierState == ReferralIdentifierState.CtwaClid ? "Advertising.Referral.CtwaClid.v1" : null,
                CtwaClidHash = NullIfEmpty(message.CtwaClidHash), OpaquePayloadHash = NullIfEmpty(message.OpaquePayloadHash),
                ProviderAdExternalId = NullIfEmpty(message.ProviderAdExternalId), PayloadHash = message.Id.ToString("N"),
                GatewayType = message.GatewayType
            };
            db.AdvertisingAttributionObservations.Add(observation);
            var context = await db.AdvertisingAttributionContexts.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                item.ProjectId == message.ProjectId && item.ConversationId == message.ConversationId);
            if (context is null)
            {
                context = new WhatsAppAttributionContext
                {
                    ProjectId = message.ProjectId, ConversationId = message.ConversationId, CustomerId = message.CustomerId,
                    JourneyKey = message.CustomerId.ToString("N"), DestinationId = message.DestinationId,
                    FirstObservedAtUtc = message.MessageOccurredAtUtc, LastObservedAtUtc = message.MessageOccurredAtUtc
                };
                db.AdvertisingAttributionContexts.Add(context);
            }
            context.LastObservedAtUtc = message.MessageOccurredAtUtc > context.LastObservedAtUtc ? message.MessageOccurredAtUtc : context.LastObservedAtUtc;
            context.ObservationCount++;
            if (identifierState == ReferralIdentifierState.CtwaClid)
            {
                context.ValidReferralCount++;
                if (string.Equals(message.GatewayType, "CloudApi", StringComparison.OrdinalIgnoreCase))
                {
                    var destination = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                        item.ProjectId == message.ProjectId && item.Id == message.DestinationId);
                    if (destination?.WhatsAppIntegrationMode is WhatsAppIntegrationMode.CloudApi or WhatsAppIntegrationMode.CloudApiCoexistence)
                    {
                        destination.ReferralCaptureState = ReferralProofState.CtwaClidObserved;
                        destination.ReferralProofAtUtc = message.MessageOccurredAtUtc;
                    }
                }
                db.AdvertisingAttributionTouches.Add(new AdvertisingAttributionTouch
                {
                    ProjectId = message.ProjectId, AttributionContextId = context.Id, ObservationId = observation.Id,
                    ConversationId = message.ConversationId, JourneyKey = context.JourneyKey, DestinationId = message.DestinationId,
                    Method = "CtwaClid", ExternalClickIdHash = message.CtwaClidHash,
                    ProtectedCtwaClid = message.ProtectedCtwaClid, ProviderAdExternalId = NullIfEmpty(message.ProviderAdExternalId),
                    EligibilityEvidenceJson = "{\"source\":\"WhatsAppReferral\",\"eligible\":true}", TouchedAtUtc = message.MessageOccurredAtUtc
                });
            }
        }
        db.IntegrationInboxReceipts.Add(new IntegrationInboxReceipt
        {
            ProjectId = message.ProjectId, EventId = message.Id, Consumer = Consumer, State = "Processed", ProcessedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
