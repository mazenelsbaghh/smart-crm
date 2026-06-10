# Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Technical Planning (`speckit-plan`)
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`)

---

# Tasks: Flutter Mobile Application for Smart CRM

**Input**: Design documents from `/specs/022-flutter-mobile-app/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 Scaffold Flutter mobile application at mobile_app/ using `flutter create --org com.smartcrm.app --project-name mobile_app ./mobile_app`
- [x] T002 Update dependency configurations in mobile_app/pubspec.yaml to include flutter_bloc, dio, signalr_netcore, go_router, flutter_secure_storage, shared_preferences, google_fonts, table_calendar, fl_chart, equatable, and intl.
- [x] T003 Configure code formatting rules and lint parameters in mobile_app/analysis_options.yaml

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Create custom brand-tinted color constants using HSL conversions at mobile_app/lib/core/theme/colors.dart
- [x] T005 Create custom typography configs using Google Fonts (Outfit for headings, Inter for body) at mobile_app/lib/core/theme/typography.dart
- [x] T006 Implement secure storage manager service at mobile_app/lib/core/services/secure_storage.dart using flutter_secure_storage for access and refresh JWT token persistence.
- [x] T007 Implement central API client with global headers and 401 retry interceptors at mobile_app/lib/core/services/api_client.dart using dio.
- [x] T008 Implement real-time WebSocket client service at mobile_app/lib/core/services/signalr_service.dart mapping JoinProjectGroup and presence update hub methods.
- [x] T009 Create adaptive base layout template shell with drawer and persistent navigation menus at mobile_app/lib/core/widgets/shell.dart

**Checkpoint**: Foundation ready - user story implementation can now begin

---

## Phase 3: User Story 1 - Secure Access and Project Context Management (Priority: P1) 🎯 MVP

**Goal**: Implement secure login, registration, session token persistence, and multi-tenant project isolation.

**Independent Test**: Build and run the app, register a user, log in, view the project selection page, select a project, and verify it updates the X-Project-Id header on subsequent requests.

### Implementation for User Story 1

- [x] T010 [P] [US1] Implement User and AuthSession serialization models at mobile_app/lib/features/auth/data/models/user_model.dart
- [x] T011 [US1] Create Auth Repository to call REST login/register endpoints at mobile_app/lib/features/auth/data/repositories/auth_repository.dart
- [x] T012 [US1] Create Authentication BLoC state manager at mobile_app/lib/features/auth/bloc/auth_bloc.dart
- [x] T013 [US1] Design Login screen with input validation at mobile_app/lib/features/auth/presentation/login_screen.dart
- [x] T014 [US1] Design Registration screen with validation at mobile_app/lib/features/auth/presentation/register_screen.dart
- [x] T015 [US1] Design Project Selection screen retrieving available tenants at mobile_app/lib/features/auth/presentation/project_select_screen.dart

**Checkpoint**: At this point, User Story 1 is fully functional and testable independently.

---

## Phase 4: User Story 2 - Real-time Chat Inbox & AI-Assisted Agent Operations (Priority: P1)

**Goal**: Build a real-time WhatsApp inbox supporting chat logs, AI smart replies, customer tagging, blacklist toggles, and follow-up scheduling.

**Independent Test**: Connect to the running backend, select a project, verify the inbox list receives real-time messages from SignalR, open a chat thread, verify AI suggestion displays, and tap "Approve & Send".

### Implementation for User Story 2

- [x] T016 [P] [US2] Implement Chat, Message, and AISuggestion serialization models at mobile_app/lib/features/inbox/data/models/chat_models.dart
- [x] T017 [US2] Create Chat Repository mapping conversations, messages, typing indicators, and suggestion updates at mobile_app/lib/features/inbox/data/repositories/chat_repository.dart
- [x] T018 [US2] Create Inbox BLoC managing conversations lists and active chat states at mobile_app/lib/features/inbox/bloc/inbox_bloc.dart
- [x] T019 [US2] Design Chat Inbox Screen list view with real-time indicators at mobile_app/lib/features/inbox/presentation/inbox_list_screen.dart
- [x] T020 [US2] Design Active Chat Thread screen with message bubble bubbles, attachment capabilities, and AI reply approvals at mobile_app/lib/features/inbox/presentation/chat_thread_screen.dart
- [x] T021 [US2] Design Conversation detail side-drawer sheet managing customer tags and scheduling follow-ups at mobile_app/lib/features/inbox/presentation/conversation_detail_sheet.dart

**Checkpoint**: At this point, User Stories 1 and 2 are fully functional and work together.

---

## Phase 5: User Story 3 - Mobile CRM Customer & Deal Pipeline Management (Priority: P2)

**Goal**: Port customer management list, details editors, and sales Kanban deal pipelines to the mobile application.

**Independent Test**: Load the CRM page, search for a customer, click to update their tags and blacklist status, open the pipeline view, and move a deal card from one stage to another.

### Implementation for User Story 3

- [x] T022 [P] [US3] Implement Customer, PipelineStage, and Deal models at mobile_app/lib/features/crm/data/models/crm_models.dart
- [x] T023 [US3] Create CRM Repository fetching customer listings and deal state pipelines at mobile_app/lib/features/crm/data/repositories/crm_repository.dart
- [x] T024 [US3] Create CRM BLoC managing customer queries and pipeline transitions at mobile_app/lib/features/crm/bloc/crm_bloc.dart
- [x] T025 [US3] Design Customer Directory Screen with text search, labels, and table pagination at mobile_app/lib/features/crm/presentation/customer_list_screen.dart
- [x] T026 [US3] Design Customer Profile Details edit screen at mobile_app/lib/features/crm/presentation/customer_detail_screen.dart
- [x] T027 [US3] Design Deal Pipeline Board visual grid screen at mobile_app/lib/features/crm/presentation/pipeline_board_screen.dart

