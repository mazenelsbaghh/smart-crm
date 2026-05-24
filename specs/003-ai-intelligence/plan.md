# Implementation Plan: AI Intelligence, CRM Auto-Updates, Assignment & Smart Messaging

**Branch**: `phase/2-ai-intelligence` | **Date**: 2026-05-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/003-ai-intelligence/spec.md`

## Summary

Implement the Phase 2 specification by extending the C# ASP.NET Core backend (.NET 9) and the Python integration tests. This phase implements the core AI intelligence, real-time communications, scheduling jobs, and data isolation required for a production-grade multi-tenant platform.
- **AI Marketing & Messaging**: `AIMarketingBrain` prompt templates and `HumanMessagingEngine` with message chunking, random delay simulation, and rate-limiting.
- **AI CRM Auto-Updates & Classification**: Extraction of metadata parameters from messages and publishing a `CRMUpdateSuggested` event to trigger database update or supervisor review.
- **Assignment Engine**: Redis presence tracking, agent active load assessment, and least-busy routing.
- **Scheduler**: Integration of Hangfire for database checks, lead score recalculations, and health monitoring.
- **Notifications**: Real-time push system using SignalR for instant SLA alerts, complaints, and VIP notifications.
- **Reporting**: REST endpoints for daily metrics, follow-up reports, and AI analytics.

## Technical Context

**Language/Version**: C# (.NET 9), Python 3.14 (tests), Node.js v20 (gateway)

**Primary Dependencies**: 
- Backend: `Hangfire.AspNetCore` (v1.8), `Hangfire.PostgreSql` (v1.20), `Microsoft.AspNetCore.SignalR.Core`, `RabbitMQ.Client` (v7.2.1), `StackExchange.Redis` (v2.13)
- Testing: `pytest`, `pytest-asyncio`, `httpx`

**Storage**: PostgreSQL (Primary DB, Hangfire storage), Redis (Agent presence, caching)

**Testing**: pytest (running on host testing the containerized API endpoints)

**Target Platform**: Linux Server (Ubuntu) / macOS (Docker/Docker Compose containerized environment)

**Project Type**: Web application (ASP.NET Core Backend Web API + Node.js WhatsApp Gateway)

**Performance Goals**: 
- AI analysis completion and routing decision within 1.5 seconds of aggregation window completion.
- Real-time SignalR notifications delivered in < 200ms.
- Reporting APIs return aggregated outputs for up to 10k rows in < 300ms.

**Constraints**:
- Strict multi-tenant isolation (all data scoped to `ProjectId` via EF query filters).
- Outgoing messages throttled with dynamic typing delays to prevent WhatsApp bans.
- SignalR hubs must authenticate connections via JWT token.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular Monolith Architecture**: Yes. Features are organized in `Modules/AI`, `Modules/CRM`, `Modules/WhatsApp`, `Modules/Projects`, and `Modules/Conversations` communicating asynchronously via RabbitMQ events.
- **Strict Multi-Tenant Project Isolation**: Yes. `ProjectId` is validated on all operations. All DB tables implementing `ITenantEntity` have query filters.
- **Gemini 3.5 Flash Unified AI Engine**: Yes. Integrated directly via the shared `GeminiClient` in `Modules/AI`.
- **Human-Like Messaging and Aggregation**: Yes. Outgoing messages are chunked and typed with realistic delays in `HumanMessagingEngine`.
- **Risk-Based Action Approval System (Human-in-the-Loop)**: Yes. The `CRMWorker` automatically routes high-risk CRM updates (or low-confidence extractions) to an approval queue rather than executing immediately.

## Project Structure

### Documentation (this feature)

```text
specs/003-ai-intelligence/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technology choice records
├── data-model.md        # Database schema updates
├── quickstart.md        # How to run and test Phase 2
└── contracts/           # API contracts for reports, notifications, routing
    ├── reports.json
    ├── assignment.json
    └── notifications.json
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── AI/
│   │   │   ├── Services/
│   │   │   │   └── AIMarketingBrain.cs
│   │   │   └── Workers/
│   │   │       └── AIReplyWorker.cs
│   │   ├── CRM/
│   │   │   ├── Services/
│   │   │   │   └── CRMAutoUpdateEngine.cs
│   │   │   └── Workers/
│   │   │       └── CRMWorker.cs
│   │   ├── WhatsApp/
│   │   │   ├── Services/
│   │   │   │   └── HumanMessagingEngine.cs
│   │   │   └── Workers/
│   │   │       └── ReplySender.cs
│   │   ├── Conversations/
│   │   │   ├── Services/
│   │   │   │   └── AssignmentEngine.cs
│   │   │   └── Hubs/
│   │   │       └── NotificationHub.cs
│   │   └── Projects/
│   └── Program.cs
```

**Structure Decision**: Web application with ASP.NET Core backend modular structure.
