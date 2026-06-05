# Data Model Design: WhatsApp Media & AI Processing

## Database Entities

### 1. Asset (`Modules.Media.Domain.Asset`)
This entity represents a stored raw media file. (Already defined in code, but mapped here for completeness).

- `Id` (Guid, PK)
- `ProjectId` (Guid, Index, Tenant Isolation)
- `FileName` (String)
- `ContentType` (String)
- `FileSize` (Long)
- `FileHash` (String)
- `StoragePath` (String - key inside S3 container)
- `ReferenceCount` (Int)
- `UploadedBy` (Guid)
- `CreatedAt` (DateTime)
- `UpdatedAt` (DateTime)

### 2. AssetVariant (`Modules.Media.Domain.AssetVariant`)
This entity represents optimized or alternative variants of an asset (like thumbnails or optimized versions). (Already defined in code).

- `Id` (Guid, PK)
- `AssetId` (Guid, FK to `Asset`)
- `VariantType` (Enum: `Thumbnail`, `WhatsAppOptimized`)
- `FileSize` (Long)
- `StoragePath` (String)
- `MetadataJson` (String - optional width/height metadata)
- `CreatedAt` (DateTime)
- `UpdatedAt` (DateTime)

### 3. Message (`Modules.Conversations.Domain.Message`)
Modify this existing entity to reference Assets and hold voice transcription texts:

- `AssetId` (Guid?, Nullable FK to `Asset`)
- `Transcription` (String?, Nullable)

## Relationships

- **Asset 1 : N AssetVariant**: One raw asset can have multiple variants (e.g. a thumbnail for dashboard rendering and a compressed version for client devices).
- **Message 0..1 : 1 Asset**: A message can optionally contain a single media asset attachment.
- **Project 1 : N Asset**: Strict multi-tenant isolation. Assets are scoped to a Project.
