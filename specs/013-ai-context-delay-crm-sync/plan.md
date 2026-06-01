# Implementation Plan: AI Context, Delay Tuning & Auto CRM Deal Sync

**Branch**: `013-ai-context-delay-crm-sync` | **Date**: 2026-05-25 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/013-ai-context-delay-crm-sync/spec.md)

**Input**: Feature specification from `/specs/013-ai-context-delay-crm-sync/spec.md`

## Summary

This plan integrates recent chat history and long-term customer memory into the Gemini auto-reply prompt, updates the message aggregation and typing delay values, and ensures automated CRM budget updates are synchronized with active deals in the database.

## Technical Context

**Language/Version**: C# (.NET 9.0)

**Primary Dependencies**: Microsoft.EntityFrameworkCore, StackExchange.Redis

**Storage**: PostgreSQL, Redis

**Testing**: Pytest integration tests in `tests/`

**Target Platform**: Docker Compose / Linux server

## Constitution Check

- **Modular Monolith**: Boundaries maintained.
- **Human-Like Messaging**: Handled via `HumanMessagingEngine` (typing delay) and `MessageAggregator` (silence window).

## Project Structure

### Documentation

```text
specs/013-ai-context-delay-crm-sync/
├── spec.md              # Feature specification
├── plan.md              # This file
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Task breakdown
```

### Source Code

```text
backend/
└── src/
    └── Modules/
        ├── AI/
        │   ├── Services/
        │   │   └── AIMarketingBrain.cs
        │   └── Workers/
        │       └── AIReplyWorker.cs
        ├── CRM/
        │   └── Services/
        │       └── CRMAutoUpdateEngine.cs
        ├── Approvals/
        │   └── API/
        │       └── ApprovalsController.cs
        ├── Conversations/
        │   └── Services/
        │       └── MessageAggregator.cs
        └── WhatsApp/
            └── Services/
                └── HumanMessagingEngine.cs
```

**Structure Decision**: Monolith backend. Updates are distributed across the respective domain modules (AI, CRM, Approvals, Conversations, WhatsApp).
