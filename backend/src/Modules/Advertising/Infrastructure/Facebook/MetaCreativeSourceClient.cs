using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Services;
using Shared.Infrastructure;

namespace Modules.Advertising.Infrastructure.Facebook;

public sealed record MetaCreativeSource(string StableSourceId, string ProviderPostId, string MediaType,
    string? MediaExternalId, string? Message, string? PreviewUrl, DateTime? CreatedAtUtc,
    string RightsState, CreativeEligibility Eligibility, string? BlockingReason);

public sealed class MetaCreativeSourceClient(AppDbContext db, MetaAdsClient meta, AdvertisingSecretVault vault)
{
    public async Task<IReadOnlyList<MetaCreativeSource>> DiscoverAsync(Guid projectId, CancellationToken cancellationToken = default)
    {
        var connection = await db.AdvertisingConnections.AsNoTracking().SingleOrDefaultAsync(
            item => item.ProjectId == projectId && item.State == AdvertisingConnectionState.Ready && item.ProtectedAccessToken != null,
            cancellationToken) ?? throw new AdvertisingException("ADS_CONNECTION_NOT_READY", "Meta connection is not ready.", 409);
        if (string.IsNullOrWhiteSpace(connection.PageExternalId))
            throw new AdvertisingException("ADS_PAGE_REQUIRED", "A verified Page is required for WhatsApp creatives.", 409);

        var posts = await meta.GetPageContentAsync(vault.Unprotect(connection.ProtectedAccessToken), connection.PageExternalId, cancellationToken);
        return posts.Select(post =>
        {
            var supported = post.MediaType is "Image" or "Carousel" or "Video";
            var stableId = $"meta:{connection.PageExternalId}:{post.Id}:{post.MediaExternalId ?? "post"}";
            return new MetaCreativeSource(stableId, post.Id, post.MediaType, post.MediaExternalId, post.Message,
                post.MediaUrl, post.CreatedAtUtc, "PageOwned", supported ? CreativeEligibility.Eligible : CreativeEligibility.Ineligible,
                supported ? null : "ADS_CREATIVE_FORMAT_UNSUPPORTED");
        }).ToArray();
    }
}
