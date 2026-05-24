# Implementation Plan: Shared Assets, Media Engine & Audit Trail

**Branch**: `phase/5-media-audit` | **Date**: 2026-05-25 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/006-media-audit/spec.md)

**Input**: Feature specification from `/specs/006-media-audit/spec.md`

## Summary

Implement the centralized asset management system using MinIO for physical storage and PostgreSQL for registry cataloging (complete with reference counting, file hash deduplication, and secure signed URLs). Build a background media transformation worker using `SixLabors.ImageSharp` for thumbnail generation and WhatsApp compatibility optimization. Develop a comprehensive audit logging system utilizing Serilog and Elasticsearch for indexing user API actions, AI replies, and CRM modifications. Finally, construct a system health and telemetry monitoring engine exposing `/health` and `/metrics` APIs.

## Technical Context

**Language/Version**: C# (.NET 9.0)

**Primary Dependencies**:
- `AWSSDK.S3` (v3.7) for MinIO interactions
- `SixLabors.ImageSharp` (v3.1) for image transformations
- `Serilog.AspNetCore` (v9.0) and `Serilog.Sinks.File` for structured logging
- `Elastic.Clients.Elasticsearch` (v8) for audit log indexing and querying
- `Hangfire.AspNetCore` (v1.8) for scheduling media transformation jobs

**Storage**: PostgreSQL (catalog and audit transactions), Redis (health cache), MinIO (media storage), Elasticsearch (audit indexing)

**Testing**: Python (`pytest` + `httpx` + `pytest-asyncio` + `boto3`)

**Target Platform**: Linux Server (Docker/Docker Compose)

**Project Type**: Web API and Background Workers

**Performance Goals**:
- Thumbnail generation completed in under 2 seconds.
- Deduplication lookup executed in under 15 milliseconds.
- Overall system health endpoint response time under 200 milliseconds.

**Constraints**: Strict multi-tenant isolation by `ProjectId`.

**Scale/Scope**: Support files up to 100MB; thousands of uploads per project.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Modular Monolith Architecture**: Assets, Media transformations, and Audit logs reside in their own clean modular boundaries and coordinate using Hangfire background jobs and DB context triggers.
- [x] **Strict Multi-Tenant Project Isolation**: Every asset record, file path, audit log, and query checks `ProjectId`.
- [x] **Gemini 3.5 Flash Unified AI Engine**: Future AI tagging queries will bind directly to the existing Gemini module services.
- [x] **Human-Like Messaging and Aggregation**: Media optimization guarantees files sent to WhatsApp stay under limits to prevent ban flags.
- [x] **Risk-Based Action Approval System**: Audit logs capture all approval actions.

## Project Structure

### Documentation (this feature)

```text
specs/006-media-audit/
├── plan.md              # This file
├── research.md          # Decision and Rationale
├── data-model.md        # DB Entity Layout
├── quickstart.md        # Running and Verification
└── contracts/
    └── api.md           # API endpoints schema
```

### Source Code (repository root)

```text
backend/
├── Program.cs
├── backend.csproj
└── src/
    ├── Modules/
    │   ├── Media/          # Asset registry, signed URLs, transformations
    │   ├── Audit/          # Structured logs database sync and Elasticsearch client
    │   └── SystemHealth/   # Telemetry, health checks, queue monitoring
    └── Shared/
        ├── Domain/
        └── Infrastructure/
```

**Structure Decision**: Monolith structure. Extends `backend/` with `Media`, `Audit`, and `SystemHealth` modules.
