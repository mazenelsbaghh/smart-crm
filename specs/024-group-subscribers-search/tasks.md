# Tasks: Group Subscribers Search

**Input**: Design documents from `/specs/024-group-subscribers-search/`

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Arabic Clarification (`speckit-clarify`)
- [x] Phase 3: Technical Planning (`speckit-plan`)
- [x] Phase 4: Detailed Task Breakdown (`speckit-tasks`)

---

## Phase 1: Setup

- [x] T001 Verify specs directory and plan/research files are active

---

## Phase 2: Foundational

- [x] T002 Verify local development environment is ready for testing

---

## Phase 3: User Story 1 - Search Group Participants by Name or Phone (Priority: P1) 🎯 MVP

**Goal**: Implement client-side filtering on the group participants list panel using an input text search bar.

**Independent Test**: Filter by typing and matching participant name or phone, checking that results update instantly and reset when closed.

### Implementation for User Story 1

- [x] T003 [US1] Define `searchQuery` state using `useState` in `frontend/src/packages/settings/GroupAppointmentsManager.tsx`.
- [x] T004 [US1] Create `filteredBookings` computed variable in `frontend/src/packages/settings/GroupAppointmentsManager.tsx` to filter list by name and phone.
- [x] T005 [US1] Add the search input UI element in `frontend/src/packages/settings/GroupAppointmentsManager.tsx` with classes and styles.
- [x] T006 [US1] Display no results message in `frontend/src/packages/settings/GroupAppointmentsManager.tsx` if search returns empty.
- [x] T007 [US1] Reset `searchQuery` state on group close/change in `frontend/src/packages/settings/GroupAppointmentsManager.tsx`.

---

## Phase 4: Polish & QA

- [x] T008 Run deep critique fixes on the implementation.
- [x] T009 Run `clean-code-guard` against changed code.
- [x] T010 Run `test-guard` against changed tests.
- [x] T011 Run feature tests and document verification results.
- [x] T012 Run final build verification checks.
- [x] T013 Run deployment script `deploy/deploy.sh` to publish changes.

---

## Dependencies & Execution Order

- **Phase 3**: User Story 1 (T003 - T007) is sequential.
- **Phase 4**: Polish (T008 - T013) must run after Phase 3 is completed.
