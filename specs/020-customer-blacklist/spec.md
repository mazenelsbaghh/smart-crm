# Feature Specification: Customer Blacklist for AI Exclusion (حظر الرد الآلي بالذكاء الاصطناعي)

**Feature Branch**: `020-customer-blacklist`

**Created**: 2026-06-05

**Status**: Draft

**Input**: User description: "عايز اضيف البلاك ليسشت ان ال ai ميردش عليهم" (I want to add the blacklist so that the AI doesn't reply to them)

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Toggle Blacklist Status for a Customer (Priority: P1)

As an agent/administrator, I want to add or remove a customer to/from the AI reply blacklist, so that I can control whether the AI automatically replies to their incoming WhatsApp messages.

**Why this priority**: Crucial capability enabling users to flag customers for exclusion. This is the foundation of the feature.

**Independent Test**: Can be fully tested by opening a customer profile, checking/unchecking the "AI Exclusion / Blacklist" toggle, and verifying that the state is saved and shows a "Blacklisted" status.

**Acceptance Scenarios**:

1. **Given** an existing customer profile is open in the CRM details view, **When** the agent checks the "حظر الرد الآلي (Blacklist)" option and clicks "حفظ التغييرات" (Save Changes), **Then** the customer's blacklist status is updated to true in the database.
2. **Given** a customer is blacklisted, **When** the agent opens their profile details, **Then** the "حظر الرد الآلي (Blacklist)" option is shown as checked.
3. **Given** a customer is blacklisted, **When** the agent unchecks the "حظر الرد الآلي (Blacklist)" option and saves, **Then** the customer's blacklist status is updated to false in the database.

---

### User Story 2 - AI Auto-Reply Exclusion (Priority: P1)

As the system, when a blacklisted customer sends an incoming WhatsApp message, the AI auto-reply engine should be bypassed completely, ensuring no automatic response is sent or generated, and the typing indicator is not displayed.

**Why this priority**: This is the core functional goal. The AI must ignore messages from blacklisted contacts.

**Independent Test**: Send a WhatsApp message from a blacklisted customer number, verify that the AI does not respond, and verify that the "AI is typing..." indicator is never broadcasted via SignalR.

**Acceptance Scenarios**:

1. **Given** a customer has `IsBlacklisted` set to true, **When** they send a message to the WhatsApp integration, **Then** the system saves the incoming message to the database but does NOT generate an AI reply, does NOT trigger AI typing state, and does NOT publish any auto-reply events.
2. **Given** a customer has `IsBlacklisted` set to false, **When** they send a message to the WhatsApp integration, **Then** the system operates normally (shows typing status and generates an AI reply as configured).

---

### User Story 3 - Visual Indicators in CRM Customer List (Priority: P2)

As an agent, I want to easily see which customers are blacklisted from the customer table view, so that I don't have to open each profile to check.

**Why this priority**: Improves usability and user experience for managing contacts.

**Independent Test**: Load the Customer List in CRM, check if blacklisted customers display a clear visual indicator.

**Acceptance Scenarios**:

1. **Given** a customer has `IsBlacklisted` set to true, **When** the CRM customer list is loaded, **Then** a distinctive red/muted badge saying "مستبعد من الرد الآلي" or "Blacklisted" is displayed next to their name.

---

### Edge Cases

- **What happens when a new customer message is received for the first time?**
  - By default, new customers created automatically when receiving a message are not blacklisted (`IsBlacklisted` defaults to false) and AI will respond normally.
- **Can an agent still reply manually to a blacklisted customer?**
  - Yes. The blacklist only blocks the automated AI replies and automated AI typing indicators. Agent manual messages sent through the chat inbox should work normally.
- **What happens if a customer is blacklisted while the AI is already in the middle of generating a reply or during the typing delay?**
  - The check should be performed both before entering the queue/reply loop and inside the reply worker, to ensure that if they are blacklisted, any ongoing scheduled or delayed AI replies are immediately canceled or skipped.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The `Customer` model MUST support a boolean field `IsBlacklisted` indicating whether the customer is excluded from AI replies. This field MUST default to `false`.
- **FR-002**: The CRM Customer edit API (`PUT /api/customers/{id}`) MUST allow setting the `IsBlacklisted` field.
- **FR-003**: The CRM Customer details API (`GET /api/customers/{id}`) and list API (`GET /api/projects/{projectId}/customers`) MUST include the `IsBlacklisted` field in the response payload.
- **FR-004**: The AI background worker (`AIReplyWorker`) MUST verify the customer's `IsBlacklisted` status before starting AI generation and skip processing if the customer is blacklisted.
- **FR-005**: The Webhook Controller (`WebhookController`) MUST check the customer's `IsBlacklisted` status and suppress the "AITyping" SignalR broadcast if the customer is blacklisted.
- **FR-006**: The React frontend customer details panel (`CustomerDetail`) MUST present a togglable checkbox/switch for the blacklist status, labeled "حظر الرد الآلي بالذكاء الاصطناعي (Blacklist)".
- **FR-007**: The React frontend customer list table (`CustomerList`) MUST display a prominent tag/badge next to the customer name if they are blacklisted.

### Key Entities

- **Customer**: Represents the contact. Added attribute:
  - `IsBlacklisted` (Boolean): Flag representing if the customer is blacklisted from AI auto-replies.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of messages received from blacklisted customers do not trigger an AI reply or an AI typing indicator.
- **SC-002**: Agents can successfully toggle the blacklist status of any customer in the CRM dashboard in under 2 clicks.
- **SC-003**: Blacklist status changes are persisted in the database instantly and reflect immediately in the customer list and details views.

## Assumptions

- Auto-created customers start with `IsBlacklisted = false`.
- Custom manual messaging by human agents remains completely functional and is unaffected by the blacklist.
- Re-enabling AI replies is as simple as turning off the toggle in CRM details.
