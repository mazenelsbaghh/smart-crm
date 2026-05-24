using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure;
using Shared.Security;
using Modules.Media.Domain;
using Hangfire;

namespace Modules.Media.Services
{
    public interface IAssetService
    {
        Task<Asset> UploadAssetAsync(Guid projectId, string fileName, string contentType, Stream fileStream, Guid uploadedBy);
        Task<string> GetSignedUrlAsync(Guid assetId);
        Task<Asset?> DeleteAssetAsync(Guid assetId);
        Task<string> GetThumbnailUrlAsync(Guid assetId);
    }

    public class AssetService : IAssetService
    {
        private readonly AppDbContext _context;
        private readonly IMinIoStorageService _storageService;
        private readonly IBackgroundJobClient _backgroundJobClient;
        private readonly ITenantContext _tenantContext;

        public AssetService(
            AppDbContext context, 
            IMinIoStorageService storageService,
            IBackgroundJobClient backgroundJobClient,
            ITenantContext tenantContext)
        {
            _context = context;
            _storageService = storageService;
            _backgroundJobClient = backgroundJobClient;
            _tenantContext = tenantContext;
        }

        public async Task<Asset> UploadAssetAsync(Guid projectId, string fileName, string contentType, Stream fileStream, Guid uploadedBy)
        {
            if (projectId != _tenantContext.ProjectId)
            {
                throw new UnauthorizedAccessException("Upload ProjectId does not match current tenant.");
            }

            // 1. Copy stream to memory to compute hash and upload
            using var ms = new MemoryStream();
            await fileStream.CopyToAsync(ms);
            var fileBytes = ms.ToArray();

            // 2. Compute SHA-256 hash
            string fileHash;
            using (var sha256 = SHA256.Create())
            {
                var hashBytes = sha256.ComputeHash(fileBytes);
                fileHash = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }

            // 3. Check if file already exists in this project (tenant isolation)
            var existingAsset = await _context.Assets
                .FirstOrDefaultAsync(a => a.FileHash == fileHash && a.ProjectId == projectId);

            if (existingAsset != null)
            {
                existingAsset.ReferenceCount += 1;
                existingAsset.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return existingAsset;
            }

            // 4. Create new asset record
            var assetId = Guid.NewGuid();
            var extension = Path.GetExtension(fileName).ToLower();
            var objectKey = $"projects/{projectId}/assets/{assetId}{extension}";

            // 5. Upload to MinIO
            using var uploadStream = new MemoryStream(fileBytes);
            await _storageService.UploadFileAsync(objectKey, uploadStream, contentType);

            var asset = new Asset
            {
                Id = assetId,
                ProjectId = projectId,
                FileName = fileName,
                ContentType = contentType,
                FileSize = fileBytes.Length,
                FileHash = fileHash,
                StoragePath = objectKey,
                ReferenceCount = 1,
                UploadedBy = uploadedBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Assets.Add(asset);
            await _context.SaveChangesAsync();

            // 6. Schedule background media transformation job if it's an image
            if (contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                // We will enqueue a background job. We'll implement Jobs.MediaWorker next.
                // For now, we schedule it dynamically.
                _backgroundJobClient.Enqueue<Jobs.IMediaWorker>(worker => worker.ProcessAssetAsync(asset.Id));
            }

            return asset;
        }

        public async Task<string> GetSignedUrlAsync(Guid assetId)
        {
            var asset = await _context.Assets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == assetId);
            if (asset == null)
            {
                throw new KeyNotFoundException($"Asset with ID {assetId} was not found.");
            }

            if (asset.ProjectId != _tenantContext.ProjectId)
            {
                throw new UnauthorizedAccessException("Cross-tenant asset access is forbidden.");
            }

            // Generate signed URL valid for 1 hour
            return await _storageService.GetSignedUrlAsync(asset.StoragePath, TimeSpan.FromHours(1));
        }

        public async Task<Asset?> DeleteAssetAsync(Guid assetId)
        {
            var asset = await _context.Assets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == assetId);
            if (asset == null)
            {
                return null;
            }

            if (asset.ProjectId != _tenantContext.ProjectId)
            {
                throw new UnauthorizedAccessException("Cross-tenant asset access is forbidden.");
            }

            if (asset.ReferenceCount > 1)
            {
                asset.ReferenceCount -= 1;
                asset.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return asset;
            }

            // Reference count is 1, completely delete
            // Delete primary file from storage
            try
            {
                await _storageService.DeleteFileAsync(asset.StoragePath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Failed to delete main asset file from MinIO: {ex.Message}");
            }

            // Delete variants from storage and database
            var variants = await _context.AssetVariants.Where(v => v.AssetId == assetId).ToListAsync();
            foreach (var variant in variants)
            {
                try
                {
                    await _storageService.DeleteFileAsync(variant.StoragePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Failed to delete variant file from MinIO: {ex.Message}");
                }
                _context.AssetVariants.Remove(variant);
            }

            _context.Assets.Remove(asset);
            await _context.SaveChangesAsync();

            return null;
        }

        public async Task<string> GetThumbnailUrlAsync(Guid assetId)
        {
            var asset = await _context.Assets.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.Id == assetId);
            if (asset == null)
            {
                throw new KeyNotFoundException($"Asset with ID {assetId} was not found.");
            }

            if (asset.ProjectId != _tenantContext.ProjectId)
            {
                throw new UnauthorizedAccessException("Cross-tenant asset access is forbidden.");
            }

            var thumbnail = await _context.AssetVariants
                .FirstOrDefaultAsync(v => v.AssetId == assetId && v.VariantType == AssetVariantType.Thumbnail);

            if (thumbnail != null)
            {
                return await _storageService.GetSignedUrlAsync(thumbnail.StoragePath, TimeSpan.FromHours(1));
            }

            // Fallback to the main asset URL
            return await _storageService.GetSignedUrlAsync(asset.StoragePath, TimeSpan.FromHours(1));
        }
    }
}
