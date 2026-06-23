# Implementation Plan: remove-deals-and-profits

**Branch**: `028-remove-deals-and-profits` | **Date**: 2026-06-23 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/028-remove-deals-and-profits/spec.md`

## Summary

We will remove the deals, profit indicators, and sales pipeline boards from the user interface. We will keep the backend database structure intact to prevent breaking changes or complex database migrations, but we will clean up the frontend UI elements entirely.

## Technical Context

**Language/Version**: TypeScript / Next.js (Frontend)
**Primary Dependencies**: React, Lucide-React, CSS Modules
**Storage**: N/A (no schema modifications)
**Testing**: build and run manual validation checks
**Target Platform**: Web Client
**Project Type**: Web Application
**Performance Goals**: N/A
**Constraints**: Keep backend C# code compiled with zero errors; do not touch schema.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **I. Modular Monolith Architecture**: PASS. No database schema changes, purely frontend view clean up.
- **II. Strict Multi-Tenant Project Isolation**: PASS. No project isolation logic is affected.
- **III. Gemini 3.5 Flash Unified AI Engine**: PASS. No change to LLM operations.
- **IV. Human-Like Messaging and Aggregation**: PASS.
- **V. Risk-Based Action Approval System**: PASS.

## Project Structure

### Documentation (this feature)

```text
specs/028-remove-deals-and-profits/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Verification guide (Quickstart)
└── tasks.md             # Phase 2 output
```

### Source Code (repository root)

We will modify the following frontend files:
- [layout.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/app/%28dashboard%29/layout.tsx): Remove pipeline menu item.
- [Dashboard.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/dashboard/Dashboard.tsx): Remove open deals and closed revenue stats, and the pipeline shortcut action.
- [CustomerDetail.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/components/shared/CustomerDetail.tsx): Remove budget input field and pipeline stage select dropdown.

We will also update the project documentation:
- [frontend_plan.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/docs/frontend_plan.md): Record layout/dashboard updates.

## Complexity Tracking

No constitution violations detected.