**Checkpoint**: CRM customer and pipeline tracking is fully operational.

---

## Phase 6: User Story 4 - Visual Group Appointments Calendar & Booking (Priority: P2)

**Goal**: Build a group appointments calendar for scheduling and slot booking.

**Independent Test**: Navigate to the calendar, view appointments on a specific day, tap "Book Appointment", submit the form, and verify the slot updates on the calendar.

### Implementation for User Story 4

- [x] T028 [P] [US4] Implement Appointment data models at mobile_app/lib/features/bookings/data/models/appointment_model.dart
- [x] T029 [US4] Create Bookings Repository for appointment CRUD operations at mobile_app/lib/features/bookings/data/repositories/bookings_repository.dart
- [x] T030 [US4] Create Bookings BLoC state manager at mobile_app/lib/features/bookings/bloc/bookings_bloc.dart
- [x] T031 [US4] Design Booking Calendar visual screen at mobile_app/lib/features/bookings/presentation/bookings_calendar_screen.dart
- [x] T032 [US4] Design Bookings Scheduler form dialog screen at mobile_app/lib/features/bookings/presentation/booking_form_dialog.dart

**Checkpoint**: Group booking and calendar scheduler are fully functional.

---

## Phase 7: User Story 5 - Executives Dashboard Analytics & Configuration (Priority: P3)

**Goal**: View executive analytics charts and edit RAG configuration and WhatsApp gateway options.

**Independent Test**: Load the dashboard screen, verify fl_chart visualization loads correctly, go to settings, toggle AI auto-replies, and save settings.

### Implementation for User Story 5

- [x] T033 [P] [US5] Create Dashboard and Settings repository at mobile_app/lib/features/dashboard/data/repositories/dashboard_repository.dart
- [x] T034 [US5] Create Dashboard BLoC managing analytic snapshots and system configs at mobile_app/lib/features/dashboard/bloc/dashboard_bloc.dart
- [x] T035 [US5] Design Analytics Dashboard Screen with performance charts at mobile_app/lib/features/dashboard/presentation/dashboard_screen.dart
- [x] T036 [US5] Design Settings Configuration screen managing WhatsApp gateways and RAG variables at mobile_app/lib/features/settings/presentation/settings_screen.dart

**Checkpoint**: Analytics overview and configurations update successfully.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: General audits, formatting updates, test code coverage, and warning-free compilations.

- [x] T037 Write Dart BLoC unit and widget tests under mobile_app/test/ features for Auth, Inbox, and CRM.
- [x] T038 Audit overall styling, animations, colors, and margins against impeccable guidelines.
- [x] T039 Clean up code formatting, delete unused imports, run `flutter build` check, and eliminate all compiler warnings.

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
- **User Story 2 (P1)**: Can start after Foundational (Phase 2) - Integrates with US1
- **User Story 3 (P2)**: Can start after Foundational (Phase 2) - Integrates with US1/US2
- **User Story 4 (P2)**: Can start after Foundational (Phase 2) - Integrates with US1/US3
- **User Story 5 (P3)**: Can start after Foundational (Phase 2) - Integrates with US1

---

## Parallel Opportunities

- All Setup tasks T001-T003 can be run in parallel.
- Foundational tasks T004-T006 can run in parallel.
- Data models across all features (US1-US5) can be developed concurrently.
- BLoC unit tests can be written concurrently with BLoC controllers.
- Screens design can be done in parallel once repositories and BLoCs are established.

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories)
3. Complete Phase 3: User Story 1
4. **STOP and VALIDATE**: Test User Story 1 independently (Login, Register, Project Select)
5. Proceed to next phase

### Incremental Delivery

1. Complete Setup + Foundational
2. Add User Story 1 (Auth & Projects) -> MVP Ready!
3. Add User Story 2 (Real-time Inbox) -> Chat Engine Ready!
4. Add User Story 3 (CRM & Deals Board) -> Sales Engine Ready!
5. Add User Story 4 (Bookings Calendar) -> Service Scheduling Ready!
6. Add User Story 5 (Dashboard & Settings) -> Admin Suite Ready!
7. Complete Polish Phase -> Hardened Release Ready!

---

## Phase 9: Hardening & Parity Enhancements

- [x] T040 Implement auto-login post-frame check on `LoginScreen` in `mobile_app/lib/features/auth/presentation/login_screen.dart`
- [x] T041 Change theme mode to Light Theme by default in `mobile_app/lib/core/theme/colors.dart` and `mobile_app/lib/main.dart`
- [x] T042 Sort bookings/events list chronologically in `mobile_app/lib/features/bookings/presentation/bookings_calendar_screen.dart`
- [x] T043 Redesign booking screen to display list of Current Groups instead of a calendar view, including capacity occupancy ratio progress indicators and action buttons in `mobile_app/lib/features/bookings/presentation/bookings_calendar_screen.dart`
- [x] T044 Add Days selection and Date/Time picker in `mobile_app/lib/features/bookings/presentation/booking_form_dialog.dart`
- [x] T045 Integrate `CrmRepository` in `DashboardBloc` and calculate real-time CRM stats (total customers, active deals, closed won revenue, average lead score) in `mobile_app/lib/features/dashboard/bloc/dashboard_bloc.dart` and `mobile_app/lib/features/dashboard/presentation/dashboard_screen.dart`
- [x] T046 Enhance settings screen to support all 10 project settings fields matching the web client in `mobile_app/lib/features/settings/presentation/settings_screen.dart`
- [x] T047 Fix settings toggle cache staleness by updating status check in `mobile_app/lib/features/auth/bloc/auth_bloc.dart` to query latest project details from the network.

