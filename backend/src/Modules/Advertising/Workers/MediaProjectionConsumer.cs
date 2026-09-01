using Hangfire;
using Microsoft.EntityFrameworkCore;
using Modules.Advertising.Domain;
using Modules.Advertising.Jobs;
using Shared.Domain;
using Shared.Infrastructure;
using Shared.Queue;

namespace Modules.Advertising.Workers;

public sealed class MediaProjectionConsumer(AppDbContext db, IBackgroundJobClient jobs) :
    IntegrationProjectionConsumer<AdvertisingProjectAssetChanged>(db),
    IIntegrationEventHandler<AdvertisingProjectAssetChanged>
{
    protected override string ConsumerName => nameof(MediaProjectionConsumer);

    public async Task HandleAsync(AdvertisingProjectAssetChanged e)
    {
        Guid? eligibleCreativeId = null;
        await ConsumeAsync(e, async cancellationToken =>
        {
            var media = await Db.AdvertisingMediaProjections.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.ProjectId == e.ProjectId && x.AssetId == e.AssetId, cancellationToken);
            if (media is null)
            {
                media = new AdvertisingMediaProjection { ProjectId = e.ProjectId, AssetId = e.AssetId };
                Db.AdvertisingMediaProjections.Add(media);
            }
            media.AssetVersion = e.SourceVersion;
            media.ContentType = e.ContentType;
            media.FileHash = e.FileHash;
            media.ObjectReference = e.StoragePath;
            media.FileSize = e.FileSize;
            media.RightsState = e.RightsState;
            media.BrandMetadataJson = e.BrandMetadataJson;
            media.UpdatedFromEventId = e.Id;
            media.IsTombstoned = e.IsTombstone || string.Equals(e.Action, "Delete", StringComparison.OrdinalIgnoreCase);

            var creative = await Db.AdvertisingCreatives.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.ProjectId == e.ProjectId && x.SourceAssetId == e.AssetId, cancellationToken);
            if (media.IsTombstoned)
            {
                if (creative is not null) { creative.EligibilityState = CreativeEligibility.Stale; creative.PolicyState = "SourceDeleted"; }
                return;
            }
            var mediaType = MapMediaType(e.ContentType);
            var eligible = mediaType is not null && e.FileSize is > 0 and <= 1_000_000_000 && e.RightsState == "Owned";
            if (creative is null)
            {
                creative = new AdvertisingCreative { ProjectId = e.ProjectId, SourceType = CreativeSourceType.ProjectAsset, SourceAssetId = e.AssetId };
                Db.AdvertisingCreatives.Add(creative);
            }
            creative.SourceHash = e.FileHash;
            creative.SourceVersion = checked((int)e.SourceVersion);
            creative.SourceStoragePath = e.StoragePath;
            creative.SourceContentType = e.ContentType;
            creative.MediaType = mediaType ?? CreativeMediaType.Image;
            creative.RightsState = e.RightsState;
            creative.PolicyState = eligible ? "FormatAndRightsPassed" : "RejectedFormatRightsOrSize";
            creative.EligibilityState = eligible ? CreativeEligibility.Eligible : CreativeEligibility.Ineligible;
            creative.LastAnalyzedAtUtc = DateTime.UtcNow;
            if (eligible) eligibleCreativeId = creative.Id;
        });
        if (eligibleCreativeId is { } creativeId)
            jobs.Enqueue<CreativeVariantJob>(job => job.GenerateAsync(e.ProjectId, creativeId, CancellationToken.None));
    }

    private static CreativeMediaType? MapMediaType(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/jpeg" or "image/png" or "image/webp" => CreativeMediaType.Image,
        "video/mp4" or "video/quicktime" or "video/webm" => CreativeMediaType.Video,
        _ => null
    };
}
