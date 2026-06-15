# Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification
- [x] Phase 2: Technical Planning
- [x] Phase 3: Detailed Task Breakdown

---

# Tasks: Group Appointments & Follow-up Extensions

**Input**: Design documents from `specs/023-group-appointments-followup-extensions/`

**Prerequisites**: plan.md (required), spec.md (required)

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Database and initial migrations setup

- [x] T001 Add EF Core migration for attendance and tone fields: run `dotnet ef migrations add AddGroupBookingAttendanceAndFollowUpTone --project backend`
- [x] T002 Apply PostgreSQL database migrations: run `make db-migrate`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Disable automatic seen receipt on WhatsApp gateway

- [x] T003 In gateway file `whatsapp-gateway/src/baileys-manager.js`, comment out or delete the `await sock.readMessages([msg.key]);` seen receipt logic inside the upsert handler block.

---

## Phase 3: User Story 1 - Single Group Booking Limitation (Priority: P1) 🎯 MVP

**Goal**: Prevent a customer from booking more than one group under the same project.

**Independent Test**: Request two public bookings for the same phone number under the same project. The second request must return a validation error.

### Implementation for User Story 1

- [x] T004 [US1] Modify `BookGroupSlot` in `backend/src/Modules/GroupAppointments/API/GroupAppointmentsController.cs` to check if a booking with the same phone number already exists under the project. Use:
  ```csharp
  var hasAnyGroupBooking = await _context.GroupAppointmentBookings
      .AnyAsync(b => b.ProjectId == request.ProjectId && b.CustomerPhone == cleanPhone);
  if (hasAnyGroupBooking) {
      return BadRequest(new { error = "عذراً، لا يمكن التسجيل في أكثر من مجموعة واحدة" });
  }
  ```

---

## Phase 4: User Story 2 - Student Attendance and Payment Status (Priority: P1)

**Goal**: Allow marking students as attended/not attended and paid/not paid in group details.

**Independent Test**: Navigate to the group details list in the dashboard and verify that attendance and payment checkboxes are visible and toggle correctly.

### Implementation for User Story 2

- [x] T005 [US2] Update domain model `backend/src/Modules/GroupAppointments/Domain/GroupAppointmentBooking.cs` to include properties `public bool IsAttended { get; set; } = false;` and `public bool IsPaid { get; set; } = false;`.
- [x] T006 [US2] Update `GetGroups` query projection in `backend/src/Modules/GroupAppointments/API/GroupAppointmentsController.cs` to map and return `b.IsAttended` and `b.IsPaid` fields in the returned booking objects.
- [x] T007 [US2] Add a PATCH API route `PATCH /api/group-appointments/bookings/{bookingId}` in `backend/src/Modules/GroupAppointments/API/GroupAppointmentsController.cs` to accept a body `{ bool? isAttended, bool? isPaid }`, update the booking record, save changes, and trigger the SignalR `GroupBookingUpdated` broadcast to notify other clients.
- [x] T008 [US2] Update UI package `frontend/src/packages/settings/GroupAppointmentsManager.tsx` to include `isAttended: boolean` and `isPaid: boolean` in the `Booking` interface, and add checkboxes/toggles in the bookings table with API callbacks to update the status.

---

## Phase 5: User Story 3 - AI Reply Suppression for Paid Students (Priority: P1)

**Goal**: Stop AI auto-reply immediately when a student is marked as paid.

**Independent Test**: Mark a student booking as paid. Send an incoming message from them. Verify that backend logs show AI reply was skipped for this paid customer.

### Implementation for User Story 3

- [x] T009 [US3] Modify `AIReplyWorker.cs` in `backend/src/Modules/AI/Workers/AIReplyWorker.cs` around line 92 to check if the customer has any group booking with `IsPaid == true`. If so, log the bypass and return early.

---

## Phase 6: User Story 4 - Attendance-Based Follow-up Adaptation (Priority: P2)

**Goal**: Adapt follow-up message content automatically using Gemini if they attended.

**Independent Test**: Mark a student as attended. Verify that their follow-up message rewrite request is passed to Gemini with instructions referencing their attendance.

### Implementation for User Story 6

