# Feature Specification: Group Appointments & Follow-up Extensions

**Feature Branch**: `023-group-appointments-followup-extensions`

**Created**: 2026-06-11

**Status**: Draft

**Input**: User description: "عايزين نشيل السين. عايزي ف مجموعات العيال ماينفعش عيل يحجز ف مجموعين اخرو مجموعه بس. وعايز احدد الطالب حضر و محضرش ودفع و مدفعش. لو حضر يبقي خلاص تعرف انو حضر ف الفولو اب يتغير عل طول اوتوماتك الرساله بتاعتو. ولو دفع يقف ال ai ليه علي طول خلاص. عايز زرار اكسبورت للشيل اللي حجر ف المجموعه. عايز الفولو اب يبقي كيرتف دي حاجه و عايز سلزجي صايع"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Single Group Booking Limitation (Priority: P1)
As a project admin, I want to prevent students from booking into multiple groups simultaneously so that seats are distributed fairly and double-bookings are eliminated.

**Why this priority**: Preventing double group bookings is a core business constraint for class size management.

**Independent Test**: Try to register a phone number in Group A, then try to register the same phone number in Group B. The second registration must fail with a validation error.

**Acceptance Scenarios**:
1. **Given** a customer with no existing group bookings, **When** they request a booking in an active group, **Then** the booking is created successfully.
2. **Given** a customer who is already booked in Group A, **When** they attempt to book Group B, **Then** the system returns a bad request error with a message indicating they are already registered in another group.

---

### User Story 2 - Student Attendance and Payment Status (Priority: P1)
As a project admin, I want to mark students in a group as attended/not attended (حضور / عدم حضور) and paid/not paid (دفع / عدم دفع) from the management dashboard so that I can track their progress and status.

**Why this priority**: Necessary to trigger the conditional AI auto-reply and follow-up behaviors.

**Independent Test**: Verify that the bookings list in the group appointments manager displays attendance and payment toggles, and changing them persists to the database and updates the UI.

**Acceptance Scenarios**:
1. **Given** a group booking, **When** the admin toggles the "Attended" switch, **Then** the booking's attendance state is updated and saved.
2. **Given** a group booking, **When** the admin toggles the "Paid" switch, **Then** the booking's payment state is updated and saved.

---

### User Story 3 - Automated AI Reply Suppression for Paid Students (Priority: P1)
As a project owner, I want the AI auto-reply to stop immediately for any student who has paid so that they do not receive automated promotional/nurturing replies after completing their purchase.

**Why this priority**: Crucial for student experience; once a student has paid, the conversion is successful, and generic AI auto-replies must stop to avoid confusion or inappropriate messages.

**Independent Test**: Mark a student booking as "Paid" and send an incoming message from that student's WhatsApp number. The AI Reply Worker should skip generating an auto-reply.

**Acceptance Scenarios**:
1. **Given** a customer with an active group booking marked as Paid, **When** they send a message to the WhatsApp channel, **Then** the AI reply logic is skipped and no automated response is sent.
2. **Given** a customer with an active group booking marked as unpaid, **When** they send a message, **Then** the AI auto-reply is generated normally.

---

### User Story 4 - Attendance-Based Follow-up Adaptation (Priority: P2)
As a project admin, I want the automated follow-up messages for students who attended their session to be automatically rewritten by Gemini to acknowledge their attendance.

**Why this priority**: Personalized follow-up improves conversion rates and makes the system feel more professional.

**Independent Test**: Mark a student's booking as Attended and trigger their scheduled follow-up. The sent message must mention or adapt to the fact that they attended.

**Acceptance Scenarios**:
1. **Given** a customer marked as Attended, **When** a follow-up is executed, **Then** the AI includes instructions to Gemini to rewrite the message context reflecting their attendance.

---

### User Story 5 - Follow-up Style Tones: Creative and Salesy (Priority: P2)
As a project admin, I want to choose between "Creative" (إبداعي) or "Salesy/Cunning" (سلزجي صايع) tones when configuring follow-ups so that the AI rewrites follow-up notes with the corresponding personality.

**Why this priority**: Gives marketing agents control over the tone of automation based on the lead's temperature.

**Independent Test**: Create a follow-up with the "Salesy" style and verify that the generated WhatsApp message has an engaging, sales-focused Egyptian slang style.

**Acceptance Scenarios**:
1. **Given** a follow-up with tone set to "Salesy", **When** it is processed by the AI marketing brain, **Then** it applies salesy guidelines ("سلزجي صايع وذكي ومقنع") to the rewrite prompt.
2. **Given** a follow-up with tone set to "Creative", **When** it is processed, **Then** it applies creative writing guidelines ("إبداعي ومبتكر وجذاب") to the rewrite prompt.

---

### User Story 6 - Export Group Bookings to CSV (Priority: P2)
As a project admin, I want to export the list of registered students for any group as a CSV sheet so that I can view it offline or import it into external tools.

**Why this priority**: Useful for class instructors to print sheets or view them on spreadsheet software.

**Independent Test**: Click the export button on a group's bookings view and check if the downloaded CSV file contains the correct columns and student records in Arabic.

**Acceptance Scenarios**:
1. **Given** a group with 5 bookings, **When** the admin clicks the export button, **Then** a CSV file is downloaded containing 5 records with their name, phone, booking date, attendance, and payment statuses.

