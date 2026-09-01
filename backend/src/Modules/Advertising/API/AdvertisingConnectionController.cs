using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;
using Modules.Advertising.Infrastructure.Facebook;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager")]
public sealed class AdvertisingConnectionController(
    IProjectAuthorizationService authorization,
    AppDbContext db,
    AdvertisingReadinessService readiness,
    FacebookAdsOAuthService oauth,
    AutonomyEnvelopeService envelopes,
    AdvertisingDisconnectService disconnects) : AdvertisingControllerBase(authorization)
{
    [HttpPost("facebook/oauth/start")]
    public async Task<IActionResult> StartOAuth(Guid projectId)
    {
        if (!CanManage(projectId) || UserId is null) return Forbid();
        _ = RequireIdempotencyKey();
        return Ok(await oauth.StartAsync(projectId, UserId.Value));
    }

    [HttpGet("facebook/resources")]
    public async Task<IActionResult> Resources(Guid projectId, [FromQuery] string? adAccountId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        return Ok(await readiness.DiscoverAsync(projectId, adAccountId, cancellationToken));
    }

    [HttpGet("capabilities")]
    public async Task<IActionResult> Capabilities(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingCapabilitySnapshots.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CheckedAtUtc).Take(20)
            .Select(x => new { x.Id, x.DestinationId, x.GraphApiVersion, state = x.State.ToString(), x.CheckedAtUtc, x.ExpiresAtUtc, x.ObjectivesJson, x.OptimizationGoalsJson, x.BidStrategiesJson, x.PlacementEligibilityJson, x.ValidationSupportJson, x.ProviderTraceId, x.FailureCode })
            .ToListAsync(cancellationToken));
    }

    [HttpGet("destinations")]
    public async Task<IActionResult> Destinations(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AdvertisingWhatsAppDestinations.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.LastValidatedAtUtc)
            .Select(x => new { x.Id, x.WhatsAppAccountId, x.WabaExternalId, x.PhoneNumberExternalId, x.DisplayPhoneE164, x.PageExternalId, x.DatasetExternalId, integrationMode = x.WhatsAppIntegrationMode.ToString(), state = x.State.ToString(), x.Version, x.LastValidatedAtUtc, x.LastErrorCode })
            .ToListAsync(cancellationToken));
    }
    [HttpGet("readiness")]
    public async Task<IActionResult> GetReadiness(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await readiness.GetAsync(projectId, cancellationToken));
    }

    [HttpGet("connection")]
    public async Task<IActionResult> GetConnection(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var connection = await db.AdvertisingConnections.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        if (connection is null) return Ok(null);
        var destination = await db.AdvertisingWhatsAppDestinations.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.ConnectionId == connection.Id
                && x.State != AuthorizedDestinationState.Revoked)
            .OrderByDescending(x => x.LastValidatedAtUtc)
            .Select(x => new { x.PhoneNumberExternalId, x.WhatsAppAccountId })
            .FirstOrDefaultAsync(cancellationToken);
        return Ok(new
        {
            connection.Id,
            connection.Provider,
            connection.AdAccountExternalId,
            connection.PageExternalId,
            connection.DatasetExternalId,
            connection.WabaExternalId,
            phoneNumberExternalId = destination?.PhoneNumberExternalId,
            whatsAppAccountId = destination?.WhatsAppAccountId,
            state = connection.State.ToString(),
            connection.AccountCurrency,
            connection.AccountTimezone,
            connection.AccountTimezoneIana,
            connection.ExpiresAtUtc,
            connection.LastValidatedAtUtc,
            connection.LastSyncAtUtc,
            connection.LastErrorCode,
            connection.LastErrorSummary,
            integrationMode = connection.WhatsAppIntegrationMode.ToString(),
            connection.Version
        });
    }

    [HttpPut("connection")]
    public async Task<IActionResult> SelectConnection(Guid projectId, [FromBody] SelectConnectionRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var gatewayMode = request.IntegrationMode == WhatsAppIntegrationMode.BaileysObservedExperimental;
        if (string.IsNullOrWhiteSpace(request.AdAccountId) || string.IsNullOrWhiteSpace(request.PageId)
            || !gatewayMode && (string.IsNullOrWhiteSpace(request.WabaId)
                || string.IsNullOrWhiteSpace(request.PhoneNumberId) || string.IsNullOrWhiteSpace(request.DatasetId)))
            return UnprocessableEntity(new { code = "ADS_RESOURCES_REQUIRED", message = gatewayMode
                ? "Ad Account and Page are required; the WhatsApp phone is verified from the live Gateway session."
                : "Ad Account, Page, WABA, phone and Dataset are required." });
        return Ok(await readiness.AuthorizeDestinationAsync(projectId,
            new(request.AdAccountId, request.PageId, request.WabaId, request.PhoneNumberId, request.DatasetId,
                request.IntegrationMode, request.WhatsAppAccountId), cancellationToken));
    }

    [HttpGet("envelope")]
    public async Task<IActionResult> GetEnvelope(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AutonomyEnvelopes.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.State == EnvelopeState.Active).ThenByDescending(x => x.CreatedAt)
            .Select(x => new { x.Id, x.DailyCap, x.PeriodCap, x.PeriodCapKind, x.Currency, x.SafetyReservePercent,
                x.MaximumIncreasePercent, x.CooldownHours, x.AllowedCountriesJson, x.HardExcludedGeoJson,
                x.HardMinimumAge, x.HardRequiredLanguagesJson, x.StartsAtUtc, x.EndsAtUtc,
                state = x.State.ToString(), x.Version }).FirstOrDefaultAsync(cancellationToken));
    }

    [HttpPut("envelope")]
    public async Task<IActionResult> PutEnvelope(Guid projectId, [FromBody] PutEnvelopeRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId) || UserId is null) return Forbid();
        _ = RequireIdempotencyKey();
        var envelope = await envelopes.CreateAsync(projectId, UserId.Value,
            new(request.OfferId, request.DestinationId, request.DailyCap, request.PeriodCap, request.PeriodCapKind,
                request.Currency, request.SafetyReservePercent, request.MaximumIncreasePercent, request.CooldownHours,
                request.AllowedCountries, request.ExcludedCountries, request.MinimumAge, request.RequiredLanguages,
                request.CustomAudienceExclusions, request.ReportingTimezoneIana, request.StartsAtUtc, request.EndsAtUtc), cancellationToken);
        return Ok(new { envelope.Id, state = envelope.State.ToString(), envelope.DailyCap, envelope.Currency, envelope.Version });
    }

    [HttpPost("envelope/{envelopeId:guid}/activate")]
    public async Task<IActionResult> ActivateEnvelope(Guid projectId, Guid envelopeId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        _ = RequireIdempotencyKey();
        var envelope = await envelopes.ActivateAsync(projectId, envelopeId, checked((uint)RequireIfMatch()), cancellationToken);
        return Ok(new { envelope.Id, state = envelope.State.ToString(), envelope.Version });
    }

    [HttpDelete("connection/{connectionId:guid}")]
    public async Task<IActionResult> Disconnect(Guid projectId, Guid connectionId, [FromBody] DisconnectConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!CanManage(projectId) || UserId is null) return Forbid();
        _ = RequireIdempotencyKey();
        _ = RequireIfMatch();
        var operation = await disconnects.RequestAsync(projectId, connectionId, UserId.Value, request.Mode,
            request.ContinuingSpendAcknowledgedAtUtc, cancellationToken);
        return AcceptedOperation(projectId, operation.Id, operation.Id, operation.Phase.ToString());
    }

    [HttpGet("disconnect-operations/{operationId:guid}")]
    public async Task<IActionResult> DisconnectStatus(Guid projectId, Guid operationId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        var operation = await db.AdvertisingDisconnectOperations.AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProjectId == projectId && x.Id == operationId, cancellationToken);
        if (operation is null) return NotFound(new { code = "ADS_DISCONNECT_OPERATION_NOT_FOUND" });
        var targets = await db.AdvertisingDisconnectTargets.AsNoTracking()
            .Where(x => x.ProjectId == projectId && x.DisconnectOperationId == operationId)
            .Select(x => new { x.Id, x.TargetType, x.TargetId, x.DesiredState, x.ReadBackState, x.CompletedAtUtc, x.FailureCode })
            .ToListAsync(cancellationToken);
        return Ok(new { operation.Id, mode = operation.Mode.ToString(), phase = operation.Phase.ToString(), operation.RequestedAtUtc,
            operation.CompletedAtUtc, operation.LastErrorCode, operation.RecoveryInstruction, targets });
    }
}

public sealed record SelectConnectionRequest(string AdAccountId, string PageId, string WabaId = "", string PhoneNumberId = "", string DatasetId = "",
    WhatsAppIntegrationMode IntegrationMode = WhatsAppIntegrationMode.CloudApiCoexistence, Guid? WhatsAppAccountId = null);
public sealed record PutEnvelopeRequest(Guid OfferId, Guid DestinationId, decimal DailyCap, decimal? PeriodCap, string PeriodCapKind,
    string Currency, decimal SafetyReservePercent, decimal MaximumIncreasePercent, int CooldownHours, string[] AllowedCountries,
    string[] ExcludedCountries, int MinimumAge, string[] RequiredLanguages, string[] CustomAudienceExclusions,
    string ReportingTimezoneIana, DateTime? StartsAtUtc, DateTime? EndsAtUtc);
public sealed record DisconnectConnectionRequest(DisconnectMode? Mode, DateTime? ContinuingSpendAcknowledgedAtUtc);
