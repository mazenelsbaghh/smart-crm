---
name: "phase-5"
description: "Shared Assets, Media Engine & Audit Trail"
compatibility: "Smart Customer Core Phase 5"
metadata:
  author: "community"
  source: "phase-5/SKILL.md"
---

# Phase 5: Shared Assets, Media Engine & Audit Trail

This module provides centralized asset management (MinIO storage, hashing deduplication, signed download URLs), automated media transformations (thumbnails, WhatsApp compression), a comprehensive audit trail (Serilog structured logging, Elasticsearch logs search), and system health metrics monitoring.

## 1. Shared Assets (US1)

Upload and manage media files:
- **Upload File**: `POST /api/assets/upload` (Multipart Form with `file` and `projectId`)
- **Get Download URL**: `GET /api/assets/{id}/download` (returns a secure signed URL valid for 1 hour)
- **Delete Asset**: `DELETE /api/assets/{id}` (decrements reference count or deletes completely if 0)

Files are deduplicated using SHA-256 hashes to prevent duplicate storage.

## 2. Media Transformations (US2)

Images are processed in the background using Hangfire background jobs and `SixLabors.ImageSharp`:
- **Get Thumbnail**: `GET /api/assets/{id}/thumbnail` (redirects to the 150x150 JPEG thumbnail variant)
- **WhatsApp Optimization**: MediaWorker automatically compresses images to fit under WhatsApp limits (downscaling to 1600px width/height and encoding with 80% quality).

## 3. Audit Logging & Trails (US3)

System events and database mutations are logged automatically:
- **Automatic Interception**: Entity Framework context automatically records mutations of `Customer`, `FollowUp`, `Deal`, and `Campaign` models.
- **Structured Output**: Serilog logs entries in structured JSON format to `logs/audit.json`.
- **Search Audit Logs**: `GET /api/projects/{projectId}/audit` (allows filtering logs by `action`, `user`, and `from`/`to` date ranges).

## 4. System Health & Monitoring (US4)

Monitor system-wide services and check telemetry metrics:
- **Check Health**: `GET /api/system/health` (queries PostgreSQL, Redis, RabbitMQ, MinIO, Elasticsearch, and WhatsApp gateways status)
- **Telemetry Metrics**: `GET /api/system/metrics` (exposes clients count, database connections, and Gemini latency telemetry)
