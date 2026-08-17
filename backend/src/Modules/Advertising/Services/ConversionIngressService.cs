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
    string? OriginalExternalEventId);
public sealed record ConversionCustomer(string? ExternalId, string? Email, string? Phone);
public sealed record ConversionAttribution(string? Fbclid, string? SessionId, string? AdExternalId);
public sealed record ConversionPrivacy(string? ConsentState, string? LegalBasis);
public sealed record ConversionIngressResult(Guid ConversionId, bool Duplicate);

public sealed class ConversionIngressService(AppDbContext db, AdvertisingSecretVault vault)
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        "Lead", "QualifiedLead", "Signup", "TrialStarted", "SubscriptionStarted", "SubscriptionRenewed",
        "EnrollmentPaid", "BookingConfirmed", "AttendanceConfirmed", "Purchase", "Refund", "Cancellation",
        "Chargeback", "Absent", "Churn", "DealWon", "DealLost"
    };

    public async Task<ConversionIngressResult> IngestAsync(Guid projectId, string sourceKey, long timestamp, string signature, string rawBody, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var signedAt = DateTimeOffset.FromUnixTimeSeconds(timestamp);
        if (Math.Abs((now - signedAt).TotalMinutes) > 5) throw new UnauthorizedAccessException("Webhook timestamp is outside replay window.");
        var source = await db.AdvertisingWebhookSources.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.SourceKey == sourceKey && x.IsActive, cancellationToken)
            ?? throw new UnauthorizedAccessException("Webhook source is not active.");
        var secret = vault.Unprotect(source.ProtectedSigningSecret);
        if (!ConversionSecurity.Verify(secret, timestamp, rawBody, signature))
            throw new UnauthorizedAccessException("Webhook signature is invalid.");

        var payload = JsonSerializer.Deserialize<ConversionWebhookPayload>(rawBody, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Invalid conversion payload.");
        if (payload.SchemaVersion != 1 || string.IsNullOrWhiteSpace(payload.ExternalEventId) || !Supported.Contains(payload.EventType))
            throw new InvalidOperationException("Unsupported conversion payload.");
        var allowed = JsonSerializer.Deserialize<string[]>(source.AllowedEventTypesJson) ?? [];
        if (allowed.Length > 0 && !allowed.Contains("*") && !allowed.Contains(payload.EventType, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Event type is not allowed for this webhook source.");
        if (payload.Value is not null && string.IsNullOrWhiteSpace(payload.Currency)) throw new InvalidOperationException("Currency is required with value.");

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawBody))).ToLowerInvariant();
        var existingSource = await db.AdvertisingConversionSourceEvents.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.SourceSystem == sourceKey && x.ExternalEventId == payload.ExternalEventId, cancellationToken);
        if (existingSource is not null)
        {
            if (!string.Equals(existingSource.PayloadHash, hash, StringComparison.Ordinal)) throw new InvalidOperationException("Duplicate event ID has a conflicting payload.");
            var existingConversion = await db.AdvertisingConversions.IgnoreQueryFilters().SingleAsync(x => x.ProjectId == projectId && x.CanonicalKey == $"{sourceKey}:{payload.ExternalEventId}", cancellationToken);
            return new(existingConversion.Id, true);
        }

        if (!string.IsNullOrWhiteSpace(payload.OriginalExternalEventId) && ConversionSecurity.IsCorrection(payload.EventType))
        {
            var original = await db.AdvertisingConversions.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == projectId && x.CanonicalKey == $"{sourceKey}:{payload.OriginalExternalEventId}", cancellationToken)
                ?? throw new InvalidOperationException("Correction references an unknown conversion.");
            var delta = payload.Value is not null ? -Math.Abs(payload.Value.Value) : -(original.CurrentValue ?? 0m);
            db.AdvertisingConversionSourceEvents.Add(new ConversionSourceEvent { ProjectId = projectId, SourceSystem = sourceKey, ExternalEventId = payload.ExternalEventId, SchemaVersion = 1, PayloadHash = hash, ReceivedAtUtc = DateTime.UtcNow });
            db.AdvertisingConversionAdjustments.Add(new ConversionAdjustment { ProjectId = projectId, ConversionId = original.Id, ExternalEventId = payload.ExternalEventId, Kind = payload.EventType, ValueDelta = delta, Reason = payload.EventType, OccurredAtUtc = payload.OccurredAtUtc.ToUniversalTime() });
            original.CurrentValue = Math.Max(0m, (original.CurrentValue ?? 0m) + delta); original.State = ConversionState.Corrected;
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
        var conversion = new CanonicalConversion
        {
            ProjectId = projectId, CanonicalKey = $"{sourceKey}:{payload.ExternalEventId}", EventType = payload.EventType,
            OccurredAtUtc = payload.OccurredAtUtc.ToUniversalTime(), CustomerReference = payload.Customer?.ExternalId,
            VisitorReference = payload.Attribution?.SessionId, Value = payload.Value, CurrentValue = payload.Value,
            Currency = payload.Currency?.ToUpperInvariant(), AdvertisementId = attributedAd?.Id, CreativeId = attributedAd?.CreativeId,
            AttributionMethod = attributedAd is not null ? "ProviderAdId" : payload.Attribution?.Fbclid is not null ? "FacebookClick" : "Unattributed",
            ConsentState = consent, LegalBasis = payload.Privacy?.LegalBasis, ProtectedMatchData = protectedMatch, State = ConversionState.Verified
        };
        db.AdvertisingConversionSourceEvents.Add(new ConversionSourceEvent { ProjectId = projectId, SourceSystem = sourceKey, ExternalEventId = payload.ExternalEventId, SchemaVersion = 1, PayloadHash = hash, ReceivedAtUtc = DateTime.UtcNow });
        db.AdvertisingConversions.Add(conversion);
        if (attributedAd is not null || payload.Attribution?.Fbclid is not null)
            db.AdvertisingAttributionTouches.Add(new AdvertisingAttributionTouch { ProjectId = projectId, ConversionId = conversion.Id, AdvertisementId = attributedAd?.Id, Method = conversion.AttributionMethod, ExternalClickIdHash = payload.Attribution?.Fbclid is null ? null : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload.Attribution.Fbclid))).ToLowerInvariant(), TouchedAtUtc = payload.OccurredAtUtc.ToUniversalTime() });
        source.LastUsedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        return new(conversion.Id, false);
    }
}
