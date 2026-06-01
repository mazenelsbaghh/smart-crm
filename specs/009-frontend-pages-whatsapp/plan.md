# Implementation Plan: Frontend Management Pages & WhatsApp QR Connectivity

**Branch**: `009-frontend-pages-whatsapp` | **Date**: 2026-05-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/009-frontend-pages-whatsapp/spec.md`

## Summary

Build and integrate the remaining React/Next.js frontend views (`Follow-ups`, `Campaigns`, `Workflows`, `Knowledge Base`, `Approvals`, `Reports`, and `Settings`) as modular packages (`src/packages/management/` and `src/packages/settings/`) with decoupled Vanilla CSS Modules. Implement active polling settings to link WhatsApp via a dynamically rendered QR code using the backend session Gateway APIs.

## Technical Context

**Language/Version**: JavaScript/TypeScript (Node.js v20+, React 19, Next.js 15), C# (.NET 9.0) for backend.

**Primary Dependencies**:
- `axios` (v1.7) for authenticated relative HTTP requests.
- `lucide-react` (v0.400) for standard layout and UI icons.
- `Vanilla CSS` Modules (`.module.css`) for encapsulated component layout rules.

**Storage**: LocalStorage for tokens (`accessToken`, `refreshToken`, `user`) and `activeProject` context.

**Testing**: Pytest integration tests under `tests/phase_6/` checking endpoint compliance.

**Target Platform**: Linux Server (Docker/Docker Compose, Nginx).

**Project Type**: Next.js App Router (Single-Page Application pages wrapper).

**Performance Goals**:
- Page load times under 800ms.
- QR code rendering instantly from string payload.
- Connection status updates within 5s polling intervals.

**Constraints**: Every outgoing API request MUST automatically include `Authorization` (Bearer JWT) and `X-Project-Id` headers.

## Constitution Check

- **Modular Monolith**: The frontend communicates only with the Nginx reverse proxy, preserving backend modularity boundaries.
- **Strict Multi-Tenant Project Isolation**: Every page uses the `activeProject` ID context and sends it via request headers.
- **Gemini 3.5 Flash Unified AI Engine**: The Knowledge page supports uploading text files that are processed and re-indexed to trigger the brain sync.
- **Human-Like Messaging and Aggregation**: The Campaigns page restricts campaign templates and schedules outbound queues.
- **Risk-Based Action Approval System**: The Approvals page displays the AI auto-reply proposals queue, enabling supervisors to accept/reject drafts.

## Project Structure

### Documentation (this feature)

```text
specs/009-frontend-pages-whatsapp/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technology decisions & rationale
├── data-model.md        # Client-side data models & API responses
├── quickstart.md        # Developer setup & validation commands
└── checklists/
    └── requirements.md  # Specification Quality Checklist
```

### Source Code (repository root)

```text
frontend/
└── src/
    ├── app/
    │   └── (dashboard)/
    │       ├── settings/
    │       │   └── page.tsx           # Thin wrapper importing settings module
    │       └── management/
    │           ├── follow-ups/
    │           │   └── page.tsx       # Thin wrapper importing follow-ups module
    │           ├── campaigns/
    │           │   └── page.tsx       # Thin wrapper importing campaigns module
    │           ├── workflows/
    │           │   └── page.tsx       # Thin wrapper importing workflows module
    │           ├── knowledge/
    │           │   └── page.tsx       # Thin wrapper importing knowledge module
    │           ├── approvals/
    │           │   └── page.tsx       # Thin wrapper importing approvals module
    │           └── reports/
    │               └── page.tsx       # Thin wrapper importing reports module
    └── packages/
        ├── settings/
        │   ├── Settings.tsx           # WhatsApp QR link panel & general options
        │   └── settings.module.css
        └── management/
            ├── FollowUps.tsx          # Follow-ups schedule list & completion UI
            ├── Campaigns.tsx          # Marketing campaigns list & creation form
            ├── Workflows.tsx          # Trigger-Action workflows configuration list
            ├── KnowledgeBase.tsx      # Document upload registry & brain sync trigger
            ├── Approvals.tsx          # Supervisor AI auto-reply proposals queue
            ├── Reports.tsx            # Operations & AI performance metrics cards
            └── management.module.css  # Shared CSS rules for management package views
```

**Structure Decision**: Web application layout. Create routing files as thin wrappers importing functional presentation logic from the feature packages directory (`src/packages/`). Encapsulate all styles inside CSS Modules (`.module.css`).

## Complexity Tracking

*No violations of the Constitution identified.*