- [x] T010 [US4] Update `RewriteFollowUpNotesAsync` signature and implementation in `backend/src/Modules/AI/Services/IAIMarketingBrain.cs` and `backend/src/Modules/AI/Services/AIMarketingBrain.cs` to accept `bool hasAttended`. If `hasAttended` is true, append Arabic instructions telling Gemini to write a welcoming message acknowledging their attendance.
- [x] T011 [US4] Update the background runner in `backend/src/Modules/CRM/Services/FollowUpScheduler.cs` and manual send endpoint `SendFollowUp` in `backend/src/Modules/CRM/API/CRMController.cs` to query if the customer has any booking where `IsAttended == true` and pass that boolean as `hasAttended` to `RewriteFollowUpNotesAsync`.

---

## Phase 7: User Story 5 - Follow-up Style Tones: Creative and Salesy (Priority: P2)

**Goal**: Add support for selecting a Creative or Salesy tone for follow-ups and pass it to Gemini.

**Independent Test**: Create a follow-up with "Salesy" style. Verify that the generated message has a sales-focused Egyptian slang style.

### Implementation for User Story 7

- [x] T012 [US5] Update domain model `backend/src/Modules/CRM/Domain/FollowUp.cs` to add `public string Tone { get; set; } = "Default";`.
- [x] T013 [US5] Update request DTOs and endpoints `CreateFollowUp`, `UpdateFollowUp` in `backend/src/Modules/CRM/API/CRMController.cs` to support receiving and saving the `Tone` parameter.
- [x] T014 [US5] Update the follow-up forms and models in `frontend/src/packages/management/FollowUps.tsx` or settings views to include a dropdown field for `Tone` selection with options (Default, Creative, Salesy).
- [x] T015 [US5] Update `RewriteFollowUpNotesAsync` in `backend/src/Modules/AI/Services/AIMarketingBrain.cs` to receive the `tone` parameter. If `tone` is `"Creative"`, append creative guidelines; if `tone` is `"Salesy"`, append salesy/cunning Egyptian slang guidelines.

---

## Phase 8: User Story 6 - Export Group Bookings to CSV (Priority: P2)

**Goal**: Add a CSV export button in the group details view.

**Independent Test**: Open a group's bookings in the UI, click export, and verify that a CSV file is downloaded containing student records with UTF-8 BOM.

### Implementation for User Story 8

- [x] T016 [US6] Modify `frontend/src/packages/settings/GroupAppointmentsManager.tsx` to add a "تصدير CSV" button next to "إغلاق القائمة", and implement a `handleExportCSV` function that generates and downloads a CSV file containing bookings data.

---

## Phase 8.5: User Story 8 - Accurate Group Mode & Existence Communication (Priority: P1)

**Goal**: Load both available and full groups into the AI reply worker's context, and provide strict instructions on online vs offline attendance.

**Independent Test**: Send questions about full groups and check if the AI confirms their existence but marks them full. Verify that online students are told they are online-only.

### Implementation for User Story 8

- [x] T019 [US8] Update `AIReplyWorker.cs` to fetch all active groups (do not filter out full groups immediately). Separate active groups into `availableGroups` (not full) and `fullGroups` (full).
- [x] T020 [US8] Update `AIReplyWorker.cs` to format and inject `availableGroups` under "قائمة المجموعات المتاحة حالياً" and `fullGroups` under "قائمة المجموعات المكتملة العدد حالياً". Add strict instructions in the system prompt asserting that Online is strictly online-only, Offline is center-only, online students cannot attend in the center, and full groups cannot be suggested/recommended for booking.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Verification and final build checks.

- [x] T017 Run full build check for both `backend` and `frontend` projects to ensure zero warnings or errors.
- [x] T018 Run the python test harness `make test-all` to verify no regressions were introduced.
- [x] T021 Run `clean-code-guard` in guard-pass mode against all modified production files to ensure clean code standards.
- [x] T022 Run `test-guard` against all modified test files to verify test suite quality.
- [x] T023 Perform feature tests covering the full feature test matrix and record results.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies.
- **Foundational (Phase 2)**: Depends on Setup.
- **User Stories (Phases 3+)**: Depend on Foundational.
- **Polish (Final Phase)**: Depends on all user stories being complete.
