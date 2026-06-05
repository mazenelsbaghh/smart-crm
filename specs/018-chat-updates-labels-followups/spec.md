# Feature Specification: Real-time Chat Sync, AI Labeling Flexibility, and Follow-up Automation Rules

**Feature Branch**: `018-chat-updates-labels-followups`

**Created**: 2026-06-02

**Status**: Draft

**Input**: User description: "Real-time chat synchronization (inbox updates immediately without refresh), flexible/non-biased AI customer labeling (e.g. asking for details shouldn't bias to price inquiry, allow new labels if existing ones don't match), and automated follow-up lifecycle (ensure follow-up created on new messages, automatically complete old pending follow-up on customer or agent replies, reschedule/send on time)."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Real-time Inbox Updates (Priority: P1)

Agents should see incoming customer messages, AI-generated replies, and new conversations immediately in their inbox view without having to manually refresh the page.

**Why this priority**: Crucial for real-time responsiveness and a premium, modern chat application experience.

**Independent Test**: Send a message from a new customer phone number, and verify that the conversation card appears automatically at the top of the conversation list in the frontend inbox without refreshing. Send an AI/agent response and verify the message is instantly appended to the timeline and the conversation list order updates dynamically.

**Acceptance Scenarios**:

1. **Given** the agent is on the inbox page, **When** a new message is received from a customer whose conversation does not currently exist in the sidebar list, **Then** the sidebar list should automatically fetch and display the new conversation card.
2. **Given** the agent is on the inbox page, **When** any message (incoming or outgoing) is saved in the database, **Then** the matching conversation's `lastMessageAt` updates and the conversation list automatically sorts itself to put that conversation at the top.
3. **Given** the agent has a conversation open, **When** the AI replies to the customer's message, **Then** the message bubble appears in the active chat area immediately and the typing indicator is cleared.

---

### User Story 2 - Flexible AI Customer Labeling (Priority: P1)

The AI should categorize customers using accurate, natural labels. If a customer asks for "details" (تفاصيل), the AI should not be biased/forced to label it as "Price Inquiry" (استفسار عن السعر) if it's not a pricing query. The AI should reuse existing database labels where they fit, but must be allowed to create a new appropriate label if none of the existing labels match.

**Why this priority**: Ensures the CRM data remains clean, accurate, and reflects the true customer need, rather than hardcoding or over-biasing toward price.

**Independent Test**: Send a message asking "I want to know the details of the course" (عايز تفاصيل الكورس) and verify that the AI labels the customer appropriately (e.g., "استفسار عن التفاصيل" or similar) instead of "Price Inquiry" (استفسار عن السعر).

**Acceptance Scenarios**:

1. **Given** a customer messages requesting details about the product, **When** the AI analyzes the message and suggests updates, **Then** it should generate a custom, descriptive Arabic label (max 3 words) matching the specific query.
2. **Given** existing labels exist in the database, **When** the AI categorizes the customer, **Then** it should select from the list of existing labels only if one of them is a perfect fit, otherwise it is allowed to generate a new appropriate label.

---

### User Story 3 - Automated Follow-up Lifecycle (Priority: P1)

The follow-up engine must ensure a follow-up is scheduled for every active customer, but old pending follow-ups must be marked as "Completed" immediately whenever a new message is exchanged (either customer replies or agent replies) to avoid sending duplicate or obsolete reminders.

**Why this priority**: Automates proactive marketing follow-ups without spamming customers with out-of-date automated messages.

**Independent Test**: Verify that a customer with a pending follow-up has that follow-up marked as "Completed" the moment they reply or when the agent replies. Verify that a new follow-up is scheduled in the "Pending" state after the interaction, which gets sent automatically if no further replies occur before its due date.

**Acceptance Scenarios**:

1. **Given** a customer has a follow-up scheduled with status "Pending", **When** a new message is received from the customer, **Then** the pending follow-up is immediately updated to status "Completed".
2. **Given** a customer has a follow-up scheduled with status "Pending", **When** a new manual message is sent by an agent, **Then** the pending follow-up is immediately updated to status "Completed".
3. **Given** any new interaction occurs, **When** a new follow-up is scheduled (either suggested custom by AI or a default nurturing follow-up in 24 hours), **Then** it is saved as "Pending" and automatically executed at its due date if the customer does not reply before then.
4. **Given** the AI completes a reply and explicitly determines no follow-up is needed (FollowUpNeeded = false), **When** processing is complete, **Then** any default pending follow-up created during the message receipt is marked as "Completed".

---

### Edge Cases

- **Multiple concurrent messages**: If the customer sends multiple messages in a row, only one new follow-up should remain pending at any time (the previous ones are completed, and the newest one is created/updated).
- **SignalR disconnection**: If the user's browser briefly disconnects and reconnects to SignalR, the conversation list and message history should re-sync.
- **AI Failure**: If the Gemini API fails or is slow, the default follow-up (Nurturing, 24h) remains active as a fallback.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The React frontend MUST automatically listen to `ReceiveMessage` and update the active message history and the conversation list.
- **FR-002**: The frontend conversation list MUST dynamically sort conversations in descending order of `lastMessageAt` upon receiving a message.
- **FR-003**: The frontend MUST query and append the conversation card to the list if a message is received for a conversation that does not exist in the current state.
- **FR-004**: The AI prompt logic in `AIMarketingBrain.cs` MUST allow the AI to invent new labels if existing ones are not a perfect fit.
- **FR-005**: The mock GeminiClient logic MUST NOT bias the "تفاصيل" keyword to the price inquiry label fallback.
- **FR-006**: The backend MUST mark all existing "Pending" follow-ups for a customer as "Completed" when an incoming webhook message is received or an outgoing agent message is sent.
- **FR-007**: The backend MUST schedule a default "Pending" follow-up (Nurturing, 24 hours) upon receiving a new message or sending an agent message.
- **FR-008**: The `CRMAutoUpdateEngine` MUST update the scheduled follow-up with the AI's custom suggested details if the AI specifies follow-up is needed, or mark it as "Completed" if the AI specifies follow-up is not needed.

### Key Entities

- **FollowUp**: Represents a scheduled automated follow-up message for a customer. Key attributes: `CustomerId`, `DueDate`, `Notes`, `Type` (Nurturing, AppointmentReminder), `Status` (Pending, Completed, Missed).
- **Conversation**: Represents a chat thread. Key attributes: `CustomerId`, `Status` (Open, Pending, Resolved, Closed), `LastMessageTimestamp`.
- **Message**: Represents a single text or media message. Key attributes: `ConversationId`, `Direction` (Incoming, Outgoing), `Content`.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Chat timeline and conversation list update in under 500ms from the database save.
- **SC-002**: 100% of incoming customer messages and manual agent messages successfully complete previous pending follow-ups.
- **SC-003**: AI categorizes general detail inquiries under descriptive labels like "استفسار عن التفاصيل" rather than forcing "استفسار عن السعر".

## Assumptions

- We assume the existing SignalR hub is fully functioning and clients can connect.
- We assume local server time is synchronized with UTC.
- We assume that updating a follow-up's status to "Completed" is sufficient to prevent the Hangfire job from executing it.
