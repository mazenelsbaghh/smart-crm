using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Shared.Infrastructure;

namespace Modules.Advertising.Services;

public sealed record WebhookSourceSecretResult(AdvertisingWebhookSource Source, string SigningSecret);

public sealed class AdvertisingWebhookSourceService(AppDbContext db, AdvertisingSecretVault vault)
{
    public async Task<WebhookSourceSecretResult> CreateAsync(Guid projectId, string sourceKey, string[] allowedEvents,
        CancellationToken cancellationToken = default)
    {
        sourceKey = sourceKey.Trim();
        if (sourceKey.Length is < 1 or > 80) throw new AdvertisingException("ADS_WEBHOOK_SOURCE_INVALID", "Source key is invalid.", 422);
        if (await db.AdvertisingWebhookSources.IgnoreQueryFilters().AnyAsync(item => item.ProjectId == projectId && item.SourceKey == sourceKey, cancellationToken))
            throw new AdvertisingException("ADS_SOURCE_EXISTS", "Webhook source already exists.", 409);
        var secret = NewSecret();
        var source = new AdvertisingWebhookSource
        {
            ProjectId = projectId, SourceKey = sourceKey, ProtectedSigningSecret = vault.Protect(secret),
            AllowedEventTypesJson = System.Text.Json.JsonSerializer.Serialize(allowedEvents.Length == 0 ? ["*"] : allowedEvents),
            State = WebhookSourceState.Active, IsActive = true
        };
        db.AdvertisingWebhookSources.Add(source); await db.SaveChangesAsync(cancellationToken);
        return new(source, secret);
    }

    public async Task<WebhookSourceSecretResult> RotateAsync(Guid projectId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.AdvertisingWebhookSources.IgnoreQueryFilters().SingleAsync(item => item.ProjectId == projectId && item.Id == sourceId, cancellationToken);
        if (!source.IsActive) throw new AdvertisingException("ADS_WEBHOOK_SOURCE_REVOKED", "Webhook source is revoked.", 409);
        var secret = NewSecret();
        source.PreviousProtectedSigningSecret = source.ProtectedSigningSecret;
        source.ProtectedSigningSecret = vault.Protect(secret); source.Version++;
        source.State = WebhookSourceState.Rotating; source.RotatedAtUtc = DateTime.UtcNow;
        source.OverlapEndsAtUtc = DateTime.UtcNow.AddMinutes(15);
        await db.SaveChangesAsync(cancellationToken);
        return new(source, secret);
    }

    public async Task RevokeAsync(Guid projectId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var source = await db.AdvertisingWebhookSources.IgnoreQueryFilters().SingleAsync(item => item.ProjectId == projectId && item.Id == sourceId, cancellationToken);
        source.State = WebhookSourceState.Revoked; source.IsActive = false; source.RevokedAtUtc = DateTime.UtcNow;
        source.PreviousProtectedSigningSecret = null; source.ProtectedSigningSecret = string.Empty;
        await db.SaveChangesAsync(cancellationToken);
    }

    private static string NewSecret() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
}
