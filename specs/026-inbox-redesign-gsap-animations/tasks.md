# Tasks: UX/UI Unified Inbox Redesign & GSAP Animations

**Input**: Design documents from `/specs/026-inbox-redesign-gsap-animations/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

---

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Arabic Clarification (`speckit-clarify`)
- [x] Phase 3: Technical Planning (`speckit-plan`)
- [x] Phase 4: Detailed Task Breakdown (`speckit-tasks`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Frontend dependencies installation

- [x] T001 Install `gsap` and `@gsap/react` dependencies in `frontend/package.json`

---

## Phase 2: Foundational (C# Backend & EF Core Migration)

**Purpose**: Database extensions and C# API updates

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T002 Update C# `Customer.cs` domain model in `backend/src/Modules/Conversations/Domain/Customer.cs` to add `PurchaseProbability`, `AIInsights`, and `AutomationRules` fields.
- [x] T003 Generate EF Core migration by running `dotnet ef migrations add AddCrmExtraFields` inside `backend/` directory, and apply it to update database.
- [x] T004 Update C# `CRMController.cs` in `backend/src/Modules/CRM/API/CRMController.cs` to expose and map the new fields in `GetCustomers`, `GetCustomer`, and `UpdateCustomer` endpoints, including updating `UpdateCustomerRequest` DTO.

**Checkpoint**: Foundation ready - C# data structure and API endpoints fully support the new CRM attributes.

---

## Phase 3: User Story 1 - Unified Layout and CSS Styling (Priority: P1)

**Goal**: Implement the generic layout structure and colors matching the screenshot

**Independent Test**: Verify that the layout splits correctly into the 4 regions (8%, 22%, 45%, 25%) and renders matching dark-mode panels and off-white workspace.

- [x] T005 Update frontend service definitions in `frontend/src/services/crm.ts` to include new Customer fields.
- [x] T006 Create `frontend/src/packages/inbox/InboxLayout.tsx` orchestrating Left Sidebar, Conversation List, Chat/Timeline Workspace, and Right Context Panel.
- [x] T007 Design the stylesheet in `frontend/src/packages/inbox/inbox.module.css` with dark matte background (`#0F1115`), transparent dark gray cards (`#171A21`), borders (`rgba(255,255,255,.06)`), Workspace Off-white background (`#F8F8F6`), outgoing messages (`#D8F15D`), and incoming (`#1D2430`).

**Checkpoint**: UI Layout matches the visual requirements and layout distribution.

---

## Phase 4: User Story 2 - UI Channel Integration & Unification (Priority: P1)

**Goal**: Move WhatsApp, Messenger, and Comments inboxes to use the new layout

**Independent Test**: Verify that all three routes (/inbox, /inbox/messenger, /inbox/comments) render correctly with their active messages.

- [x] T008 Integrate WhatsApp Inbox in `frontend/src/packages/inbox/Inbox.tsx` with the new `InboxLayout`.
- [x] T009 Integrate Messenger Inbox in `frontend/src/packages/inbox/MessengerInbox.tsx` with the new `InboxLayout`.
- [x] T010 Integrate Comments Inbox in `frontend/src/packages/inbox/CommentsInbox.tsx` with the new `InboxLayout`.
- [x] T011 Implement the Right Smart Sidebar detail updates, connecting save handlers back to the `crmService.updateCustomer` API.

**Checkpoint**: All three inboxes are fully functional under the unified design.

---

## Phase 5: User Story 3 - GSAP Animations (Priority: P2)

**Goal**: Implement micro-interactions and transitions with GSAP

**Independent Test**: Inspect entrance animations on page load, active worklist selection changes, and sidebar transitions.

- [x] T012 Add staggered slide-up animations for the top metrics cards (Worklist, New leads, etc.) using GSAP.
- [x] T013 Add smooth transition animations when switching selected conversations (active card transitions to lime green background).
- [x] T014 Add slide-in transitions for the right context panel cards and micro-bounce hovers on circular quick buttons.

**Checkpoint**: GSAP animations render smoothly without memory leaks or layout shift.

---

## Phase 5.5: Dashboard & CRM Page Theme Redesign (Priority: P1)

**Goal**: Apply unified dark neon crm theme to all remaining sidebar pages.

**Independent Test**: Load /crm and /settings and verify the theme aligns perfectly with the inbox.

- [x] T020 Redesign global CSS variables inside `frontend/src/styles/variables.css` (redefine HSL and OKLCH properties, make body background flat `#0F1115`).
- [x] T021 Refactor `frontend/src/packages/crm/crm.module.css` to replace all hardcoded blue/cyan (`rgba(0, 243, 255)`) and pink (`rgba(255, 0, 127)`) color codes with standard CSS variables.
- [x] T022 Refactor `frontend/src/packages/settings/settings.module.css` to replace all hardcoded blue/cyan (`rgba(0, 243, 255)`) color codes with standard CSS variables.
- [x] T023 Walk through and visually audit all sidebar layouts, metrics cards, table styles, and page titles to ensure layout is flawless.

**Checkpoint**: All sidebar pages render matching the dark neon premium CRM theme.

---

## Phase 6: Polish & Verification

- [x] T015 Perform a deep critique of the implemented UI/UX and fix layout/styling bugs (deep critique)
- [x] T016 Run `clean-code-guard` against changed code files (clean-code-guard)
- [x] T017 Run `test-guard` against changed test files (test-guard - skipped per user request)
- [x] T018 Run feature tests to verify the UI redesign and animations (feature tests - skipped per user request)
- [x] T019 Execute full backend build and Next.js frontend compile checks (build verification - validated by successful deployment)

### Verification Commands & Expected Outcomes
- To check backend correctness, run: `dotnet build` inside the `backend/` directory. The expected outcome is a successful build with zero errors.
- To check frontend correctness, run: `npm run build` inside the `frontend/` directory. The expected result is a successful build with zero typescript compilation errors.
- To check styling and lint rules, run: `npm run lint` inside the `frontend/` directory. The expected result is that all lint checks passes.

## Dependencies & Execution Order

1. **Phase 1 (Setup)** must complete first.
2. **Phase 2 (Foundational)** runs second, blocking frontend integration.
3. **Phase 3 (User Story 1)** and **Phase 4 (User Story 2)** run sequentially to establish the UI container and connect active channels.
4. **Phase 5 (GSAP Animations)** overlays motion states once layout is functional.
5. **Phase 6 (Polish)** executes final verification.
