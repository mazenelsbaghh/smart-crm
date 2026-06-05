# Implementation Plan: WhatsApp Media & AI Processing

**Branch**: `017-whatsapp-media-ai` | **Date**: 2026-06-01 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/017-whatsapp-media-ai/spec.md)

**Input**: Feature specification from `/specs/017-whatsapp-media-ai/spec.md`

## Summary

Implement end-to-end incoming WhatsApp media processing (images, voice notes, documents). The Node.js Baileys gateway will download raw media files, forward them to the backend, which will upload them to MinIO Object Storage, index them in PostgreSQL as `Asset` entities, and transform images for optimization. The backend `AIReplyWorker` will utilize Gemini 3.5 Flash's native multimodal capabilities to parse voice notes and extract facts from images OCR-free. The React frontend inbox will be updated with audio players and image previews using temporary secure pre-signed URLs.

## Technical Context

**Language/Version**: C# (.NET 8), Node.js (ES18), TypeScript (React 18)

**Primary Dependencies**: `@whiskeysockets/baileys` (v6.x), `AWSSDK.S3` (v3.7), `SixLabors.ImageSharp` (v3.x), `Google.GenerativeAI` / HttpClient, `SignalR`

**Storage**: PostgreSQL (with `pgvector`), Redis, MinIO (local S3-compatible Object Storage)

**Testing**: Pytest (Python test harness for integration tests), C# Unit Tests

**Target Platform**: Ubuntu Server / Docker Containerized Environment

**Project Type**: Web Application (Monolith ASP.NET backend, React frontend, Node.js gateway)

**Performance Goals**: Media processing, upload, and timeline indexing complete in <3s. Multimodal Gemini 3.5 Flash response under 15s.

**Constraints**: Strict multi-tenant isolation by `ProjectId` in database queries and S3 directory partitioning. Pre-signed media URLs expire after 1 hour.

**Scale/Scope**: Support 5MB file uploads for voice/images. Handle 100+ concurrent media files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status / Justification |
| :--- | :--- | :--- |
| **I. Modular Monolith** | Code changes must remain partitioned inside respective modules. | **PASSED**. Gateway modifications, Media file uploads, and AI Gemini multi-modal processing will reside strictly in `WhatsApp`, `Media`, `AI`, and `Conversations` modules respectively. |
| **II. Project Isolation** | Media files and DB records must be isolated by `ProjectId`. | **PASSED**. File paths in MinIO will follow partition pattern `/projects/{projectId}/assets/`. PostgreSQL entities filter by project. |
| **III. Gemini 3.5 Unified AI** | Direct multimodal processing. No separate OCR or Whisper. | **PASSED**. Gemini 3.5 Flash handles transcription of audio voice notes and extraction of image text/details directly. |
| **IV. Human-Like Messaging** | Grouping and delays. | **PASSED**. Media aggregation delay waits for files to finish downloading before processing the combined message intent. |
| **V. Risk Approval** | High-risk action routing. | **PASSED**. High-risk actions from images (e.g. paying invoices) will require supervisor approval. |

## Project Structure

### Documentation (this feature)

```text
specs/017-whatsapp-media-ai/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
└── quickstart.md        # Phase 1 output
```

### Source Code (repository root)

This feature spans the modular monolith and WhatsApp gateway:

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── Media/
│   │   │   ├── API/AssetsController.cs        # Asset endpoints and signed URL generation
│   │   │   ├── Services/AssetService.cs       # Uploads, variants, and resizing
│   │   │   └── Services/MinIoStorageService.cs # MinIO S3 API connector
│   │   ├── AI/
│   │   │   ├── Services/GeminiClient.cs       # Multimodal HttpClient logic
│   │   │   └── Workers/AIReplyWorker.cs       # Multimodal prompt construction
│   │   ├── Conversations/
│   │   │   ├── API/WebhookController.cs       # Intercepting media webhook payloads
│   │   │   └── Domain/Message.cs              # Add AssetId & Transcription references
│   │   └── WhatsApp/
│   │       └── Workers/ReplySender.cs         # Handles sending media attachments
│   └── Shared/
│       └── Infrastructure/AppDbContext.cs     # Migration updates for Message & Assets
whatsapp-gateway/
├── src/
│   ├── index.js                               # Download and forward media file streams
│   └── baileys-manager.js                     # Intercept WhatsApp media message events
frontend/
└── src/
    ├── components/shared/CustomerDetail.tsx   # Displays transcriptions
    └── packages/inbox/
        ├── Inbox.tsx                          # Media rendering in chat bubble timeline
        └── inbox.module.css                   # styling for media player/images
```

**Structure Decision**: Option 2: Web application (encompassing Backend modular monolith, Frontend packages, and WhatsApp gateway service).

## Complexity Tracking

*No violations identified. Solution conforms perfectly to architectural constitution.*
