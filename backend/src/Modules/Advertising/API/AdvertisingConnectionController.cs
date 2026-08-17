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
    MetaAdsClient meta,
    AdvertisingSecretVault vault) : AdvertisingControllerBase(authorization)
{
    [HttpPost("facebook/oauth/start")]
    public async Task<IActionResult> StartOAuth(Guid projectId)
    {
        if (!CanManage(projectId) || UserId is null) return Forbid();
        return Ok(await oauth.StartAsync(projectId, UserId.Value));
    }

    [HttpGet("facebook/resources")]
    public async Task<IActionResult> Resources(Guid projectId, [FromQuery] string? adAccountId, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        var connection = await db.AdvertisingConnections.AsNoTracking().FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        if (connection is null) return Conflict(new { code = "ADS_OAUTH_REQUIRED" });
        var token = connection.ProtectedAccessToken is null ? "mock" : vault.Unprotect(connection.ProtectedAccessToken);
        return Ok(await meta.DiscoverAsync(token, adAccountId, cancellationToken));
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
            .Where(x => x.ProjectId == projectId)
            .Select(x => new { x.Id, x.Provider, x.AdAccountExternalId, x.PageExternalId, x.DatasetExternalId, state = x.State.ToString(), x.AccountCurrency, x.AccountTimezone, x.ExpiresAtUtc, x.LastValidatedAtUtc, x.LastSyncAtUtc, x.LastErrorCode, x.LastErrorSummary })
            .FirstOrDefaultAsync(cancellationToken);
        return Ok(connection);
    }

    [HttpPut("connection")]
    public async Task<IActionResult> SelectConnection(Guid projectId, [FromBody] SelectConnectionRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.AdAccountId) || string.IsNullOrWhiteSpace(request.PageId))
            return UnprocessableEntity(new { code = "ADS_RESOURCES_REQUIRED", message = "Ad Account and Page are required." });

        var connection = await db.AdvertisingConnections.FirstOrDefaultAsync(x => x.ProjectId == projectId, cancellationToken);
        if (connection?.ProtectedAccessToken is null) return Conflict(new { code = "ADS_OAUTH_REQUIRED" });
        var token = vault.Unprotect(connection.ProtectedAccessToken);
        var catalog = await meta.DiscoverAsync(token, request.AdAccountId, cancellationToken);
        var selectedAccount = catalog.AdAccounts.SingleOrDefault(x => x.Id == request.AdAccountId);
        if (selectedAccount is null || !catalog.Pages.Any(x => x.Id == request.PageId) ||
            (!string.IsNullOrWhiteSpace(request.DatasetId) && !catalog.Datasets.Any(x => x.Id == request.DatasetId)))
            return UnprocessableEntity(new { code = "ADS_RESOURCES_NOT_ELIGIBLE", message = "Selected Facebook resources are not mutually accessible." });
        if (selectedAccount.Status is not null and not 1)
            return UnprocessableEntity(new { code = "ADS_ACCOUNT_INACTIVE", message = "Selected Ad Account is not active." });
        connection.AdAccountExternalId = request.AdAccountId.Trim();
        connection.PageExternalId = request.PageId.Trim();
        connection.DatasetExternalId = string.IsNullOrWhiteSpace(request.DatasetId) ? null : request.DatasetId.Trim();
        connection.AccountCurrency = selectedAccount.Currency ?? request.Currency.Trim().ToUpperInvariant();
        connection.AccountTimezone = selectedAccount.Timezone ?? request.Timezone.Trim();
        connection.State = AdvertisingConnectionState.Ready;
        connection.LastValidatedAtUtc = DateTime.UtcNow;
        connection.LastErrorCode = null;
        connection.LastErrorSummary = null;
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { connection.Id, state = connection.State.ToString() });
    }

    [HttpGet("envelope")]
    public async Task<IActionResult> GetEnvelope(Guid projectId, CancellationToken cancellationToken)
    {
        if (!CanRead(projectId)) return Forbid();
        return Ok(await db.AutonomyEnvelopes.AsNoTracking().Where(x => x.ProjectId == projectId)
            .OrderByDescending(x => x.CreatedAt).Select(x => new { x.Id, x.DailyCap, x.PeriodCap, x.PeriodCapKind, x.Currency, x.SafetyReservePercent, x.MaximumIncreasePercent, x.CooldownHours, x.AllowedCountriesJson, x.StartsAtUtc, x.EndsAtUtc, state = x.State.ToString(), x.Version }).FirstOrDefaultAsync(cancellationToken));
    }

    [HttpPut("envelope")]
    public async Task<IActionResult> PutEnvelope(Guid projectId, [FromBody] PutEnvelopeRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        if (request.DailyCap <= 0 || request.SafetyReservePercent is < 0 or > 50 || request.MaximumIncreasePercent is < 0 or > 50)
            return UnprocessableEntity(new { code = "ADS_INVALID_ENVELOPE", message = "Budget and safety limits are invalid." });
        var connection = await db.AdvertisingConnections.FirstOrDefaultAsync(x => x.ProjectId == projectId && x.State == AdvertisingConnectionState.Ready, cancellationToken);
        if (connection is null) return Conflict(new { code = "ADS_CONNECTION_NOT_READY", message = "Connect Facebook resources first." });

        var active = await db.AutonomyEnvelopes.Where(x => x.ProjectId == projectId && x.State == EnvelopeState.Active).ToListAsync(cancellationToken);
        foreach (var old in active) old.State = EnvelopeState.Revoked;
        var envelope = new AutonomyEnvelope
        {
            ProjectId = projectId, ConnectionId = connection.Id, DailyCap = request.DailyCap, PeriodCap = request.PeriodCap,
            Currency = connection.AccountCurrency ?? request.Currency.ToUpperInvariant(), SafetyReservePercent = request.SafetyReservePercent,
            MaximumIncreasePercent = request.MaximumIncreasePercent, CooldownHours = Math.Max(1, request.CooldownHours),
            AllowedCountriesJson = System.Text.Json.JsonSerializer.Serialize(request.AllowedCountries.Distinct(StringComparer.OrdinalIgnoreCase)),
            StartsAtUtc = request.StartsAtUtc ?? DateTime.UtcNow, EndsAtUtc = request.EndsAtUtc,
            AuthorizedByUserId = UserId ?? Guid.Empty, AuthorizedAtUtc = DateTime.UtcNow, State = EnvelopeState.Active
        };
        db.AutonomyEnvelopes.Add(envelope);
        await db.SaveChangesAsync(cancellationToken);
        return Ok(new { envelope.Id, state = envelope.State.ToString(), envelope.DailyCap, envelope.Currency });
    }
}

public sealed record SelectConnectionRequest(string AdAccountId, string PageId, string? DatasetId, string Currency = "EGP", string Timezone = "Africa/Cairo");
public sealed record PutEnvelopeRequest(decimal DailyCap, decimal? PeriodCap, string Currency, decimal SafetyReservePercent, decimal MaximumIncreasePercent, int CooldownHours, string[] AllowedCountries, DateTime? StartsAtUtc, DateTime? EndsAtUtc);
