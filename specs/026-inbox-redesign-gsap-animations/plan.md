# Implementation Plan: UX/UI Unified Inbox Redesign & GSAP Animations

**Branch**: `026-inbox-redesign-gsap-animations` | **Date**: 2026-06-21 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/026-inbox-redesign-gsap-animations/spec.md`

## Summary

Redesign the inbox routes (WhatsApp, Messenger, Comments) to render a unified, modern dark/light layout exactly like the screenshot. To optimize reusability, create a unified `InboxLayout` structure. To support the right context panel, extend the EF Core C# `Customer` model with `PurchaseProbability`, `AIInsights`, and `AutomationRules` columns, generating a database migration. Introduce `gsap` and `@gsap/react` for fade-ins, active card animations, and panel transitions. Additionally, unify the global CSS variables and refactor CRM/Settings modules to automatically propagate this dark neon premium theme across all remaining dashboard sidebar pages.

## Technical Context

**Language/Version**: C# (.NET 8), TypeScript (Next.js 16 / React 19)

**Primary Dependencies**: `gsap`, `@gsap/react`, Entity Framework Core

**Storage**: PostgreSQL (primary with EF Core migrations)

**Testing**: build verify, lint verify, visual layout manual inspection

**Target Platform**: Linux server (Docker Compose) / Web browsers

**Project Type**: Web application (Modular Monolith backend + Next.js frontend)

**Performance Goals**: GSAP transitions completed under 0.4s; pages loading under 1.0s.

**Constraints**: Keep modular monolith boundary clean; enforce strict multi-tenant isolation via ProjectId.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Modular Monolith | ✅ PASS | Changes are localized within the existing `Conversations` domain (Customer entity) and `CRM` module controllers without adding direct references between database tables. |
| II. Multi-Tenant Isolation | ✅ PASS | All customer queries and API endpoints are strictly scoped to project context using EF Core's tenant filters and ProjectId validation. |
| III. Gemini 3.5 Unified AI Engine | ✅ PASS | Custom AI insights in the right sidebar are populated asynchronously via the existing Gemini background service workers. |
| IV. Human-Like Messaging | ✅ PASS | Message aggregation and human-like typing engines are preserved. |
| V. Risk-Based Action Approval | ✅ PASS | The right context panel only displays metrics and tasks; no critical action pathways are triggered without the existing approvals filter. |

## Project Structure

### Documentation (this feature)

```text
specs/026-inbox-redesign-gsap-animations/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-contracts.md
└── tasks.md             # Phase 2 output (created by /speckit-tasks)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── Conversations/
│   │   │   └── Domain/
│   │   │       └── Customer.cs     # MODIFIED: +PurchaseProbability, +AIInsights, +AutomationRules
│   │   └── CRM/
│   │       └── API/
│   │           └── CRMController.cs # MODIFIED: DTOs updated to expose extended customer properties
│   └── Shared/
│       └── Infrastructure/
│           └── AppDbContext.cs     # UNCHANGED (auto-applies migrations)

frontend/
├── package.json                    # MODIFIED: +gsap, +@gsap/react
├── src/
│   ├── styles/
│   │   └── variables.css           # MODIFIED: Redesign global HSL and OKLCH color variables to match dark neon theme
│   ├── services/
│   │   └── crm.ts                  # MODIFIED: updated Customer type definitions
│   ├── packages/
│   │   ├── inbox/
│   │   │   ├── InboxLayout.tsx     # NEW: Unified 4-panel viewport component
│   │   │   ├── Inbox.tsx           # MODIFIED: Re-engineered under InboxLayout wrapper
│   │   │   ├── MessengerInbox.tsx  # MODIFIED: Re-engineered under InboxLayout wrapper
│   │   │   ├── CommentsInbox.tsx   # MODIFIED: Re-engineered under InboxLayout wrapper
│   │   │   └── inbox.module.css    # MODIFIED: Stylesheet containing colors, layouts, animations
│   │   ├── crm/
│   │   │   └── crm.module.css      # MODIFIED: Clean up hardcoded cyan/pink colors
│   │   └── settings/
│   │       └── settings.module.css # MODIFIED: Clean up hardcoded cyan colors
```

**Structure Decision**: Web application with backend C# modular monolith updates and Next.js frontend updates containing a unified CSS and components structure.

## Complexity Tracking

*No constitution violations present. All designs match existing patterns.*
