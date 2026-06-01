# Tasks: Frontend Management Pages & WhatsApp QR Connectivity

**Input**: Design documents from `/specs/009-frontend-pages-whatsapp/`

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification
- [x] Phase 2: Technical Planning
- [x] Phase 3: Detailed Task Breakdown

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create Next.js App Router page entry files.

- [ ] T001 Create settings route file `frontend/src/app/(dashboard)/settings/page.tsx` importing and rendering the `<Settings />` component.
- [ ] T002 Create follow-ups route file `frontend/src/app/(dashboard)/management/follow-ups/page.tsx` importing and rendering the `<FollowUps />` component.
- [ ] T003 Create campaigns route file `frontend/src/app/(dashboard)/management/campaigns/page.tsx` importing and rendering the `<Campaigns />` component.
- [ ] T004 Create workflows route file `frontend/src/app/(dashboard)/management/workflows/page.tsx` importing and rendering the `<Workflows />` component.
- [ ] T005 Create knowledge route file `frontend/src/app/(dashboard)/management/knowledge/page.tsx` importing and rendering the `<KnowledgeBase />` component.
- [ ] T006 Create approvals route file `frontend/src/app/(dashboard)/management/approvals/page.tsx` importing and rendering the `<Approvals />` component.
- [ ] T007 Create reports route file `frontend/src/app/(dashboard)/management/reports/page.tsx` importing and rendering the `<Reports />` component.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Create shared styles for management layouts.

- [ ] T008 [P] Create `frontend/src/packages/management/management.module.css` with classes for layout headers, stats cards grids, lists containers, action buttons, status tags, and form overlays.

---

## Phase 3: User Story 1 - Settings & WhatsApp Connectivity (Priority: P1) 🎯 MVP

**Goal**: Implement WhatsApp connection status checking, session starting, and live QR code polling.

**Independent Test**: Open Settings page, verify "Disconnected" state, click "Link WhatsApp", check that the QR code image appears, and click "Mock Connect" to verify it switches to "Connected" with a phone number.

- [ ] T009 [P] [US1] Create the settings module CSS file `frontend/src/packages/settings/settings.module.css` containing classes for QR scanner card, status indicators, and connection details list.
- [ ] T010 [P] [US1] Create the settings module file `frontend/src/packages/settings/Settings.tsx` rendering basic UI structure.
- [ ] T011 [US1] Implement active polling using `setInterval` every 5 seconds to query `/api/whatsapp/session/status` in `frontend/src/packages/settings/Settings.tsx`.
- [ ] T012 [US1] Implement session initialization calling `POST /api/whatsapp/session/start` and polling `/api/whatsapp/session/qr` when in `Initializing` state in `frontend/src/packages/settings/Settings.tsx`.
- [ ] T013 [US1] Implement mock connection triggers calling `POST /api/whatsapp/session/mock` to simulate successful scan in `frontend/src/packages/settings/Settings.tsx`.

---

## Phase 4: User Story 2 - AI Approvals Queue (Priority: P2)

**Goal**: Support reviewing and approving/rejecting message replies drafted by the AI engine.

**Independent Test**: Navigate to Approvals, see pending drafts, click "Approve" or "Reject", and confirm they disappear from the list.

- [ ] T014 [P] [US2] Create the Approvals package module file `frontend/src/packages/management/Approvals.tsx` mapping to list container styles.
- [ ] T015 [US2] Implement api calls fetching pending approvals from `/api/projects/{projectId}/approvals?status=Pending` in `frontend/src/packages/management/Approvals.tsx`.
- [ ] T016 [US2] Implement event handlers for Accept/Reject calling `/api/projects/{projectId}/approvals/{id}/action` in `frontend/src/packages/management/Approvals.tsx`.

---

## Phase 5: User Story 3 - Follow-ups, Campaigns & Workflows (Priority: P2)

**Goal**: Support completing follow-up tasks, scheduling campaigns, and viewing trigger-action automation rules.

**Independent Test**: Complete a follow-up task, fill and submit the Campaign creation form, and view active automation workflows.

- [ ] T017 [P] [US3] Create the FollowUps module file `frontend/src/packages/management/FollowUps.tsx` rendering pending follow-up schedules.
- [ ] T018 [US3] Implement action handler for complete button calling `/api/projects/{projectId}/follow-ups/{id}/complete` in `frontend/src/packages/management/FollowUps.tsx`.
- [ ] T019 [P] [US3] Create the Campaigns module file `frontend/src/packages/management/Campaigns.tsx` displaying active campaigns and a creation modal form.
- [ ] T020 [US3] Implement form submission calling `POST /api/projects/{projectId}/campaigns` to schedule a campaign in `frontend/src/packages/management/Campaigns.tsx`.
- [ ] T021 [P] [US3] Create the Workflows module file `frontend/src/packages/management/Workflows.tsx` listing trigger-action automation rules.

---

## Phase 6: User Story 4 - Knowledge Base & Reports Dashboard (Priority: P3)

**Goal**: Manage company lookup documents, trigger AI brain reindexing, and view analytics reports.

**Independent Test**: Upload a document, trigger a brain sync, and verify reports cards render daily operations metrics.

- [ ] T022 [P] [US4] Create the KnowledgeBase module file `frontend/src/packages/management/KnowledgeBase.tsx` supporting document lists, uploads, and a "Sync Brain" action.
- [ ] T023 [US4] Implement brain sync API call posting to `/api/projects/{projectId}/brain/sync` in `frontend/src/packages/management/KnowledgeBase.tsx`.
- [ ] T024 [P] [US4] Create the Reports module file `frontend/src/packages/management/Reports.tsx` rendering operations and AI performance metrics.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Verify warning-free compilation and page load states.

- [ ] T025 Run Next.js production compiler check and resolve all TypeScript / Turbopack warnings.
- [ ] T026 Execute manual page verification in a web browser using the credentials in `quickstart.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately.
- **Foundational (Phase 2)**: Depends on Setup completion.
- **User Story 1 (Phase 3)**: Depends on Foundational completion.
- **User Stories 2-4 (Phases 4-6)**: Depend on Foundational completion, can be implemented in parallel.
- **Polish (Phase 7)**: Depends on all user stories being completed.

### Parallel Opportunities

- All route setup tasks (T001-T007) can be created in parallel.
- Settings page styling (T009) and basic logic (T010) can start in parallel.
- Once Phase 2 completes, Developer A can implement US1 Settings (T011-T013) while Developer B implements US2 Approvals (T014-T016).
