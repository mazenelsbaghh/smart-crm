# Feature Specification: WhatsApp Group Automation & Post-Session Workflows

**Feature Branch**: `032-whatsapp-group-automation`

**Created**: 2026-06-29

**Status**: Draft

**Input**: User description:
- Enable/disable WhatsApp group automation addon.
- The night before a session (Sunday 11:00 PM for a Monday session), automatically create a WhatsApp group named `wave [WaveName]`, add the manager (+20 106 869 0092) to it, lock settings to admin-only, disable direct member additions, and generate an invite link.
- Send the group invite link to booked students in their session reminder message (for online: "This is the group link where the session link will be sent"; for offline: send the location address and link with "We are waiting for you").
- Ensure AI ignores group messages.
- 2 days after the session, send an automated follow-up. If they didn't attend, offer other dates. If they attended, ask if they subscribed; if they say yes, automatically blacklist/block them.
- If a customer requests a human, send a WhatsApp notification to the manager (+20 106 869 0092) with the customer's name and phone number.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toggle Group Automation Addon (Priority: P1)
As a project manager, I want to enable or disable the WhatsApp Group Automation addon from the settings page, so that I can control whether automated groups are created and reminders are sent.

**Why this priority**: Core control switch. If disabled, no background group creation or automated reminders should trigger.

**Independent Test**: Verify that the Addons tab displays a toggle for WhatsApp Group Automation. Toggling it should save the preference to the project settings.

**Acceptance Scenarios**:
1. **Given** the project manager is on the Addons settings page, **When** they toggle "WhatsApp Group Automation" on and save, **Then** the feature is enabled in the project.
2. **Given** the feature is disabled, **When** a session is scheduled, **Then** no background task creates groups or schedules invite follow-ups.

---

### User Story 2 - Automated Group Creation & Locked Settings (Priority: P1)
As a project manager, I want the system to automatically create a locked WhatsApp group the night before a scheduled session (at 11:00 PM Cairo time), adding my phone number as an admin, and generating an invite link.

**Why this priority**: Essential to run the automated WhatsApp group lifecycle.

**Independent Test**: Create a group appointment scheduled for Monday, and verify that at 11:00 PM on Sunday, a WhatsApp group is created with the subject `wave [Name]`, the manager is added, settings are locked, and an invite link is generated.

**Acceptance Scenarios**:
1. **Given** a session named `wave 5` is scheduled for Monday at 3:00 PM, **When** Sunday 11:00 PM Cairo time is reached, **Then** the system creates a group named `wave 5`, adds `+20 106 869 0092`, sets group info to admin-only, and saves the group JID and invite link.

---

### User Story 3 - Session Reminders with Invite Links (Priority: P1)
As a booked student, I want to receive a reminder message the day before the session containing the WhatsApp group invite link, so that I can join the group.

**Why this priority**: Crucial communication channel for students.

**Independent Test**: Book a student for a session. When the reminder is sent, verify that:
- For online: it contains the group invite link and states "This is the group link where the session link will be sent".
- For offline: it contains the group invite link, the location link/address, and "We are waiting for you".

**Acceptance Scenarios**:
1. **Given** an online session, **When** the reminder follow-up is sent to a booked student, **Then** the message says: "هذا هو رابط الجروب الذي سيرسل عليه رابط الحصة: [Invite Link]"
2. **Given** an offline session, **When** the reminder follow-up is sent to a booked student, **Then** the message says: "هذا هو رابط الجروب: [Invite Link]. عنوان وموقع الحضور: [Location]. نحن بانتظاركم!"

---

### User Story 4 - Post-Session Follow-up & Blacklist on Subscription (Priority: P2)
As a project manager, I want the system to automatically send a follow-up 2 days after the session to ask students if they attended and if they subscribed, automatically blacklisting them if they subscribed.

**Why this priority**: Automates the conversion and clean-up funnel.

