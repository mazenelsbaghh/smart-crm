# Feature Specification: Shared Assets, Media Engine & Audit Trail

**Feature Branch**: `006-media-audit`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "Phase 5: Shared Assets, Media Engine & Audit Trail"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Central Asset Upload and Retrieval (Priority: P1)

Agents and automated background processes need a centralized, performant, and secure way to upload, store, and retrieve media assets (images, videos, voice recordings, documents) without duplicating file storage.

**Why this priority**: Highly critical foundation for messaging and CRM contacts. All media sent/received via WhatsApp and stored in CRM must go through this shared asset manager.

**Independent Test**: Upload a file via the API, verify the file content is saved to MinIO and recorded in the database asset registry, verify a signed URL with short expiration is generated, and verify that uploading the exact same file (same hash) reuse the existing storage reference.

**Acceptance Scenarios**:

1. **Given** an agent is authenticated and has access to a project, **When** they upload a valid image file to `/api/assets/upload`, **Then** the file is stored in MinIO under the project container, metadata (dimensions, hash, MIME type) is recorded, and the API returns the asset registry details including its unique ID.
2. **Given** a file already exists in the asset registry, **When** another user uploads the identical file (matching SHA-256 hash), **Then** the system does not upload a duplicate to MinIO, but rather increments the reference count in the registry and returns the existing asset ID.
3. **Given** an asset ID, **When** a user requests `/api/assets/{id}/download`, **Then** the system returns a secure, signed MinIO URL that expires after a configurable time (default 1 hour).

---

### User Story 2 - Automated Media Transformation and WhatsApp Optimization (Priority: P2)

Uploaded images and videos must be processed automatically to ensure compatibility with WhatsApp file size limits and optimized for fast mobile rendering.

**Why this priority**: Essential to prevent outbound WhatsApp media failures due to file size limits and to reduce bandwidth usage.

**Independent Test**: Upload a high-resolution 10MB JPEG image, verify that a `MediaWorker` job is triggered via Hangfire, creates a thumbnail (e.g. 150x150) and a compressed variant optimized for WhatsApp, and verifies both are accessible via endpoints.

**Acceptance Scenarios**:

1. **Given** a large image has been uploaded, **When** the file registry entry is created, **Then** a background worker is scheduled to compress the image and generate a thumbnail.
2. **Given** a compressed version has been generated, **When** an agent requests the thumbnail endpoint `/api/assets/{id}/thumbnail`, **Then** the system returns the resized thumbnail image.
3. **Given** an image or video exceeds WhatsApp's maximum message size constraints, **When** it is processed by the Media Engine, **Then** it is downscaled, re-encoded, and compressed until it fits within the limit.

---

### User Story 3 - Full Audit Trail and Decisional Logging (Priority: P3)

Administrators require a tamper-evident audit trail of all core system changes, API requests, CRM mutations, AI replies, and manual approvals for compliance and debugging.

**Why this priority**: Critical for security compliance, resolving dispute issues, and auditing AI activity.

**Independent Test**: Perform a customer lead score change or manual approval edit, search the audit trail via `/api/projects/{id}/audit`, and verify that the logs show the exact original and modified values, IP address, and user ID.

**Acceptance Scenarios**:

1. **Given** a user changes a customer's CRM profile, **When** the change is committed, **Then** an audit log is written recording the field, before value, after value, modifier user ID, and timestamp.
2. **Given** the system is running, **When** any API request is processed, **Then** a structured log (via Serilog) is generated capturing endpoint, user context, IP address, and execution duration.
3. **Given** the audit trail exists, **When** an admin queries `/api/projects/{id}/audit` with filters for user, action, or date range, **Then** the system returns the matching historical logs sorted chronologically from Elasticsearch.

---

### User Story 4 - System Health Monitoring and Health Metrics Dashboard (Priority: P4)

Operations teams must monitor the health status and processing latencies of backend workers, RabbitMQ queue depths, Redis connections, database connection pools, WhatsApp gateways, and Gemini AI endpoints.

**Why this priority**: Ensures system reliability, early failure detection, and automatic scaling indicators.

**Independent Test**: Trigger a query to `/api/system/health`, verify that the system returns health status indicators for all critical infrastructure elements, and verify that metrics are exposed for external monitoring.

**Acceptance Scenarios**:

