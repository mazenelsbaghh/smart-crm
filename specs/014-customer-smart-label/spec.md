# Feature Specification: Customer Smart Label

**Feature Branch**: `014-customer-smart-label`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "I want to add a label for each customer, and it should change with every message, and this should also be in the CRM and visible in the chat as well, very important."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Automated Label Prediction (Priority: P1)

Every incoming message from a customer should be analyzed by the AI to determine their current status or need, classifying it into a short Arabic label (max 3 words, e.g., "استفسار عن الأسعار", "طلب شراء", "شكوى", "ترحيب"). This label is saved immediately to the customer's profile.

**Why this priority**: Core value of the feature to automate customer categorization dynamically with zero manual input.

**Independent Test**: Can be tested by sending a message containing a purchase intent (e.g., "أريد شراء المنتج") and verifying that the customer's label in the database updates to a purchase-related label.

**Acceptance Scenarios**:

1. **Given** a customer sends the message "بكم سعر الاشتراك السنوي؟", **When** the message is processed by the AI, **Then** the customer's label is updated to "استفسار عن السعر" or similar.
2. **Given** a customer sends the message "المنتج متأخر جداً ولم يصلني بعد", **When** the message is processed by the AI, **Then** the customer's label is updated to "شكوى" or similar.

---

### User Story 2 - Real-time Chat Label Display (Priority: P1)

The customer's smart label should be visible next to their name in the active Chat/Inbox panel list and in the chat header. The label must update in real-time when new messages arrive, without requiring page reloads.

**Why this priority**: Critical for operators to immediately see the customer's state or inquiry type while chatting.

**Independent Test**: Open the inbox page in a browser, send a message from a customer, and verify that the label badge next to the customer's name updates in real-time.

**Acceptance Scenarios**:

1. **Given** an operator has the Inbox page open, **When** a customer sends a greeting message, **Then** the label badge "ترحيب" appears next to the customer's name in the left sidebar and header in under 1 second after processing.
2. **Given** a customer's label is "استفسار", **When** they send a message complaining about a bug, **Then** the label badge next to their name changes to "شكوى" in real-time.

---

### User Story 3 - CRM Board & List Integration (Priority: P2)

The customer's smart label should be displayed in the CRM customer list table and inside the deal cards on the Pipeline board, ensuring sales agents are aligned on the customer's status across all pages.

**Why this priority**: Ensures consistent metadata and alignment across the CRM pipeline.

**Independent Test**: Navigate to the CRM board and verify that the deal cards show the correct Arabic smart label badge under or next to the customer's name.

**Acceptance Scenarios**:

1. **Given** a customer has an active deal in the "Proposal" stage and a smart label "طلب عرض سعر", **When** the operator opens the Pipeline board, **Then** the deal card shows "طلب عرض سعر" next to the customer's name.
2. **Given** a customer in the CRM customer list has a smart label "شراء مكتمل", **When** the customer list is rendered, **Then** the table row displays the label "شراء مكتمل" in the customer details cell.

---

### Edge Cases

- **What happens when the AI response is invalid or fails to classify?**
  - The system preserves the last existing label. If no label exists, it sets the label to a default fallback like "استفسار عام" (General Inquiry).
- **What happens if a customer sends a message while AI Auto-Reply is disabled?**
  - Even if auto-reply is disabled, the AI analysis engine should still process the message to update the classification label, ensuring the CRM stays updated.
- **What happens when an agent manually updates the customer profile?**
  - The agent can view the current label. If they manually edit fields, the label is preserved until the next incoming message triggers an automated update.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST add a `Label` property (string, nullable) to the `Customer` domain model and persist it in the PostgreSQL database.
- **FR-002**: The AI Marketing Brain MUST predict a short Arabic classification label (maximum 3 words, e.g., "استفسار عن السعر", "طلب شراء", "شكوى", "ترحيب") from the customer's message and include it in the analysis result.
- **FR-003**: The backend MUST update the customer's `Label` in the database whenever a new message is processed (both in automated high-confidence blocks and general analysis).
- **FR-004**: The backend MUST broadcast the updated customer object (including the `Label`) to the SignalR tenant group.
- **FR-005**: The frontend Chat/Inbox list MUST listen to the SignalR update and dynamically render the label badge next to the customer's name.
- **FR-006**: The frontend CRM table and Pipeline board deal cards MUST display the label badge.
- **FR-007**: The frontend Customer Detail modal MUST display the customer's smart label.

### Key Entities

- **Customer**: Represents a contact in the CRM. Adding `Label` (string, nullable) to represent the current AI-predicted state or inquiry category.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of processed incoming messages update the associated customer's `Label` field in PostgreSQL.
- **SC-002**: The backend broadcasts the updated label to all connected tenant browsers via SignalR within 500ms of message processing.
- **SC-003**: The frontend displays the updated label next to the customer's name in the inbox sidebar, chat header, CRM table, and CRM pipeline cards in real-time.

## Assumptions

- We will reuse the existing Gemini API configuration.
- We will reuse the existing SignalR presence and message hubs.
- The default fallback label when classification fails is "استفسار عام".
