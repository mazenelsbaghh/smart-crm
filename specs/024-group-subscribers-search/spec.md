# Feature Specification: Group Subscribers Search

**Feature Branch**: `024-group-subscribers-search`

**Created**: 2026-06-16

**Status**: Draft

**Input**: User description: "عايز اضيف ف المشتركين ف المجموعه يبقي فيه سرش بس و ارفع بعدها@[/Users/mazenelsbagh/mazen mac/apps/smart whatsapp/deploy/deploy.sh]"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Search Group Participants by Name or Phone (Priority: P1)

As a project administrator, I want to search through the list of participants/subscribers in a specific group appointment by typing their name or phone number, so that I can quickly find, update, or manage a specific student.

**Why this priority**: High priority because groups can have large capacities, making scrolling through a long list of bookings slow and error-prone.

**Independent Test**: Can be fully tested by clicking the "المشتركين" (Participants) button on any group appointment, typing a search query in the search bar, and verifying that the table rows dynamically filter to display only matching participants.

**Acceptance Scenarios**:

1. **Given** a group with multiple booked students, **When** I type a partial or full name of a student in the search box, **Then** only participants matching that name (case-insensitive) are shown in the list.
2. **Given** a group with multiple booked students, **When** I type a partial or full phone number of a student in the search box, **Then** only participants matching that phone number are shown in the list.
3. **Given** a filtered participants list, **When** I clear the search box, **Then** the full list of booked students is displayed.
4. **Given** a group with booked students, **When** I search for a term that does not match any student, **Then** a friendly "no results found" message is displayed instead of an empty table.
5. **Given** a search query is active in the list, **When** I close the list panel or switch to another group, **Then** the search query is reset to empty.

---

### Edge Cases

- **Special Characters/Spaces in Search**: The search query should trim leading/trailing whitespace and handle special characters gracefully without breaking the interface.
- **Empty Group**: If a group has no bookings at all, the search bar should still be rendered (or disabled) and the existing empty state message "لا يوجد مشتركون مسجلون في هذه المجموعة بعد." should be shown.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST render a text search input field above the group participants table when a group's participants list is opened.
- **FR-002**: The search input MUST dynamically filter the list of participants on the client-side in real-time as the user types.
- **FR-003**: The search logic MUST match the query against the customer's name (`customerName`) (case-insensitive) and the customer's phone number (`customerPhone`).
- **FR-004**: If the search filters out all results, the system MUST display a clear "no results found" (لم يتم العثور على نتائج تطابق البحث) message.
- **FR-005**: The system MUST reset the search input to an empty string whenever the user closes the participants list or switches to a different group.

### Key Entities

- **GroupAppointment**: Represents a group booking schedule.
- **Booking**: Represents a student's reservation within a specific group appointment (contains `customerName`, `customerPhone`, `isAttended`, `isPaid`, etc.).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can locate a specific participant in under 3 seconds using the search input.
- **SC-002**: The filtering of the participants list happens instantly (in under 100 milliseconds) on the client side without requiring additional API calls.
- **SC-003**: Clearing the search input immediately restores the full list of participants.

## Assumptions

- Search is performed entirely client-side on the list of bookings already loaded for the selected group.
- The UI language remains consistent (Arabic).
- Mobile responsiveness is maintained for the search bar layout.
