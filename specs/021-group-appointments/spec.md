# Feature Specification: Group Appointments Add-on (إضافة مواعيد المجموعات)

**Feature Branch**: `021-group-appointments`

**Created**: 2026-06-05

**Status**: Draft

**Input**: User description: "عايز اضيف حاجه اسمها اضافات ويكون فيها سيشكن افعلوا اسمو مواعيد ممجوعات هو يعبي عليها بس بشرط انو احدد انا عايز كام واحد لكل مجموعه ولو اتملت يقولوا اتملت فاهمين" (I want to add something called Add-ons and inside it a section that I can activate called Group Appointments. The customer fills it out, but on the condition that I specify how many people are allowed per group, and if it gets full, it tells them it's full. Do you understand?)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toggle and Configure Group Appointments Add-on (Priority: P1)

As an administrator in the CRM dashboard, I want to navigate to an "Add-ons" (الاضافات) settings page, activate the "Group Appointments" (مواعيد المجموعات) add-on, and define appointment groups with custom limits (capacity), so that I can control scheduling options for my business.

**Why this priority**: It is the foundation of the feature. Admin must be able to turn the feature on/off and configure the groups and capacities.

**Independent Test**: Can be tested by navigating to the Add-ons page, enabling Group Appointments, creating a group named "مجموعة السبت 4 عصراً" with a capacity of 3, and saving it.

**Acceptance Scenarios**:
1. **Given** the administrator is on the Add-ons page, **When** they toggle the "Group Appointments" switch to ON, **Then** the Group Appointments configuration panel is revealed.
2. **Given** the add-on is enabled, **When** the administrator adds a new group with a Name, Date/Time, and Max Capacity (e.g., 5), **Then** the group is saved in the database and visible in the admin list.
3. **Given** a group exists, **When** the administrator views the list of groups, **Then** they see the current enrollment count (e.g., "0 / 5") and can click to see the names/phones of booked customers.

---

### User Story 2 - Public Booking Page for Customers (Priority: P1)

As a customer, I want to access a public booking page, select from the list of available group appointment slots, enter my name and WhatsApp phone number, and register for a group, so that I can secure my slot.

**Why this priority**: Customers must be able to register themselves.

**Independent Test**: Load the public booking page for a specific project, select a group, fill in name and phone, submit, and verify that the registration is recorded.

**Acceptance Scenarios**:
1. **Given** the customer loads the public booking page for a project, **When** they view the available slots, **Then** they only see groups that are active, in the future, and not yet full.
2. **Given** the customer fills in their name and WhatsApp number, selects a group with remaining capacity, and clicks "سجل الآن" (Register Now), **Then** they receive a success confirmation and a booking is created in the database.
3. **Given** a customer has booked, **When** the admin checks the CRM, **Then** a new or existing customer record is synced/created, and a booking entry is associated with them.

---

### User Story 3 - Limit Enforcement and "Full" Status (Priority: P1)

As a customer and administrator, I want the system to prevent registrations when a group reaches its maximum capacity, displaying a clear "Group Full" message, to prevent overbooking.

**Why this priority**: Core constraint of the feature request: "ولو اتملت يقولوا اتملت" (and if it gets full, it tells them it's full).

**Independent Test**: Create a group with a capacity of 1. Book one slot. Attempt to book a second slot for the same group and verify that booking is blocked with a "Full" message.

**Acceptance Scenarios**:
1. **Given** a group has a capacity of 5 and already has 5 bookings, **When** a customer loads the public booking page, **Then** the group is displayed as "مكتملة" (Full) and the selection is disabled.
2. **Given** a customer submits a booking for a group that becomes full at the exact moment of submission, **When** the backend processes the request, **Then** it rejects the booking and returns an error message: "عذراً، هذه المجموعة ممتلئة بالكامل" (Sorry, this group is completely full).

---

### User Story 4 - Agent Notification on Booking (Priority: P2)

As a CRM agent, I want to receive a real-time notification/alert when a customer successfully registers for a group appointment, so that I can follow up immediately.

**Why this priority**: Enhances usability and allows agents to act on new bookings immediately.

**Independent Test**: Book a group appointment on the public page, and check if a notification banner or alert appears in the agent's CRM sidebar/notifications.

**Acceptance Scenarios**:
1. **Given** a customer successfully books a slot in a group, **When** the booking is created, **Then** a real-time SignalR notification is broadcasted to the agents, and a `NotificationAlert` is saved in the database.

---

### Edge Cases

- **Concurrent booking requests on the last remaining slot**:
  - The backend MUST use a database transaction or check the booking count atomically before saving the booking. If two customers click register at the same time, the first one succeeds, and the second one receives the "Group is full" error.
- **Customer registers multiple times**:
  - If a customer with the same phone number attempts to book the same group, the system should prevent duplicate booking and show a message: "أنت مسجل بالفعل في هذه المجموعة" (You are already registered in this group).
- **Admin deactivates the Group Appointments add-on**:
  - If disabled, the public booking page should display a friendly message: "خدمة الحجز غير مفعلة حالياً" (Booking service is currently inactive) and prevent any booking requests.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `ProjectSettings` model MUST include a boolean field `IsGroupAppointmentsEnabled` (defaulting to `false`) to control whether the add-on is active.
- **FR-002**: System MUST provide API endpoints to manage group appointment slots: `GET`, `POST`, `PUT`, `DELETE` under authorized `/api/group-appointments`.
- **FR-003**: System MUST provide a public endpoint `GET /api/public/group-appointments/{projectId}` to fetch active future group slots and their current booking/capacity status.
- **FR-004**: System MUST provide a public endpoint `POST /api/public/group-appointments/book` to book a slot. This endpoint MUST validate the project settings, duplicate registrations, and group capacity atomically.
- **FR-005**: The booking process MUST check if a customer with the provided phone number exists in the project. If not, it MUST create a new `Customer` record; if yes, it MUST associate the booking with the existing customer.
- **FR-006**: The admin dashboard MUST have an "Add-ons" (الاضافات) page/tab where users can toggle the feature, manage groups, and see list of bookings with customer details.
- **FR-007**: A `NotificationAlert` of type `Booking` MUST be generated and broadcasted via SignalR to agents when a booking is successful.

### Key Entities

- **GroupAppointment**:
  - `Id` (Guid, Primary Key)
  - `ProjectId` (Guid, Foreign Key)
  - `Name` (String, e.g., "مجموعة أ")
  - `DateTime` (DateTime, Slot start time)
  - `Capacity` (Integer, max capacity)
  - `IsActive` (Boolean, slot active state)
  - `CreatedAt`/`UpdatedAt` (DateTime)
- **GroupAppointmentBooking**:
  - `Id` (Guid, Primary Key)
  - `ProjectId` (Guid, Foreign Key)
  - `GroupAppointmentId` (Guid, Foreign Key)
  - `CustomerId` (Guid, Foreign Key)
  - `CustomerName` (String, name entered at booking)
  - `CustomerPhone` (String, phone entered at booking)
  - `CreatedAt` (DateTime)

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Under no circumstances can the number of bookings for a group exceed its defined `Capacity`.
- **SC-002**: Public booking page loads in under 500ms and displays real-time capacity states accurately.
- **SC-003**: Customer booking submissions are processed and saved in under 300ms, immediately updating the remaining capacity.
- **SC-004**: Real-time notifications reach active agents in the CRM within 1 second of booking completion.

## Assumptions

- Booking is done via a public responsive web page link that agents can share with customers.
- Customers do not need to log in to book, only enter name and phone number.
- Standard timezone is assumed to be local time (e.g. Africa/Cairo) as defined in project settings.
