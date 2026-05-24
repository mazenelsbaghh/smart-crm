using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Modules.Media.Domain;
using Modules.Media.Services;

namespace Modules.Media.Jobs
{
    public interface IMediaWorker
    {
        Task ProcessAssetAsync(Guid assetId);
    }

    public class MediaWorker : IMediaWorker
    {
        private readonly AppDbContext _context;
        private readonly IMinIoStorageService _storageService;
        private readonly IImageTransformer _imageTransformer;

        public MediaWorker(
            AppDbContext context,
            IMinIoStorageService storageService,
            IImageTransformer imageTransformer)
        {
            _context = context;
            _storageService = storageService;
            _imageTransformer = imageTransformer;
        }

        public async Task ProcessAssetAsync(Guid assetId)
        {
            // 1. Fetch the asset using IgnoreQueryFilters because Hangfire jobs run out-of-tenant context (background)
            var asset = await _context.Assets
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(a => a.Id == assetId);

            if (asset == null)
            {
                Console.WriteLine($"⚠️ Asset with ID {assetId} not found in background job.");
                return;
            }

            // 2. Download primary file
            using var fileStream = await _storageService.DownloadFileAsync(asset.StoragePath);
            using var primaryMs = new MemoryStream();
            await fileStream.CopyToAsync(primaryMs);

            // 3. Generate and Upload Thumbnail
            try
            {
                primaryMs.Position = 0;
                using var thumbStream = await _imageTransformer.CreateThumbnailAsync(primaryMs);
                var thumbKey = $"projects/{asset.ProjectId}/assets/{asset.Id}_thumb.jpg";
                
                await _storageService.UploadFileAsync(thumbKey, thumbStream, "image/jpeg");

                var thumbVariant = new AssetVariant
                {
                    Id = Guid.NewGuid(),
                    AssetId = asset.Id,
                    VariantType = AssetVariantType.Thumbnail,
                    FileSize = thumbStream.Length,
                    StoragePath = thumbKey,
                    MetadataJson = "{\"width\":150,\"height\":150}"
                };

                _context.AssetVariants.Add(thumbVariant);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to generate thumbnail variant for asset {assetId}: {ex.Message}");
            }

            // 4. Generate and Upload WhatsApp Optimized version
            try
            {
                primaryMs.Position = 0;
                using var optStream = await _imageTransformer.OptimizeForWhatsAppAsync(primaryMs);
                var optKey = $"projects/{asset.ProjectId}/assets/{asset.Id}_optimized.jpg";

                await _storageService.UploadFileAsync(optKey, optStream, "image/jpeg");

                var optVariant = new AssetVariant
                {
                    Id = Guid.NewGuid(),
                    AssetId = asset.Id,
                    VariantType = AssetVariantType.WhatsAppOptimized,
                    FileSize = optStream.Length,
                    StoragePath = optKey,
                    MetadataJson = "{\"maxDimension\":1600}"
                };

                _context.AssetVariants.Add(optVariant);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to generate WhatsApp optimized variant for asset {assetId}: {ex.Message}");
            }

            await _context.SaveChangesAsync();
            Console.WriteLine($"✅ Successfully processed media variants for asset {assetId}.");
        }
    }
}
