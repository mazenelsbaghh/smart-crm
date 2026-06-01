# Feature Specification: Dynamic Follow-up & Appointment Reminders

**Feature Branch**: `015-dynamic-followups`

**Created**: 2026-06-01

**Status**: Draft

**Input**: User description: "او لو حجز مثلا معاد الخميس الساعه ٨ هيحضر كورس ده لو كورس يبقي قبلها بيوم. النوع الثاني: متابعة العملاء المترددين أو غير المهتمين (Nurturing / Re-engagement) اكيد اه عايز دول"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Nurturing / Re-engagement Follow-up (Priority: P1)
As an agent, I want to schedule a nurturing follow-up message for a hesitant or uninterested customer to be sent at a specific date and time, so that we can re-engage them automatically without manual tracking.

**Why this priority**: Core nurturing requirement to revive cold or hesitant leads.

**Independent Test**: Schedule a nurturing follow-up for 1 minute from now, wait, and verify the message is automatically sent to the customer via WhatsApp and marked as completed.

**Acceptance Scenarios**:
1. **Given** a customer is marked as "hesitant", **When** the agent schedules a nurturing follow-up for tomorrow at 10:00 AM, **Then** a follow-up record of type `Nurturing` is saved with `DueDate` set to tomorrow at 10:00 AM.
2. **Given** a nurturing follow-up has `DueDate` in the past, **When** the background scheduler runs, **Then** the follow-up message is sent via the WhatsApp gateway and its status is updated to `Completed`.

---

### User Story 2 - Course / Appointment Booking Reminder (Priority: P1)
As an agent or system event, when a customer has a booked appointment or course, I want to schedule a reminder message to be sent exactly one day (24 hours) before the course starts (or a custom time before a general appointment), so that they are reminded to attend.

**Why this priority**: Minimizes show-up failure rates for booked courses and appointments.

**Independent Test**: Schedule a course reminder follow-up for a course happening 25 hours from now, wait 1 hour, and verify the reminder message is sent (since the send time is 1 day before the course).

**Acceptance Scenarios**:
1. **Given** a customer booked a course for Thursday at 8:00 PM, **When** the agent creates a course reminder follow-up, **Then** the follow-up record of type `AppointmentReminder` is saved with `AppointmentTime` set to Thursday at 8:00 PM and the calculated `DueDate` set to Wednesday at 8:00 PM (24 hours before).
2. **Given** a course reminder follow-up with `DueDate` (Wednesday at 8:00 PM) in the past, **When** the background scheduler runs, **Then** the reminder message is sent to the customer and the follow-up is marked as `Completed`.

---

### User Story 3 - Interactive Follow-up Scheduler UI (Priority: P2)
As an agent, I want a clean, localized Arabic interface to select the follow-up type and schedule it directly from the customer details panel or the follow-ups page.

**Why this priority**: Essential for agents to easily schedule correct follow-ups without manual date calculations.

**Independent Test**: Open the customer detail drawer, click "Schedule Follow-up", verify the toggle between "متابعة" (Nurturing) and "تذكير بموعد/كورس" (Reminder), select "تذكير بموعد/كورس", input the appointment date, and verify that it lists the calculated send date (1 day before) before submitting.

**Acceptance Scenarios**:
1. **Given** the schedule form is open, **When** "متابعة" (Nurturing) is selected, **Then** only the "تاريخ المتابعة" (Due Date) field is shown.
2. **Given** the schedule form is open, **When** "تذكير بموعد/كورس" (Reminder) is selected, **Then** the user inputs "تاريخ الموعد" (Appointment Date) and the UI shows a helper text indicating that the reminder will be sent 24 hours prior.

---

### Edge Cases

- **What happens if an appointment is booked for less than 24 hours from now?**
  - If the appointment time is less than 24 hours away (e.g. 10 hours from now), the calculated `DueDate` would be in the past. The system should default to sending the reminder immediately (clamping `DueDate` to current time) or alert the user.
- **What happens if the agent manually updates or cancels the appointment?**
  - If the appointment/follow-up is deleted or status changed, the background job will ignore it.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support two follow-up types: `Nurturing` and `AppointmentReminder`.
- **FR-002**: The `FollowUp` entity MUST store the `Type` (enum/string), `AppointmentTime` (DateTime, nullable), and `DueDate` (DateTime).
- **FR-003**: The backend MUST calculate `DueDate` for `AppointmentReminder` as `AppointmentTime - 24 hours` (or a configurable offset), and set `DueDate` directly to the follow-up time for `Nurturing`.
- **FR-004**: If the calculated `DueDate` for a new reminder is in the past (because the appointment is less than 24 hours away), the system MUST clamp `DueDate` to `DateTime.UtcNow` to trigger it immediately.
- **FR-005**: The `FollowUpScheduler` background job MUST send the follow-up's custom text (or default reminder text if notes are empty) to the customer when `DueDate` is reached.
- **FR-006**: The frontend customer detail drawer and follow-up management UI MUST be updated to support selecting follow-up types and inputting appointment times in Arabic.
- **FR-007**: The frontend follow-ups table MUST display the follow-up type and the target appointment time clearly.

### Key Entities

- **FollowUp**:
  - `Type`: String/Enum (`Nurturing` or `AppointmentReminder`).
  - `AppointmentTime`: DateTime (nullable). The actual time of the course/appointment.
  - `DueDate`: DateTime. The exact time the message should be sent to WhatsApp.
  - `Notes`/`Message`: String. The customized message to send.
  - `Status`: String (`Pending`, `Completed`, `Missed`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of course reminder follow-ups have their `DueDate` correctly calculated as exactly 24 hours before the `AppointmentTime`.
- **SC-002**: The background scheduler executes overdue follow-ups within 10 seconds of their `DueDate` passing (based on Hangfire interval).
- **SC-003**: The frontend displays type badges ("تذكير موعد" / "متابعة عميل") in real-time in the follow-ups list.

## Assumptions

- We assume a default offset of 24 hours for appointment reminders, as specified for the "course" scenario.
- All date-times are processed and stored in UTC database-side and formatted to local timezone client-side.
