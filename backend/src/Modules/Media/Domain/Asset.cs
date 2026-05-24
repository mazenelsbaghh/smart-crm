using System;
using Shared.Domain;

namespace Modules.Media.Domain
{
    public enum AssetVariantType
    {
        Thumbnail,
        WhatsAppOptimized
    }

    public class Asset : AuditableEntity, ITenantEntity
    {
        public Guid ProjectId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContentType { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string FileHash { get; set; } = string.Empty;
        public string StoragePath { get; set; } = string.Empty;
        public int ReferenceCount { get; set; } = 1;
        public Guid UploadedBy { get; set; }
    }

    public class AssetVariant : AuditableEntity
    {
        public Guid AssetId { get; set; }
        public AssetVariantType VariantType { get; set; }
        public long FileSize { get; set; }
        public string StoragePath { get; set; } = string.Empty;
        public string? MetadataJson { get; set; }
    }
}
