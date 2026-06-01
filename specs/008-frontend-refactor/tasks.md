# Spec Kit Preparation Workflow / سير عمل إعداد Spec Kit

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Technical Planning (`speckit-plan`)
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`)

---

# Tasks: Frontend Clean Code, Modular Packaging & CSS Separation

**Input**: Design documents from `/specs/008-frontend-refactor/`

**Prerequisites**: plan.md (required), spec.md (required)

**Organization**: Tasks are grouped by component package to enable clean code execution and modular testing.

---

## Phase 1: Reorganization Setup

**Purpose**: Set up directory hierarchies

- [x] T001 Create folders:
  - `frontend/src/packages/auth`
  - `frontend/src/packages/dashboard`
  - `frontend/src/packages/inbox`
  - `frontend/src/packages/crm`
  - `frontend/src/packages/error`
  - `frontend/src/components/layout`
  - `frontend/src/components/shared`

---

## Phase 2: Auth Feature Package (`auth`)

**Purpose**: Move Login and Register screens to auth package and convert to CSS Modules

- [x] T002 Create auth CSS Module: `frontend/src/packages/auth/auth.module.css` containing styles for login/registration forms, inputs, background glow, cards, links, and action buttons.
- [x] T003 Create Login component: `frontend/src/packages/auth/Login.tsx` with form state, submission callbacks, layout structure, and class names mapped from `auth.module.css`.
- [x] T004 Create Register component: `frontend/src/packages/auth/Register.tsx` with agent registration details, submission callbacks, and class names mapped from `auth.module.css`.
- [x] T005 Update Login router entry: `frontend/src/app/page.tsx` to import and render `<Login />`.
- [x] T006 Update Register router entry: `frontend/src/app/register/page.tsx` to import and render `<Register />`.

---

## Phase 3: General App Shell Components (`layout`)

**Purpose**: Extract Sidebar and Header components into modular, CSS Module styled files

- [x] T007 Create Layout CSS Module: `frontend/src/components/layout/layout.module.css` with layout navigation list, projects selector drawer, presence ring, collapsible panels, and responsive margins.
- [x] T008 Create Sidebar component: `frontend/src/components/layout/Sidebar.tsx` encapsulating app navigation and toggle icons, styled via `layout.module.css`.
- [x] T009 Create Header component: `frontend/src/components/layout/Header.tsx` encapsulating active project selector, agent profile info, and online status badges, styled via `layout.module.css`.
- [x] T010 Update layout router file: `frontend/src/app/(dashboard)/layout.tsx` to import and mount `<Sidebar />` and `<Header />`, wrapping the children.

---

## Phase 4: Dashboard Feature Package (`dashboard`)

**Purpose**: Extract metrics dashboard and map to CSS Modules

- [x] T011 Create Dashboard CSS Module: `frontend/src/packages/dashboard/dashboard.module.css` containing layout grids, performance metrics KPI card shapes, hover transitions, and action lists.
- [x] T012 Create Dashboard component: `frontend/src/packages/dashboard/Dashboard.tsx` with analytical summary widgets, project calculations triggers, and class names mapped from `dashboard.module.css`.
- [x] T013 Update Dashboard router entry: `frontend/src/app/(dashboard)/dashboard/page.tsx` to import and render `<Dashboard />`.

---

## Phase 5: Real-Time Chat Feature Package (`inbox`)

**Purpose**: Refactor 3-panel realtime chat component and separate CSS

- [x] T014 Create Inbox CSS Module: `frontend/src/packages/inbox/inbox.module.css` containing 3-panel split columns, user chat lists, bubble messages logs (incoming/outgoing), attachments triggers, and smart replies.
- [x] T015 Create Inbox component: `frontend/src/packages/inbox/Inbox.tsx` containing conversations logs, SignalR state triggers, Gemini suggestion pickers, and class names mapped from `inbox.module.css`.
- [x] T016 Update Inbox router entry: `frontend/src/app/(dashboard)/inbox/page.tsx` to import and render `<Inbox />`.

---

## Phase 6: CRM Feature Package (`crm`) & Shared Profile Drawer (`CustomerDetail`)

**Purpose**: Reorganize customer registries and pipelines into crm package, and drawer into shared components

- [x] T017 Create Customer Detail CSS Module: `frontend/src/components/shared/customer-detail.module.css` with sidebar detail inputs, follow-ups tables, lead score, and tag badges.
- [x] T018 Move and refactor CustomerDetail: `frontend/src/components/shared/CustomerDetail.tsx` (moved from `frontend/src/components/CustomerDetail.tsx`) using class names mapped from `customer-detail.module.css`.
- [x] T019 Create CRM CSS Module: `frontend/src/packages/crm/crm.module.css` containing search inputs, data grids, opportunity sums headers, and Kanban column deal boards.
- [x] T020 Create CustomerList component: `frontend/src/packages/crm/CustomerList.tsx` showing contact directories, and class names mapped from `crm.module.css`.
- [x] T021 Create PipelineBoard component: `frontend/src/packages/crm/PipelineBoard.tsx` showing Kanban pipeline, and class names mapped from `crm.module.css`.
- [x] T022 Update CRM router entry: `frontend/src/app/(dashboard)/crm/page.tsx` to render `<CustomerList />`.
- [x] T023 Update Pipeline router entry: `frontend/src/app/(dashboard)/crm/pipeline/page.tsx` to render `<PipelineBoard />`.
- [x] T024 Delete deprecated file: `frontend/src/components/CustomerDetail.tsx` to keep the code clean.

---

## Phase 7: Error Fallback Package (`error`)

**Purpose**: Reorganize Next.js error fallback page

- [x] T025 Create Error CSS Module: `frontend/src/packages/error/error-boundary.module.css` containing error layouts, trace boxes, glowing neon warning icons, and button actions.
- [x] T026 Create ErrorBoundary component: `frontend/src/packages/error/ErrorBoundary.tsx` wrapping runtime exception diagnostics.
- [x] T027 Update router error handler: `frontend/src/app/error.tsx` to import and render `<ErrorBoundary />`.

---

## Phase 8: Operations, Verification & Compilation

**Purpose**: Build confirmation and tests runs

- [x] T028 Check Next.js build compilation: Run `npm run build` inside `frontend/` directory to verify there are zero TypeScript compiler warnings or CSS bundling errors.
- [x] T029 Run all tests: Run `make test-all` from root workspace directory to verify all 50 integration tests pass cleanly.
