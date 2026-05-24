# Spec Kit Preparation Workflow / سير عمل إعداد Spec Kit

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Technical Planning (`speckit-plan`)
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`)

---

# Tasks: Frontend Dashboard, Realtime & Production Hardening

**Input**: Design documents from `/specs/007-frontend-production/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are included to verify SignalR realtime messaging, production hardening (CORS, SSL, Rate limiting), API integration, and backup/restore.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Frontend**: `frontend/`
- **Nginx configuration**: `nginx/`
- **Deployment scripts**: `deploy/`
- **Tests**: `tests/phase_6/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [ ] T001 Create `frontend/` directory structure containing app router folders (`app/`, `src/components/`, `src/services/`, `src/context/`, `src/styles/`)
- [ ] T002 Initialize Next.js TypeScript project inside `frontend/` with `package.json` specifying react, next, `@microsoft/signalr`, `axios`, and `lucide-react`
- [ ] T003 [P] Configure ESLint and Prettier rules in `frontend/eslint.config.mjs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T004 Configure global CSS styling with variables (design tokens for colors, typography, margins, grids) in `frontend/src/styles/variables.css`
- [ ] T005 [P] Implement authentication client service (login, token storage, request/response interceptors for token refresh) in `frontend/src/services/auth.ts`
- [ ] T006 [P] Implement API communication client and axios instance with JWT header support in `frontend/src/services/api.ts`
- [ ] T007 Implement `AuthContext` provider to handle global logged-in state and current project switching in `frontend/src/context/auth-context.tsx`
- [ ] T008 Add authentication page `frontend/app/page.tsx` (Login form) and registration page `frontend/app/register/page.tsx`
- [ ] T009 Add App layout with sidebar navigation, header context selector, and mobile responsive shell in `frontend/app/layout.tsx`

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Realtime Agent Inbox (Priority: P1) 🎯 MVP

**Goal**: Support agents can manage communications in real-time, review AI suggestions, upload media attachments, and track customer contexts.

**Independent Test**: Log into the application as an agent, open the inbox page, send a message to a mock client, verify real-time SignalR status updates, and view AI suggestions.

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T010 [P] [US1] Write SignalR connection lifecycle and real-time event tests in `tests/phase_6/test_signalr.py`

### Implementation for User Story 1

- [ ] T011 [P] [US1] Create the `Message` and `Conversation` typescript type interfaces in `frontend/src/types/chat.ts`
- [ ] T012 [P] [US1] Implement SignalR connection client (connect, disconnect, join group, invoke hubs) in `frontend/src/services/signalr.ts`
- [ ] T013 [US1] Create Inbox layout (3-panel grid: sidebar lists conversations, middle panel displays message log, right panel displays customer details) in `frontend/app/inbox/page.tsx`
- [ ] T014 [US1] Implement live updates via SignalR events (`ReceiveMessage`, `ConversationStatusChanged`) in `frontend/app/inbox/page.tsx`
- [ ] T015 [US1] Add message composer with send logic, attachment triggers, and Gemini AI suggestion picker in `frontend/app/inbox/page.tsx`

**Checkpoint**: At this point, User Story 1 should be fully functional and testable independently

---

## Phase 4: User Story 2 - Dashboard KPI & CRM Management (Priority: P2)

**Goal**: Administrators and supervisors see dashboard operational charts, customer list, and CRM pipeline Kanban board.

**Independent Test**: Load the dashboard to view KPI metric updates, navigate to CRM pages, search/filter customers, and drag customer cards to update pipeline stages.

### Tests for User Story 2

- [ ] T016 [P] [US2] Write frontend API client verification tests in `tests/phase_6/test_frontend_api.py`

### Implementation for User Story 2

- [ ] T017 [P] [US2] Implement customer retrieval and CRM updates client functions in `frontend/src/services/crm.ts`
- [ ] T018 [US2] Add metrics cards and quick actions widget to dashboard in `frontend/app/dashboard/page.tsx`
- [ ] T019 [US2] Create customer listing, searching, filtering, and tag management interface in `frontend/app/crm/page.tsx`
- [ ] T020 [US2] Build Kanban board pipeline layout (drag-and-drop or card move indicators) in `frontend/app/crm/pipeline/page.tsx`
- [ ] T021 [US2] Build customer detail drawer/modal showing notes, scores, and follow-ups in `frontend/src/components/CustomerDetail.tsx`

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - Production Hardening & Deployment Automation (Priority: P3)

**Goal**: Run the application in a secure production environment using Nginx reverse proxies and execute automated system backups.

**Independent Test**: Hit the API with rapid curl requests to trigger rate limits, run backup script, and verify system state recovery from the generated backup package.

### Tests for User Story 3

- [ ] T022 [P] [US3] Write Nginx rate-limiting, CORS, and SSL integration tests in `tests/phase_6/test_production.py`
- [ ] T023 [P] [US3] Write backup and restore validation tests in `tests/phase_6/test_deployment.py`

### Implementation for User Story 3

- [ ] T024 [P] [US3] Create Nginx proxy configuration file with TLS redirects, CORS header verification, and rate limiting zones in `nginx/production.conf`
- [ ] T025 [P] [US3] Create `deploy/docker-compose.production.yml` adding Nginx, frontend service, and production ports mapping
- [ ] T026 [US3] Build automated database, cache, and object storage backup utility in `deploy/backup.sh`
- [ ] T027 [US3] Build backup restore validation utility in `deploy/restore.sh`
- [ ] T028 [US3] Configure error page response fallback layout in `frontend/app/error.tsx`

**Checkpoint**: All user stories should now be independently functional

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Improvements that affect multiple user stories

- [ ] T029 [P] Document frontend endpoints and production installation procedures in `docs/frontend_plan.md`
- [ ] T030 [P] Implement fluid UI hover transitions, glassmorphic layout elements, and micro-animations for message sending and tab switching
- [ ] T031 Run standard `npm run build` production check in `frontend/` to ensure zero bundling or compilation errors
- [ ] T032 Execute all tests via `make test-phase-6` and verify clean passing checks

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3+)**: All depend on Foundational phase completion
  - User stories can then proceed in parallel (if staffed)
  - Or sequentially in priority order (P1 → P2 → P3)
- **Polish (Final Phase)**: Depends on all desired user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P2)**: Can start after Foundational (Phase 2) - May integrate with US1 but should be independently testable
- **User Story 3 (P3)**: Can start after Foundational (Phase 2) - May integrate with US1/US2 but should be independently testable

### Within Each User Story

- Tests (if included) MUST be written and FAIL before implementation
- Models before services
- Services before endpoints
- Core implementation before integration
- Story complete before moving to next priority

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks marked [P] can run in parallel (within Phase 2)
- Once Foundational phase completes, all user stories can start in parallel (if team capacity allows)
- All tests for a user story marked [P] can run in parallel
- Models within a story marked [P] can run in parallel
- Different user stories can be worked on in parallel by different team members

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Write SignalR connection lifecycle and real-time event tests in tests/phase_6/test_signalr.py"

# Launch all models/interfaces for User Story 1 together:
Task: "Create the Message and Conversation typescript type interfaces in frontend/src/types/chat.ts"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently
5. Deploy/demo if ready

### Incremental Delivery

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → Deploy/Demo (MVP!)
3. Add User Story 2 → Test independently → Deploy/Demo
4. Add User Story 3 → Test independently → Deploy/Demo
5. Each story adds value without breaking previous stories
