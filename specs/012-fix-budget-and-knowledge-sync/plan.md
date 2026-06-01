# Implementation Plan: CRM Customer Budget & Knowledge Sync Seeding Fixes

**Branch**: `012-fix-budget-and-knowledge-sync` | **Date**: 2026-05-25 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/012-fix-budget-and-knowledge-sync/spec.md)

**Input**: Feature specification from `/specs/012-fix-budget-and-knowledge-sync/spec.md`

## Summary

This plan addresses two critical backend bugs:
1. **CRM Customer Budget Persistence**: Currently, updating a customer profile with an empty or cleared budget sends a null budget to the backend, which is ignored because of `request.Budget.HasValue` check. We will introduce a boolean flag `IsBudgetSet` in `UpdateCustomerRequest` to detect when the field is explicitly sent, allowing it to save `null` and sync it (as `0`) to the active deal.
2. **AI Sync Brain Seeding**: Clicking the sync button deletes existing user documents and inserts mock templates. We will modify `AICompanyBrain.SyncBrainAsync` to check if documents exist first. If none exist, it will seed the 3 default templates. If documents do exist, it will only index/re-index documents lacking embeddings.

## Technical Context

**Language/Version**: C# (.NET 9.0)

**Primary Dependencies**: Microsoft.EntityFrameworkCore, pgvector

**Storage**: PostgreSQL with pgvector extension

**Testing**: Pytest integration tests in `tests/`

**Target Platform**: Docker Compose local development / Linux server

**Project Type**: REST API Web Service

## Constitution Check

- **Modular Monolith**: Handled inside CRM and Brain modules (no cross-module boundary leakage).
- **Multi-Tenant Isolation**: Scoped correctly using the tenant's `ProjectId`.

## Project Structure

### Documentation

```text
specs/012-fix-budget-and-knowledge-sync/
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
        ├── Brain/
        │   ├── API/
        │   │   └── BrainController.cs
        │   └── Services/
        │       └── AICompanyBrain.cs
        └── CRM/
            └── API/
                └── CRMController.cs
```

**Structure Decision**: Monolith backend. The changes are localized to `CRMController.cs` and `AICompanyBrain.cs`.
