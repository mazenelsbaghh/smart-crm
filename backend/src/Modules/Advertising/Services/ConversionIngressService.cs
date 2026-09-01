using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record ConversionWebhookPayload(
    int SchemaVersion,
    string ExternalEventId,
    string EventType,
    DateTime OccurredAtUtc,
    decimal? Value,
    string? Currency,
    ConversionCustomer? Customer,
    ConversionAttribution? Attribution,
    ConversionPrivacy? Privacy,
    string? OriginalExternalEventId,
    ConversionBusinessAggregate? BusinessAggregate = null,
    string? JourneyLocation = null,
    Guid? ConversationId = null);
public sealed record ConversionBusinessAggregate(string? Type, string? Id);
public sealed record ConversionCustomer(string? ExternalId, string? Email, string? Phone);
public sealed record ConversionAttribution(string? Fbclid, string? SessionId, string? AdExternalId, string? CtwaClid = null);
public sealed record ConversionPrivacy(string? ConsentState, string? LegalBasis);
public sealed record ConversionIngressResult(Guid ConversionId, bool Duplicate);

public sealed class ConversionIngressService(AppDbContext db, AdvertisingSecretVault vault)
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConversationStarted", "Lead", "QualifiedLead", "InitiateCheckout", "OrderCreated", "OrderDelivered",
        "Signup", "TrialStarted", "SubscriptionStarted", "SubscriptionRenewed", "EnrollmentPaid",
        "BookingConfirmed", "AttendanceConfirmed", "Purchase", "Refund", "Cancellation",
        "Chargeback", "Absent", "Churn", "DealWon", "DealLost"
    };

    public async Task<ConversionIngressResult> IngestAsync(Guid projectId, string sourceKey, long timestamp, string signature, string rawBody, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (Math.Abs((now - signedAt).TotalMinutes) > 5) throw new UnauthorizedAccessException("Webhook timestamp is outside replay window.");
        var source = await db.AdvertisingWebhookSources.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.SourceKey == sourceKey && x.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("Webhook source is not active.");
        var currentValid = ConversionSecurity.Verify(vault.Unprotect(source.ProtectedSigningSecret), timestamp, rawBody, signature);
        var previousValid = !currentValid && source.OverlapEndsAtUtc >= DateTime.UtcNow && source.PreviousProtectedSigningSecret is not null
            && ConversionSecurity.Verify(vault.Unprotect(source.PreviousProtectedSigningSecret), timestamp, rawBody, signature);
        if (!currentValid && !previousValid)
            throw new UnauthorizedAccessException("Webhook signature is invalid.");

        var payload = JsonSerializer.Deserialize<ConversionWebhookPayload>(rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid conversion payload.");
        if (payload.SchemaVersion is not (1 or 2) || string.IsNullOrWhiteSpace(payload.ExternalEventId) || !Supported.Contains(payload.EventType))
            throw new InvalidOperationException("Unsupported conversion payload.");
        if (payload.SchemaVersion == 2 && (string.IsNullOrWhiteSpace(payload.BusinessAggregate?.Type)
            || string.IsNullOrWhiteSpace(payload.BusinessAggregate.Id) || string.IsNullOrWhiteSpace(payload.JourneyLocation)))
            throw new InvalidOperationException("Schema v2 requires businessAggregate and journeyLocation.");
        var allowed = JsonSerializer.Deserialize<string[]>(source.AllowedEventTypesJson) ?? [];
        if (allowed.Length > 0 && !allowed.Contains("*") && !allowed.Contains(payload.EventType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Event type is not allowed for this webhook source.");
        if (payload.Value is not null && string.IsNullOrWhiteSpace(payload.Currency)) throw new InvalidOperationException("Currency is required with value.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        var existingSource = await db.AdvertisingConversionSourceEvents.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.SourceSystem == sourceKey && x.ExternalEventId == payload.ExternalEventId, cancellationToken);
        if (existingSource is not null)
        {
            if (!string.Equals(existingSource.PayloadHash, hash, StringComparison.Ordinal)) throw new InvalidOperationException("Duplicate event ID has a conflicting payload.");
            var existingKey = CanonicalKey(sourceKey, existingSource.BusinessAggregateType,
                existingSource.BusinessAggregateId, payload.ExternalEventId);
            var existingConversion = await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.CanonicalKey == existingKey, cancellationToken);
            return new(existingConversion.Id, true);
        }

        if (!string.IsNullOrWhiteSpace(payload.OriginalExternalEventId) && ConversionSecurity.IsCorrection(payload.EventType))
        {
            var aggregateType = AggregateType(payload);
            var aggregateId = AggregateId(payload, payload.OriginalExternalEventId);
            var canonicalKey = CanonicalKey(sourceKey, aggregateType, aggregateId, payload.OriginalExternalEventId);
            var original = await db.AdvertisingConversions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.CanonicalKey == canonicalKey, cancellationToken);
            if (original is null)
            {
                original = new CanonicalConversion
                {
                    ProjectId = projectId, CanonicalKey = canonicalKey, EventType = "PendingBase",
                    OccurredAtUtc = payload.OccurredAtUtc.ToUniversalTime(), CustomerReference = payload.Customer?.ExternalId,
                    CurrentValue = 0m, Currency = payload.Currency?.ToUpperInvariant(), ConsentState = ConsentState.Unknown,
                    State = ConversionState.Observed, TruthState = "PendingBase", CorrectionState = CorrectionState.PendingBase
                };
                db.AdvertisingConversions.Add(original);
            }
            var delta = payload.Value is not null ? -Math.Abs(payload.Value.Value) : 0m;
            db.AdvertisingConversionSourceEvents.Add(SourceEvent(projectId, payload, hash, sourceKey, aggregateType, aggregateId,
                original.Value is null ? "PendingBase" : "Accepted"));
            db.AdvertisingConversionAdjustments.Add(new ConversionAdjustment { ProjectId = projectId, ConversionId = original.Id, ExternalEventId = payload.ExternalEventId, Kind = payload.EventType, ValueDelta = delta, Reason = payload.EventType, OccurredAtUtc = payload.OccurredAtUtc.ToUniversalTime() });
            if (original.Value is not null)
            {
                original.CurrentValue = payload.Value is null ? 0m : Math.Max(0m, (original.CurrentValue ?? original.Value.Value) + delta);
                original.State = ConversionState.Corrected; original.TruthState = "Corrected"; original.CorrectionState = CorrectionState.Corrected;
            }
            original.SourceHistoryJson = AppendHistory(original.SourceHistoryJson, payload, sourceKey);
            source.LastUsedAtUtc = DateTime.UtcNow; await db.SaveChangesAsync(cancellationToken);
            return new(original.Id, false);
        }

        var consent = Enum.TryParse<ConsentState>(payload.Privacy?.ConsentState, true, out var parsedConsent) ? parsedConsent : ConsentState.Unknown;
        string? protectedMatch = null;
        if (payload.Customer is not null && ConversionSecurity.CanUseMatchData(consent, payload.Customer.Email, payload.Customer.Phone))
            protectedMatch = vault.Protect(JsonSerializer.Serialize(new { payload.Customer.Email, payload.Customer.Phone }));

        ManagedAdvertisement? attributedAd = null;
        if (!string.IsNullOrWhiteSpace(payload.Attribution?.AdExternalId))
            attributedAd = await db.ManagedAdvertisements.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.AdExternalId == payload.Attribution.AdExternalId, cancellationToken);
        var conversionAggregateType = AggregateType(payload);
        var conversionAggregateId = AggregateId(payload, payload.ExternalEventId);
        var conversionKey = CanonicalKey(sourceKey, conversionAggregateType, conversionAggregateId, payload.ExternalEventId);
        var conversion = await db.AdvertisingConversions.IgnoreQueryFilters().SingleOrDefaultAsync(item =>
            item.ProjectId == projectId && item.CanonicalKey == conversionKey, cancellationToken);
        if (conversion is null)
        {
            conversion = new CanonicalConversion { ProjectId = projectId, CanonicalKey = conversionKey };
            db.AdvertisingConversions.Add(conversion);
        }
        conversion.EventType = payload.EventType; conversion.OccurredAtUtc = payload.OccurredAtUtc.ToUniversalTime();
        conversion.CustomerReference = payload.Customer?.ExternalId; conversion.VisitorReference = payload.Attribution?.SessionId;
        conversion.Value = payload.Value; conversion.CurrentValue = payload.Value; conversion.Currency = payload.Currency?.ToUpperInvariant();
        conversion.AdvertisementId = attributedAd?.Id; conversion.CreativeId = attributedAd?.CreativeId;
        var journeyTouch = payload.ConversationId is null ? null : await db.AdvertisingAttributionTouches.IgnoreQueryFilters()
            .Where(item => item.ProjectId == projectId && item.ConversationId == payload.ConversationId
                && item.TouchedAtUtc <= payload.OccurredAtUtc.ToUniversalTime() && item.ProtectedCtwaClid != null)
            .OrderByDescending(item => item.TouchedAtUtc).ThenByDescending(item => item.Id)
            .FirstOrDefaultAsync(cancellationToken);
        conversion.AttributionMethod = journeyTouch?.Method ?? (attributedAd is not null ? "ProviderAdId"
            : payload.Attribution?.Fbclid is not null ? "FacebookClick" : "Unattributed");
        conversion.AttributionTouchId = journeyTouch?.Id;
        conversion.AttributionState = journeyTouch is not null || attributedAd is not null
            ? AttributionState.Attributed : AttributionState.Unattributed;
        conversion.ConsentState = consent; conversion.LegalBasis = payload.Privacy?.LegalBasis;
        conversion.ProtectedMatchData = protectedMatch; conversion.State = ConversionState.Verified; conversion.TruthState = "Verified";
        var pendingAdjustments = await db.AdvertisingConversionAdjustments.IgnoreQueryFilters().Where(item => item.ProjectId == projectId && item.ConversionId == conversion.Id).ToListAsync(cancellationToken);
        if (pendingAdjustments.Count > 0)
        {
            var fullReversal = pendingAdjustments.Any(item => item.ValueDelta == 0m && item.Kind is "Refund" or "Cancellation" or "Chargeback");
            conversion.CurrentValue = fullReversal ? 0m : Math.Max(0m, (payload.Value ?? 0m) + pendingAdjustments.Sum(item => item.ValueDelta));
            conversion.State = ConversionState.Corrected; conversion.TruthState = "Corrected"; conversion.CorrectionState = CorrectionState.Corrected;
        }
        conversion.SourceHistoryJson = AppendHistory(conversion.SourceHistoryJson, payload, sourceKey);
        db.AdvertisingConversionSourceEvents.Add(SourceEvent(projectId, payload, hash, sourceKey,
            conversionAggregateType, conversionAggregateId, "Accepted"));
        if (attributedAd is not null || payload.Attribution?.Fbclid is not null)
            db.AdvertisingAttributionTouches.Add(new AdvertisingAttributionTouch { ProjectId = projectId, ConversionId = conversion.Id, AdvertisementId = attributedAd?.Id, Method = conversion.AttributionMethod, ExternalClickIdHash = payload.Attribution?.Fbclid is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.Attribution.Fbclid))).ToLowerInvariant(), TouchedAtUtc = payload.OccurredAtUtc.ToUniversalTime() });
        source.LastUsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new(conversion.Id, false);
    }

    private static ConversionSourceEvent SourceEvent(Guid projectId, ConversionWebhookPayload payload, string hash, string sourceKey,
        string aggregateType, string aggregateId, string processingState) => new()
    {
        ProjectId = projectId, SourceSystem = sourceKey, ExternalEventId = payload.ExternalEventId,
        SchemaVersion = payload.SchemaVersion, PayloadHash = hash,
        NormalizedPayloadJson = JsonSerializer.Serialize(payload), EventType = payload.EventType,
        BusinessAggregateType = aggregateType, BusinessAggregateId = aggregateId,
        JourneyLocation = payload.JourneyLocation ?? (payload.Attribution?.SessionId is not null ? "Website" : "FirstParty"),
        OccurredAtUtc = payload.OccurredAtUtc.ToUniversalTime(), Value = payload.Value, Currency = payload.Currency,
        ConsentEvidenceJson = JsonSerializer.Serialize(payload.Privacy), ReceivedAtUtc = DateTime.UtcNow,
        ProcessingState = processingState
    };

    private static string AggregateType(ConversionWebhookPayload payload) =>
        string.IsNullOrWhiteSpace(payload.BusinessAggregate?.Type) ? "ExternalConversion" : payload.BusinessAggregate.Type.Trim();

    private static string AggregateId(ConversionWebhookPayload payload, string fallback) =>
        string.IsNullOrWhiteSpace(payload.BusinessAggregate?.Id) ? fallback : payload.BusinessAggregate.Id.Trim();

    private static string CanonicalKey(string sourceKey, string aggregateType, string aggregateId, string fallback) =>
        aggregateType == "ExternalConversion" ? $"{sourceKey}:{(string.IsNullOrWhiteSpace(aggregateId) ? fallback : aggregateId)}"
            : $"{sourceKey}:{aggregateType}:{aggregateId}";

    private static string AppendHistory(string existing, ConversionWebhookPayload payload, string sourceKey)
    {
        var history = JsonSerializer.Deserialize<List<object>>(existing) ?? [];
        history.Add(new { sourceKey, payload.ExternalEventId, payload.EventType, payload.OccurredAtUtc, payload.Value, payload.Currency });
        return JsonSerializer.Serialize(history);
    }
}
