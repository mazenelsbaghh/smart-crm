using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record AutonomyEnvelopeInput(Guid OfferId, Guid DestinationId, decimal DailyCap, decimal? PeriodCap,
    string PeriodCapKind, string Currency, decimal SafetyReservePercent, decimal MaximumIncreasePercent, int CooldownHours,
    string[] IncludedCountries, string[] ExcludedCountries, int MinimumAge, string[] RequiredLanguages,
    string[] CustomAudienceExclusions, string ReportingTimezoneIana, DateTime? StartsAtUtc, DateTime? EndsAtUtc);

public sealed class AutonomyEnvelopeService(AppDbContext db, AdvertisingAuditService audit)
{
    public async Task<AutonomyEnvelope> CreateAsync(Guid projectId, Guid actorUserId, AutonomyEnvelopeInput input,
        CancellationToken cancellationToken = default)
    {
        var connection = await db.AdvertisingConnections.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready, cancellationToken)
            ?? throw new AdvertisingException("ADS_CONNECTION_NOT_READY", "A ready Meta connection is required.", 409);
        var destination = await db.AdvertisingWhatsAppDestinations.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == input.DestinationId && x.State == AuthorizedDestinationState.Eligible, cancellationToken)
            ?? throw new AdvertisingException("ADS_WHATSAPP_DESTINATION_REQUIRED", "An eligible WhatsApp destination is required.", 409);
        var offerExists = await db.AdvertisingOffers.AnyAsync(x => x.ProjectId == projectId && x.Id == input.OfferId && x.State == "Eligible", cancellationToken);
        if (!offerExists) throw new AdvertisingException("ADS_OFFER_REQUIRED", "An eligible offer is required.", 409);
        if (!string.Equals(input.Currency, connection.AccountCurrency, StringComparison.OrdinalIgnoreCase))
            throw new AdvertisingException("ADS_CURRENCY_MISMATCH", "Envelope currency must match the Meta ad account currency.");

        var definition = new AutonomyEnvelopeDefinition(input.DailyCap, input.PeriodCap, input.PeriodCapKind,
            input.Currency.ToUpperInvariant(), input.IncludedCountries, input.ExcludedCountries, input.MinimumAge,
            input.RequiredLanguages, input.CustomAudienceExclusions, input.ReportingTimezoneIana);
        var validation = AutonomyEnvelopePolicy.Validate(definition);
        if (!validation.IsValid) throw new AdvertisingException(validation.Errors[0], "The autonomy envelope is invalid.");

        var nextVersion = await db.AutonomyEnvelopes.Where(x => x.ProjectId == projectId).Select(x => (uint?)x.Version).MaxAsync(cancellationToken) ?? 0;
        var envelope = new AutonomyEnvelope
        {
            ProjectId = projectId,
            ConnectionId = connection.Id,
            OfferId = input.OfferId,
            DailyCap = input.DailyCap,
            PeriodCap = input.PeriodCap,
            PeriodCapKind = input.PeriodCapKind,
            Currency = input.Currency.ToUpperInvariant(),
            SafetyReservePercent = input.SafetyReservePercent,
            MaximumIncreasePercent = input.MaximumIncreasePercent,
            CooldownHours = Math.Max(1, input.CooldownHours),
            AllowedCountriesJson = JsonSerializer.Serialize(input.IncludedCountries),
            HardIncludedGeoJson = JsonSerializer.Serialize(input.IncludedCountries),
            HardExcludedGeoJson = JsonSerializer.Serialize(input.ExcludedCountries),
            HardMinimumAge = input.MinimumAge,
            HardRequiredLanguagesJson = JsonSerializer.Serialize(input.RequiredLanguages),
            HardCustomAudienceExclusionsJson = JsonSerializer.Serialize(input.CustomAudienceExclusions),
            AudienceBoundaryHash = AutonomyEnvelopePolicy.DefinitionHash(definition),
            DefinitionHash = AutonomyEnvelopePolicy.DefinitionHash(definition),
            ReportingTimezoneIana = input.ReportingTimezoneIana,
            TimezoneSource = "OwnerAuthorized",
            TimezoneSnapshotAtUtc = DateTime.UtcNow,
            PlacementPolicy = PlacementPolicy.DynamicEligibleMeta,
            StartsAtUtc = input.StartsAtUtc ?? DateTime.UtcNow,
            EndsAtUtc = input.EndsAtUtc,
            State = EnvelopeState.Draft,
            AuthorizedByUserId = actorUserId,
            Version = nextVersion + 1
        };
        db.AutonomyEnvelopes.Add(envelope);
        db.AdvertisingOfferDestinationGrants.Add(new EnvelopeOfferDestinationGrant
        {
            ProjectId = projectId, EnvelopeId = envelope.Id, OfferId = input.OfferId, DestinationId = destination.Id,
            AllowedFromUtc = envelope.StartsAtUtc, AllowedUntilUtc = envelope.EndsAtUtc, MaximumDailyAllocation = input.DailyCap
        });
        audit.Append(new(projectId, "Authority", "EnvelopeCreated", nameof(AutonomyEnvelope), envelope.Id.ToString(),
            "User", actorUserId, JsonSerializer.Serialize(new { envelope.DailyCap, envelope.PeriodCap, envelope.Currency, envelope.DefinitionHash }), envelope.Id));
        await db.SaveChangesAsync(cancellationToken);
        return envelope;
    }

    public async Task<AutonomyEnvelope> ActivateAsync(Guid projectId, Guid envelopeId, uint expectedVersion,
        CancellationToken cancellationToken = default)
    {
        var envelope = await db.AutonomyEnvelopes.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == envelopeId, cancellationToken)
            ?? throw new AdvertisingException("ADS_ENVELOPE_NOT_FOUND", "Envelope not found.", 404);
        if (envelope.Version != expectedVersion) throw new AdvertisingException("ADS_ENVELOPE_VERSION_CONFLICT", "Envelope changed; reload and retry.", 412);
        var destinationReady = await db.AdvertisingOfferDestinationGrants.AnyAsync(grant => grant.ProjectId == projectId && grant.EnvelopeId == envelopeId && grant.State == "Active", cancellationToken);
        if (!destinationReady) throw new AdvertisingException("ADS_ENVELOPE_GRANT_REQUIRED", "Offer and destination authority is missing.", 409);
        var old = await db.AutonomyEnvelopes.Where(x => x.ProjectId == projectId && x.State == EnvelopeState.Active && x.Id != envelopeId).ToListAsync(cancellationToken);
        foreach (var previous in old) { previous.State = EnvelopeState.Suspended; previous.Version++; }
        envelope.State = EnvelopeState.Active;
        envelope.AuthorizedAtUtc = DateTime.UtcNow;
        envelope.Version++;
        audit.Append(new(projectId, "Authority", "EnvelopeActivated", nameof(AutonomyEnvelope), envelope.Id.ToString(),
            "User", envelope.AuthorizedByUserId, "{}", envelope.Id));
        await db.SaveChangesAsync(cancellationToken);
        return envelope;
    }

    public async Task SuspendAsync(Guid projectId, Guid envelopeId, uint expectedVersion, CancellationToken cancellationToken = default)
    {
        var envelope = await db.AutonomyEnvelopes.SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == envelopeId, cancellationToken)
            ?? throw new AdvertisingException("ADS_ENVELOPE_NOT_FOUND", "Envelope not found.", 404);
        if (envelope.Version != expectedVersion) throw new AdvertisingException("ADS_ENVELOPE_VERSION_CONFLICT", "Envelope changed; reload and retry.", 412);
        envelope.State = EnvelopeState.Suspended;
        envelope.Version++;
        await db.SaveChangesAsync(cancellationToken);
    }
}
