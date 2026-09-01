using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Infrastructure.Facebook;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.Advertising.Jobs;

public static class ConversionConsentPolicy
{
    public static bool CanDeliver(ConsentState state, string? legalBasis) =>
        state == ConsentState.Granted || state == ConsentState.NotRequired && !string.IsNullOrWhiteSpace(legalBasis);

    public static (ConsentState State, string? LegalBasis) ResolveCurrent(
        ConsentState storedState, string? storedLegalBasis, CustomerAdvertisingConsentProjection? current)
    {
        if (current is null) return (storedState, storedLegalBasis);
        if (current.IsTombstoned) return (ConsentState.Denied, null);
        return (Enum.TryParse<ConsentState>(current.ConsentState, true, out var parsed)
            ? parsed : ConsentState.Unknown, current.LegalBasis);
    }
}

public sealed class ConversionDeliveryJob(AppDbContext db, MetaBusinessMessagingClient meta,
    AdvertisingSecretVault vault, IAdvertisingReferralProtector referrals)
{
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        var candidates = await db.AdvertisingConversions.IgnoreQueryFilters().Where(conversion =>
            conversion.AttributionState == AttributionState.Attributed && conversion.AttributionTouchId != null
            && (conversion.State == ConversionState.Verified || conversion.State == ConversionState.Corrected))
            .OrderBy(conversion => conversion.OccurredAtUtc).Take(100).ToListAsync(cancellationToken);
        foreach (var conversion in candidates)
        {
            var touch = await db.AdvertisingAttributionTouches.IgnoreQueryFilters().SingleAsync(item => item.Id == conversion.AttributionTouchId, cancellationToken);
            var mapping = WhatsAppJourneyEventMapper.Map(conversion.EventType);
            var identity = $"{conversion.Id:N}:{mapping.MetaMessagingEvent}:{conversion.CurrentValue}";
            var delivery = await db.AdvertisingConversionDeliveries.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
                item.ProjectId == conversion.ProjectId && item.Provider == "MetaBusinessMessaging" && item.EventIdentity == identity, cancellationToken);
            if (delivery is null)
            {
                delivery = new ConversionDelivery { ProjectId = conversion.ProjectId, ConversionId = conversion.Id,
                    EventIdentity = identity, EventName = mapping.MetaMessagingEvent ?? string.Empty, State = ConversionDeliveryState.Pending };
                db.AdvertisingConversionDeliveries.Add(delivery); await db.SaveChangesAsync(cancellationToken);
            }
            if (delivery.State is ConversionDeliveryState.Accepted or ConversionDeliveryState.Suppressed or ConversionDeliveryState.FailedTerminal) continue;
            if (delivery.NextAttemptAtUtc is { } nextAttempt && nextAttempt > DateTime.UtcNow) continue;
            var (consentState, legalBasis) = await CurrentConsentAsync(conversion, cancellationToken);
            if (!ConversionConsentPolicy.CanDeliver(consentState, legalBasis))
            {
                delivery.State = ConversionDeliveryState.Suppressed; delivery.SuppressionReason = "ADS_CONSENT_NOT_ELIGIBLE";
                await db.SaveChangesAsync(cancellationToken); continue;
            }
            if (mapping.MetaMessagingEvent is null || touch.ProtectedCtwaClid is null || touch.DestinationId is null ||
                ConversionAttributionPolicy.Route("MessagingThread", touch.ProtectedCtwaClid is not null) != ConversionDeliveryChannel.MetaBusinessMessaging)
            {
                delivery.State = ConversionDeliveryState.Suppressed; delivery.SuppressionReason = "ADS_NOT_BUSINESS_MESSAGING_EVENT";
                await db.SaveChangesAsync(cancellationToken); continue;
            }
            var destination = await db.AdvertisingWhatsAppDestinations.IgnoreQueryFilters().SingleAsync(item =>
                item.ProjectId == conversion.ProjectId && item.Id == touch.DestinationId, cancellationToken);
            var connection = await db.AdvertisingConnections.IgnoreQueryFilters().SingleAsync(item =>
                item.ProjectId == conversion.ProjectId && item.Id == destination.ConnectionId && item.ProtectedAccessToken != null, cancellationToken);
            var attemptNumber = await db.AdvertisingConversionDeliveryAttempts.IgnoreQueryFilters().CountAsync(item => item.DeliveryId == delivery.Id, cancellationToken) + 1;
            if (attemptNumber > 8)
            {
                delivery.State = ConversionDeliveryState.FailedTerminal; await db.SaveChangesAsync(cancellationToken); continue;
            }
            var attempt = new ConversionDeliveryAttempt { ProjectId = conversion.ProjectId, DeliveryId = delivery.Id,
                ConversionId = conversion.Id, Provider = "MetaBusinessMessaging", AttemptNumber = attemptNumber,
                State = "Sent", AttemptedAtUtc = DateTime.UtcNow };
            db.AdvertisingConversionDeliveryAttempts.Add(attempt); await db.SaveChangesAsync(cancellationToken);
            try
            {
                var result = await meta.SendAsync(vault.Unprotect(connection.ProtectedAccessToken!), new(
                    destination.DatasetExternalId, destination.WabaExternalId,
                    referrals.UnprotectForBusinessMessaging(touch.ProtectedCtwaClid), mapping.MetaMessagingEvent,
                    delivery.EventIdentity, conversion.OccurredAtUtc, conversion.CurrentValue, conversion.Currency), cancellationToken);
                attempt.ProviderRequestId = result.ProviderRequestId; attempt.ProviderTraceId = result.ProviderTraceId;
                attempt.EventsReceived = result.EventsReceived; attempt.ResponseHash = result.ResponseHash;
                if (result.EventsReceived > 0)
                {
                    attempt.State = "Accepted"; delivery.State = ConversionDeliveryState.Accepted;
                    delivery.AcceptedAtUtc = DateTime.UtcNow; conversion.State = ConversionState.Delivered;
                }
                else throw new HttpRequestException("Meta returned zero accepted messaging events.");
            }
            catch (HttpRequestException ex)
            {
                attempt.State = "Failed"; attempt.ErrorCode = ex.StatusCode?.ToString() ?? "META_DELIVERY_FAILED";
                delivery.State = attemptNumber >= 8 ? ConversionDeliveryState.FailedTerminal : ConversionDeliveryState.RetryScheduled;
                delivery.NextAttemptAtUtc = DateTime.UtcNow.AddMinutes(Math.Min(360, Math.Pow(2, attemptNumber)));
            }
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task<(ConsentState State, string? LegalBasis)> CurrentConsentAsync(
        CanonicalConversion conversion, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(conversion.CustomerReference, out var customerId))
            return (conversion.ConsentState, conversion.LegalBasis);

        var current = await db.CustomerAdvertisingConsentProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(item => item.ProjectId == conversion.ProjectId && item.CustomerId == customerId,
                cancellationToken);
        return ConversionConsentPolicy.ResolveCurrent(conversion.ConsentState, conversion.LegalBasis, current);
    }
}
