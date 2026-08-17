using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;
using Shared.Security;

namespace Modules.Advertising.API;

[Route("api/projects/{projectId:guid}/ad-manager/conversion-sources")]
public sealed class AdvertisingConversionSourcesController(IProjectAuthorizationService authorization, AppDbContext db, AdvertisingSecretVault vault) : AdvertisingControllerBase(authorization)
{
    [HttpPost]
    public async Task<IActionResult> Create(Guid projectId, [FromBody] CreateWebhookSourceRequest request, CancellationToken cancellationToken)
    {
        if (!CanManage(projectId)) return Forbid();
        if (string.IsNullOrWhiteSpace(request.SourceKey) || request.SourceKey.Length > 80) return UnprocessableEntity();
        if (await db.AdvertisingWebhookSources.AnyAsync(x => x.ProjectId == projectId && x.SourceKey == request.SourceKey, cancellationToken)) return Conflict(new { code = "ADS_SOURCE_EXISTS" });
        var secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var source = new AdvertisingWebhookSource { ProjectId = projectId, SourceKey = request.SourceKey.Trim(), ProtectedSigningSecret = vault.Protect(secret), AllowedEventTypesJson = "[\"*\"]" };
        db.AdvertisingWebhookSources.Add(source);
        await db.SaveChangesAsync(cancellationToken);
        return Created($"/api/projects/{projectId}/ad-manager/conversion-sources/{source.Id}", new { source.Id, source.SourceKey, signingSecret = secret, shownOnce = true });
    }
}

public sealed record CreateWebhookSourceRequest(string SourceKey);

[ApiController]
[AllowAnonymous]
[Route("api/integrations/ad-manager/{projectId:guid}/conversions/{sourceKey}")]
public sealed class AdvertisingConversionWebhookController(ConversionIngressService ingress) : ControllerBase
{
    [HttpPost]
    [RequestSizeLimit(64 * 1024)]
    public async Task<IActionResult> Ingest(Guid projectId, string sourceKey, CancellationToken cancellationToken)
    {
        if (!long.TryParse(Request.Headers["X-Ads-Timestamp"], out var timestamp)) return Unauthorized(new { code = "ADS_SIGNATURE_REQUIRED" });
        var signature = Request.Headers["X-Ads-Signature"].ToString();
        using var reader = new StreamReader(Request.Body);
        var rawBody = await reader.ReadToEndAsync(cancellationToken);
        try
        {
            var result = await ingress.IngestAsync(projectId, sourceKey, timestamp, signature, rawBody, cancellationToken);
            return Accepted(new { result.ConversionId, result.Duplicate });
        }
        catch (UnauthorizedAccessException) { return Unauthorized(new { code = "ADS_INVALID_SIGNATURE" }); }
        catch (InvalidOperationException ex) when (ex.Message.Contains("conflicting", StringComparison.OrdinalIgnoreCase)) { return Conflict(new { code = "ADS_DUPLICATE_CONFLICT" }); }
        catch (InvalidOperationException ex) { return UnprocessableEntity(new { code = "ADS_INVALID_CONVERSION", message = ex.Message }); }
    }
}