1. **Given** all infrastructure services are functional, **When** the health endpoint `/api/system/health` is queried, **Then** the response returns HTTP 200 with an overall status of "Healthy" and detailed statuses for PostgreSQL, Redis, RabbitMQ, MinIO, Elasticsearch, Baileys gateway, and Gemini latency.
2. **Given** a failure in the WhatsApp Gateway connection, **When** the health check is run, **Then** the health check returns HTTP 503 "Unhealthy" with the WhatsApp component flagged as down.
3. **Given** queue depths in RabbitMQ exceed a critical threshold, **When** monitored, **Then** the system raises an alert via the internal notification engine to notify administrators.

---

### Edge Cases

- **Concurrent Uploads of Same File**: If two separate agents upload the identical file at the exact same millisecond, how does the system prevent write collisions? The system must use database unique constraints on the file hash combined with file lock mechanisms.
- **MinIO and Database Out-of-Sync**: If MinIO upload succeeds but the database registration fails, the system must clean up the orphaned file in MinIO.
- **Elasticsearch Ingestion Failure**: If Elasticsearch indexing for audit logs fails, the primary write to PostgreSQL must succeed, but the failure must be logged locally to avoid blocking user operations.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support media uploading (images, videos, voice recordings, documents) to a centralized storage service (MinIO) via `/api/assets/upload`.
- **FR-002**: System MUST generate secure signed URLs for all asset downloads via `/api/assets/{id}/download` that expire after a configurable duration.
- **FR-003**: System MUST compute SHA-256 hashes of all uploaded files and reference existing assets if a duplicate hash is detected (no double storage).
- **FR-004**: System MUST perform automated background media transformations including thumbnail generation, image resizing, and compression optimized for WhatsApp limits.
- **FR-005**: System MUST log all API requests, CRM changes (before/after states), AI decisions, and approvals to a structured log file and index them in Elasticsearch.
- **FR-006**: System MUST expose a search endpoint `/api/projects/{id}/audit` to query audit logs by action, user, and timestamp ranges.
- **FR-007**: System MUST provide health endpoints `/api/system/health` and `/api/system/metrics` that monitor PostgreSQL, Redis, RabbitMQ, MinIO, Elasticsearch, WhatsApp gateway, and Gemini latency.
- **FR-008**: System MUST send alerts through the notification system when critical health checks fail or queue depths exceed threshold limits.

### Key Entities *(include if feature involves data)*

- **Asset**:
  - `Id` (UUID, primary key)
  - `ProjectId` (UUID, tenant boundary)
  - `FileName` (string)
  - `ContentType` (string/MIME type)
  - `FileSize` (long)
  - `FileHash` (string, SHA-256 for deduplication)
  - `StoragePath` (string, MinIO path)
  - `Metadata` (JSON containing dimensions, duration, etc.)
  - `ReferenceCount` (int, for tracking duplicate uploads)
  - `UploadedBy` (UUID)
  - `CreatedAt` (DateTime)
  
- **AssetVariant**:
  - `Id` (UUID)
  - `AssetId` (UUID, links to primary Asset)
  - `VariantType` (enum: Thumbnail, WhatsAppOptimized)
  - `FileSize` (long)
  - `StoragePath` (string)
  - `Metadata` (JSON)
  - `CreatedAt` (DateTime)

- **AuditLog**:
  - `Id` (UUID)
  - `ProjectId` (UUID, tenant boundary)
  - `UserId` (UUID/nullable for system actions)
  - `Action` (string, e.g. "CustomerUpdate", "ApprovalExecuted")
  - `EntityType` (string, e.g. "Customer")
  - `EntityId` (string)
  - `OriginalState` (JSON/nullable)
  - `NewState` (JSON/nullable)
  - `IPAddress` (string)
  - `Timestamp` (DateTime)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of uploaded files are deduplicated if they match an existing hash, resulting in zero extra disk space consumption for duplicate uploads.
- **SC-002**: Images are compressed and resized to meet WhatsApp's 16MB file limit in under 3 seconds in the background.
- **SC-003**: System health check returns full status payload in under 200 milliseconds under normal load.
- **SC-004**: Audit log searching via Elasticsearch returns results in under 50 milliseconds for up to 100,000 logs.

## Assumptions

- MinIO, PostgreSQL, Redis, and Elasticsearch are fully running inside the Docker environment.
- The standard user role/permissions model will be utilized for restricting access to audit search and health endpoints.
- Serilog will be configured to write structured logs directly to files, which are shipped/indexed to Elasticsearch.
