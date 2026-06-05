# Implementation Plan: Implement Missing Core Features

**Branch**: `019-implement-missing-core-features` | **Date**: 2026-06-02 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart whatsapp/specs/019-implement-missing-core-features/spec.md)

**Input**: Feature specification from `/specs/019-implement-missing-core-features/spec.md`

## Summary

Implement the missing workflow actions (`SendMessage` and `SendAlert`), advanced routing logic inside `AssignmentEngine.cs` (VIP, complaint, and offline/idle reassignment), and the complete Knowledge Base Suggestion & Approval lifecycle. This includes backend EF Core database schema changes, service updates, endpoint creation, and front-end integration.

## Technical Context

**Language/Version**: C# (.NET 8), TS/React 18, Node.js

**Primary Dependencies**: Microsoft.AspNetCore.SignalR, StackExchange.Redis, Hangfire

**Storage**: PostgreSQL with pgvector, Redis

**Testing**: Pytest integration tests & C# Unit/Integration Tests

**Target Platform**: Docker Containerized Environment

**Project Type**: Web Application

**Performance Goals**: Real-time alert SignalR broadcasts under 200ms, RAG Knowledge Base searches under 300ms.

**Constraints**: Multi-tenant isolation by `ProjectId` in database queries.

## Constitution Check

| Principle | Check | Status / Justification |
| :--- | :--- | :--- |
| **I. Modular Monolith** | Separate workflows, assignment, and brain logic. | **PASSED**. Communication between Modules will utilize events or scoped DB contexts with tenant filters. |
| **II. Project Isolation** | Multi-tenant isolation by `ProjectId`. | **PASSED**. All database updates and queries are isolated by tenant/project context. |
| **III. Gemini 3.5 Unified AI** | Retrieve only approved knowledge documents. | **PASSED**. AI Company Brain RAG matches are filtered to `Approved` chunks. |
| **IV. Human-Like Messaging** | Delay sending via ReplySender/gateway scheduler. | **PASSED**. Workflow messages route through existing queueing and gateway delays. |
| **V. Risk-Based Action Approval** | Knowledge approvals and VIP notifications. | **PASSED**. Administrative approval required before suggestions are activated. |

## Project Structure

### Documentation (this feature)

```text
specs/019-implement-missing-core-features/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technical analysis & decisions
├── data-model.md        # Database and entity updates
├── quickstart.md        # Developer setup guide
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── Workflows/
│   │   │   └── Services/
│   │   │       └── WorkflowEngine.cs         # Support SendMessage and SendAlert actions
│   │   ├── Conversations/
│   │   │   ├── Services/
│   │   │       └── AssignmentEngine.cs       # Implement VIP, Complaint, and Offline/Idle routing
│   │   │   ├── API/
│   │   │       └── ConversationController.cs  # Expose agent status update and force reassignment
│   │   ├── Brain/
│   │   │   ├── Domain/
│   │   │   │   ├── KnowledgeDocument.cs      # Add ApprovalStatus enum/field
│   │   │   │   └── KnowledgeChunk.cs         # Filter by ApprovalStatus
│   │   │   ├── Services/
│   │   │   │   ├── KnowledgeBaseService.cs   # Implement suggest, approve, and reject methods
│   │   │   │   └── AICompanyBrain.cs         # Filter RAG by Approved status
│   │   │   ├── API/
│   │   │       └── BrainController.cs        # Expose suggest, approve, and reject REST endpoints
│   │   └── Auth/
│   │       └── Domain/
│   │           └── User.cs                   # Ensure Presence properties
frontend/
└── src/
    └── packages/
        └── management/
            └── KnowledgeBase.tsx             # Add tab for suggestions and approve/reject actions
```

**Structure Decision**: Web application (encompassing Backend modular monolith and Frontend packages).