---

### User Story 7 - Disable Auto Read Receipt (Seen Status) (Priority: P3)
As a project owner, I want to disable the automatic read receipts for incoming messages so that they do not instantly appear as "Read/Seen" (blue ticks) on the customer's WhatsApp interface.

**Why this priority**: Gives agents more time to reply without the customer seeing that the message was read instantly.

**Independent Test**: Send a message from a customer phone to the gateway, and check if the message remains unread (no blue ticks) on the customer's phone until an agent replies.

**Acceptance Scenarios**:
1. **Given** an incoming WhatsApp message, **When** it is received by the gateway, **Then** the gateway processes the webhook but does NOT invoke the `readMessages` function to mark it as read.

---

### User Story 8 - Accurate Group Mode & Existence Communication (Priority: P1)
As a project admin, I want the AI auto-reply to communicate correct group appointment details, ensuring it knows all active groups (even if full) to prevent claiming they don't exist, and strictly enforcing that online students attend online and offline students attend in the center.

**Why this priority**: Preventing confusion for students is critical. Online students should not attend physically in the center, and the system should not claim groups do not exist when they are simply full.

**Independent Test**:
1. Check that the AI responds to questions about a full group by saying it is full (مكتملة) rather than saying it doesn't exist.
2. Ask the AI if online students can attend in the center, and verify it replies that online registration is strictly online-only.

**Acceptance Scenarios**:
1. **Given** a group is active but full, **When** the customer asks about that group, **Then** the AI recognizes its existence but informs them it is full/completed.
2. **Given** a customer is registered in an online group, **When** they ask about attending in the center, **Then** the AI strictly advises that their attendance is online-only.

---

### Edge Cases

- **Blacklisted Customer booking**: Blacklisted customers are already prevented from normal flow, but if a booking is added, their status should still behave under single booking rules.
- **Toggling Attendance/Payment back and forth**: Toggling a paid status back to unpaid must re-enable the AI auto-reply for subsequent messages.
- **Arabic characters in CSV**: When exporting Arabic student names to CSV, it must contain a UTF-8 BOM so Excel opens it without character corruption.
- **No active bookings**: If a follow-up is run for a customer who does not have any group bookings, it defaults to normal nurturing style without attendance adjustments.
- **Full groups in AI suggestions**: If a group is full, the AI must not recommend it or set `suggestedGroupBookingId` to its ID.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST prevent a customer (by phone number) from having more than one group booking inside the same project.
- **FR-002**: Database MUST store `IsAttended` (boolean, default false) and `IsPaid` (boolean, default false) for each `GroupAppointmentBooking`.
- **FR-003**: The admin dashboard MUST provide toggles to update the `IsAttended` and `IsPaid` statuses of bookings.
- **FR-004**: The AI reply worker MUST check if the customer has any booking marked as `IsPaid == true` and, if so, skip generating an auto-reply.
- **FR-005**: The follow-up scheduler MUST pass the customer's attendance status (`IsAttended`) and the follow-up's selected style to the AI rewriting service.
- **FR-006**: The AI marketing brain MUST adjust the rewrite prompt for follow-ups based on the tone style selected ("Creative" or "Salesy") and whether the customer has attended.
- **FR-007**: The follow-up entity MUST support a `Tone` field (string/enum: Default, Creative, Salesy).
- **FR-008**: The gateway MUST NOT automatically mark incoming WhatsApp messages as read/seen.
- **FR-009**: The Group Appointments page MUST include an export button that downloads a CSV file containing all bookings for that group.
- **FR-010**: The AI reply worker MUST load all active group appointments (both available and full) into the AI context, marking full groups as full (مكتملة العدد) instead of omitting them from the context.
- **FR-011**: The AI reply worker MUST include strict instructions in the system prompt stating that Online groups are online-only and Offline groups are center-only, and that online students cannot attend physically in the center.

### Key Entities *(include if feature involves data)*

- **GroupAppointmentBooking**:
  - `IsAttended`: Boolean (default `false`) - Tracks whether the student attended the group session.
  - `IsPaid`: Boolean (default `false`) - Tracks whether the student paid for the group session.
- **FollowUp**:
  - `Tone`: String (default `"Default"`) - Can be `"Default"`, `"Creative"`, or `"Salesy"`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of double booking attempts are blocked with a clear user message.
- **SC-002**: Marked "Paid" status disables AI auto-reply within 0 seconds (instantly on next message).
- **SC-003**: Exported CSV sheets load correctly in Excel with Arabic characters.
- **SC-004**: Incoming messages do not trigger seen receipts (blue ticks) on the sender's phone.
- **SC-005**: 100% of queries about full groups receive confirmation that the group exists but is currently full, and the AI correctly instructs online students to attend online only.

## Assumptions

- We assume that "نشيل السين" means turning off auto-seen completely for all projects.
- We assume that the CSV export is done entirely client-side using JavaScript Blob since the bookings are already loaded in the UI.
- We assume that the Flutter mobile app will use the updated API models, but the primary target for management operations is the Web dashboard.
- We assume that the AI prompt should explicitly emphasize the difference between online and offline modes to avoid any student attending offline in a center if they are online.
