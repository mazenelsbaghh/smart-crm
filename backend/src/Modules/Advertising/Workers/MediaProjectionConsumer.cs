using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Jobs;
using Shared.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class MediaProjectionConsumer(AppDbContext db, IBackgroundJobClient jobs) : IIntegrationEventHandler<AdvertisingProjectAssetChanged>
{
    public async Task HandleAsync(AdvertisingProjectAssetChanged e)
    {
        const string consumer = nameof(MediaProjectionConsumer);
        if (await db.IntegrationInboxReceipts.AnyAsync(x => x.EventId == e.Id && x.Consumer == consumer)) return;
        var creative = await db.AdvertisingCreatives.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.ProjectId == e.ProjectId && x.SourceAssetId == e.AssetId);
        if (string.Equals(e.Action, "Delete", StringComparison.OrdinalIgnoreCase))
        {
            if (creative is not null) { creative.EligibilityState = CreativeEligibility.Stale; creative.PolicyState = "SourceDeleted"; }
        }
        else
        {
            var mediaType = MapMediaType(e.ContentType);
            var eligible = mediaType is not null && e.FileSize is > 0 and <= 1_000_000_000 && e.RightsState == "Owned";
            if (creative is null)
            {
                creative = new AdvertisingCreative { ProjectId = e.ProjectId, SourceType = CreativeSourceType.ProjectAsset, SourceAssetId = e.AssetId };
                db.AdvertisingCreatives.Add(creative);
            }
            creative.SourceHash = e.FileHash;
            creative.SourceVersion++;
            creative.SourceStoragePath = e.StoragePath;
            creative.SourceContentType = e.ContentType;
            creative.MediaType = mediaType ?? CreativeMediaType.Image;
            creative.RightsState = e.RightsState;
            creative.PolicyState = eligible ? "FormatAndRightsPassed" : "RejectedFormatRightsOrSize";
            creative.EligibilityState = eligible ? CreativeEligibility.Eligible : CreativeEligibility.Ineligible;
            creative.LastAnalyzedAtUtc = DateTime.UtcNow;
        }
        db.IntegrationInboxReceipts.Add(new IntegrationInboxReceipt { EventId = e.Id, Consumer = consumer, ProcessedAtUtc = DateTime.UtcNow });
        await db.SaveChangesAsync();
        if (creative?.EligibilityState == CreativeEligibility.Eligible)
            jobs.Enqueue<CreativeVariantJob>(job => job.GenerateAsync(e.ProjectId, creative.Id, CancellationToken.None));
    }

    private static CreativeMediaType? MapMediaType(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/png" or "image/webp" => CreativeMediaType.Image,
        "video/mp4" or "video/quicktime" or "video/webm" => CreativeMediaType.Video,
        _ => null
    };
}