**Independent Test**: Verify that 2 days after the session, students receive a follow-up. Test that:
- If they answer they did not attend, the AI offers other session dates.
- If they answer they attended and subscribed, they are automatically blacklisted (IsBlacklisted = true).

**Acceptance Scenarios**:
1. **Given** a student booked a session, **When** 2 days pass after the session date, **Then** the system sends a follow-up message asking: "حابين نعرف طمننا حضرت السيشن ولا لا؟"
2. **Given** the student replies "نعم حضرت واشتركت", **When** the AI receives this, **Then** the system automatically sets the student's status to `IsBlacklisted = true` (blocks auto-replies).

---

### User Story 5 - Human Request Hand-off (Priority: P1)
As a customer, when I ask to talk to a human, I want the system to notify the manager immediately via WhatsApp with my details, so they can contact me.

**Why this priority**: Essential customer support fallback.

**Independent Test**: Send a message like "عايز أكلم بني آدم" as a customer. Verify that a WhatsApp message is sent to `+20 106 869 0092` saying "العميل [Name] ([Phone]) طلب التحدث مع شخص طبيعي."

**Acceptance Scenarios**:
1. **Given** the AI is conversing with a customer, **When** the customer says "أريد التواصل مع خدمة العملاء", **Then** the AI sends a notification message to `+20 106 869 0092` with the customer's name and phone number.

---

## Edge Cases

- **Group creation fails**: If the WhatsApp session is disconnected when group creation is attempted, the system must retry and alert the manager.
- **Student is already blacklisted**: If a student is blacklisted before the reminder is sent, the system must not send any reminders or follow-ups to them.
- **Multiple bookings**: If a student has bookings in multiple waves, follow-ups must be wave-specific.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST provide a toggle in the settings dashboard under "Addons" to enable or disable "WhatsApp Group Automation".
- **FR-002**: System MUST run a background job at 11:00 PM Cairo time every night to find group appointments scheduled for the following calendar day (Cairo time).
- **FR-003**: System MUST create a WhatsApp group via the WhatsApp gateway named `wave [WaveName]`, add the manager's number `+20 106 869 0092`, lock the group settings to admin-only, restrict participant addition, and fetch the invite link.
- **FR-004**: System MUST store the group invite link and JID in the database associated with the group appointment.
- **FR-005**: System MUST schedule and send the `AppointmentReminder` follow-up to booked students immediately after group creation at 11:00 PM Cairo time the night before the session.
- **FR-006**: For offline sessions, the system MUST allow managers to specify a `Location` (address/link) when creating or updating group appointments.
- **FR-007**: System MUST ignore all incoming messages from WhatsApp JIDs ending with `@g.us` at the gateway.
- **FR-008**: System MUST schedule a `PostSessionFollowUp` 2 days after the session date.
- **FR-009**: If the customer replies to the post-session follow-up that they subscribed, the AI MUST trigger an automatic block (`IsBlacklisted = true`).
- **FR-010**: If a customer requests a human, the AI MUST call a notification action that sends a direct WhatsApp message to `+20 106 869 0092`.

---

## Key Entities *(include if feature involves data)*

- **GroupAppointment**:
  - `IsGroupAutomationEnabled` (boolean, project level / addon level)
  - `GroupJid` (string, stores the created group ID)
  - `InviteLink` (string, stores the group invitation link)
  - `Location` (string, stores address details for offline sessions)
- **FollowUp**:
  - `Type`: Added `PostSessionFollowUp` type.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: WhatsApp groups are created within 60 seconds of Sunday 11:00 PM for all Monday sessions.
- **SC-002**: 100% of booked students for online/offline sessions receive the correct group invite link and location details in their reminder.
- **SC-003**: 100% of customers requesting a human result in a WhatsApp notification sent to the manager within 15 seconds.
- **SC-004**: 0% of messages sent inside WhatsApp groups trigger AI auto-replies.

---

## Assumptions

- The manager's phone number `+20 106 869 0092` is correct and always active on WhatsApp.
- The WhatsApp gateway is connected and authorized to create groups.
