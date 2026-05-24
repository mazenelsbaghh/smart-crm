# Implementation Plan: Company Brain, Knowledge Base, Workflows & Approval System

**Branch**: `004-knowledge-workflows` | **Date**: 2026-05-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/004-knowledge-workflows/spec.md`

## Summary

Implement the Phase 3 specification by extending the C# ASP.NET Core backend (.NET 9) and Python integration tests. This phase adds project-specific intelligence through a semantic Knowledge Base and Company Brain, orchestrates events via a Workflow trigger engine, enforces safety via a Risk-Based Action Approval system, and updates Customer Memory profiles to inform future AI context.
- **AI Company Brain & Knowledge Base**: Embedding pipeline using Gemini (`text-embedding-004`), storage using `pgvector` in PostgreSQL, semantic search, versioning, and draft/approval lifecycle.
- **Workflow Engine**: Database-backed automation schemas triggered by RabbitMQ events, dynamic filters/actions, and execution logs.
- **Risk Analyzer & Approval System**: Risk classification middleware/service; high-risk events (e.g. discount sends or data updates) are paused, queued in PostgreSQL, and pushed to supervisors via SignalR for manual action.
- **Customer Memory & Integrations**: Long-term fact storage update upon conversation close, periodic sync schedules via Hangfire, and webhook dispatcher.

## Technical Context

**Language/Version**: C# (.NET 9), Python 3.14 (tests), Node.js v20 (gateway)

**Primary Dependencies**: 
- Backend: `Hangfire.AspNetCore` (v1.8), `Microsoft.AspNetCore.SignalR.Core`, `RabbitMQ.Client` (v7.2.1), `StackExchange.Redis` (v2.13), `Npgsql.EntityFrameworkCore.PostgreSQL` (v9.0.0) with pgvector support
- Testing: `pytest`, `pytest-asyncio`, `httpx`

**Storage**: PostgreSQL (Primary DB, pgvector, Hangfire), Redis (Workflow caching, rate-limiting)

**Testing**: pytest (running on host testing the containerized API endpoints)

**Target Platform**: Linux Server (Ubuntu) / macOS (Docker/Docker Compose containerized environment)

**Project Type**: Web application (ASP.NET Core Backend Web API + Node.js WhatsApp Gateway)

**Performance Goals**:
- Semantic Search response under 300ms.
- Workflow execution (excluding delays) under 1s.
- Risk analysis classification under 150ms.

**Constraints**:
- Absolute tenant separation by `ProjectId` in database queries and Redis caching.
- Vector dimension count is 768 (matching `text-embedding-004` output).
- High-risk actions must never execute without explicit supervisor approval token check.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular Monolith Architecture**: Yes. Features are isolated in `Modules/Brain`, `Modules/Workflows`, `Modules/Approvals`, `Modules/Integrations`, and `Modules/Customers` within the single backend project, communicating asynchronously using RabbitMQ events.
- **Strict Multi-Tenant Project Isolation**: Yes. All newly introduced entities implement `ITenantEntity` and are filtered by `ProjectId`.
- **Gemini 3.5 Flash Unified AI Engine**: Yes. We use Gemini 3.5 Flash for summarization, entity extraction, and intent mapping. We use Gemini `text-embedding-004` for vector representation.
- **Human-Like Messaging and Aggregation**: Yes. Workflows that send automated messages route through the existing `HumanMessagingEngine` to preserve typing delays.
- **Risk-Based Action Approval System (Human-in-the-Loop)**: Yes. The Risk Analyzer evaluates all outgoing AI actions, blocking and routing high-risk requests to the new approvals module queue.

## Project Structure

### Documentation (this feature)

```text
specs/004-knowledge-workflows/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technology choices (pgvector, workflow triggers)
├── data-model.md        # Database schema updates
├── quickstart.md        # How to run and test Phase 3
└── contracts/           # API contracts for knowledge, workflows, approvals, integrations
    ├── knowledge.json
    ├── workflows.json
    ├── approvals.json
    └── integrations.json
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── Brain/
│   │   │   ├── API/
│   │   │   │   └── BrainController.cs
│   │   │   ├── Services/
│   │   │   │   └── AICompanyBrain.cs
│   │   │   └── Models/
│   │   │       ├── KnowledgeDocument.cs
│   │   │       └── KnowledgeChunk.cs
│   │   ├── Workflows/
│   │   │   ├── API/
│   │   │   │   └── WorkflowsController.cs
│   │   │   ├── Services/
│   │   │   │   └── WorkflowEngine.cs
│   │   │   ├── Workers/
│   │   │   │   └── WorkflowWorker.cs
│   │   │   └── Models/
│   │   │       └── AutomationWorkflow.cs
│   │   ├── Approvals/
│   │   │   ├── API/
│   │   │   │   └── ApprovalsController.cs
│   │   │   ├── Services/
│   │   │   │   └── RiskAnalyzer.cs
│   │   │   └── Models/
│   │   │       └── ApprovalRequest.cs
│   │   └── Integrations/
│   │       ├── API/
│   │       │   └── IntegrationsController.cs
│   │       └── Services/
│   │           └── ProjectIntegrationService.cs
│   └── Program.cs
```

**Structure Decision**: Modular Web application structure mirroring ASP.NET Core project organization.

## Complexity Tracking

*No violations detected. Structure aligns fully with the Project Constitution.*
