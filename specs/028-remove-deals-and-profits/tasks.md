# Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Arabic Clarification (`speckit-clarify`)
- [x] Phase 3: Technical Planning (`speckit-plan`)
- [x] Phase 4: Detailed Task Breakdown (`speckit-tasks`)

---

# Tasks: remove-deals-and-profits

**Input**: Design documents from `/specs/028-remove-deals-and-profits/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

---

## Phase 1: Setup (Shared Infrastructure)

- [x] T001 Verify that backend compiles before changes using command `dotnet build` inside the `backend/` directory

---

## Phase 2: Foundational (Blocking Prerequisites)

- [x] T002 Establish fallback logic for client stages (No database edits) so that it passes silently

---

## Phase 3: User Story 1 - Clean Sidebar Navigation (Priority: P1)

**Goal**: Remove the "مسار الصفقات" (Pipeline Board) option from the sidebar layouts.

- [x] T003 Remove the navigation item for "مسار الصفقات" from `navItems` array in `frontend/src/app/(dashboard)/layout.tsx` to verify it passes successfully
- [x] T004 Verify that the menu item is no longer visible in desktop and mobile layouts with expected results

---

## Phase 4: User Story 2 - Clean Dashboard Stats & Metrics (Priority: P1)

**Goal**: Remove Open Deals and Closed Revenue/Profits cards from the main dashboard.

- [x] T005 Remove the "الصفقات المفتوحة" card from `frontend/src/packages/dashboard/Dashboard.tsx`
- [x] T006 Remove the "الإيراد المغلق" card from `frontend/src/packages/dashboard/Dashboard.tsx`
- [x] T007 Remove unused local states/variables `deals`, `activeDeals`, `closedWonDeals`, `revenue` from `frontend/src/packages/dashboard/Dashboard.tsx` to prevent build warnings with expected outcome

---

## Phase 5: User Story 3 - Clean Dashboard Quick Actions (Priority: P2)

**Goal**: Remove the "مسار الصفقات" quick action link card from the dashboard.

- [x] T008 Remove the button for `/crm/pipeline` from the "إجراءات سريعة" section in `frontend/src/packages/dashboard/Dashboard.tsx` to ensure it passes

---

## Phase 6: User Story 4 - Clean CRM Client Detailed Panel (Priority: P2)

**Goal**: Remove Budget and Pipeline Stage inputs from the customer detailed modal page.

- [x] T009 Remove the "الميزانية ($)" form group section from `frontend/src/components/shared/CustomerDetail.tsx`
- [x] T010 Remove the "مرحلة مسار المبيعات (Pipeline Stage)" form group section from `frontend/src/components/shared/CustomerDetail.tsx`
- [x] T011 Update the customer save submission payload in `handleSave` in `frontend/src/components/shared/CustomerDetail.tsx` to not send or alter the budget and pipelineStage fields, or let them fall back to their current values to achieve expected results

---

## Phase 7: Polish & Cross-Cutting Concerns

- [x] T012 Run a local build check using command `npm run build` inside the `frontend/` directory to verify it passes
- [x] T013 Update `docs/frontend_plan.md` to document the completed UI modifications

---

## Phase 8: Final Review & Verification

- [x] T014 Execute deep critique and fixes to verify results
- [x] T015 Run `clean-code-guard` against changed TSX files to confirm they conform to clean coding rules
- [x] T016 Run `test-guard` (since no tests are requested, document N/A) and check that it passes
- [x] T017 Execute the manual feature tests checklist (from `specs/028-remove-deals-and-profits/quickstart.md`)
- [x] T018 Run the verification command `npm run build` inside `frontend/` to confirm that frontend builds successfully with expected result
