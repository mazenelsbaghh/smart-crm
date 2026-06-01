# Feature Specification: AI Context, Delay Tuning & Auto CRM Deal Sync

**Feature Branch**: `013-ai-context-delay-crm-sync`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "Inject chat history (last 15 messages) and customer memory (facts, objections, summary) into the AI auto-reply prompt. Sync automated CRM/Approvals budget updates with active deals. Set message aggregation silence window to 30-50 seconds. Set chunk typing simulation delay to 5-9 seconds."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Contextualized AI Auto-Reply (Priority: P1)

The AI auto-reply should respond using the context of recent chat history and long-term customer memory so it doesn't forget details or treat every message as the first time.

**Why this priority**: Necessary for human-like, coherent conversation.

**Independent Test**: Can be tested by sending a message, waiting for response, sending a follow-up referring to the first message, and verifying that the AI understands the context.

**Acceptance Scenarios**:

1. **Given** a customer has a memory summary stating "Interested in annual subscription", **When** they send "How much is it?", **Then** the AI auto-reply refers to the Pricing Policy and mentions the annual subscription discount.
2. **Given** a conversation has a previous message stating "I live in Cairo", **When** the customer sends "What is the shipping time to my city?", **Then** the AI refers to Cairo and mentions the shipping duration without asking for their city.

---

### User Story 2 - Automated CRM Budget & Deal Sync (Priority: P1)

Automated budget updates suggested by the AI (high confidence) or approved manually by supervisors should automatically update the customer's active deal amount.

**Why this priority**: Keeps the CRM pipeline accurate without manual duplicate entries.

**Independent Test**: Trigger a high confidence budget suggestion and verify the active open deal amount is updated to match the new budget.

**Acceptance Scenarios**:

1. **Given** a customer has an active open deal, **When** the AI detects a budget of `$10,000` with high confidence, **Then** the customer's budget is updated, and the active deal amount changes to `10000`.
2. **Given** a budget suggestion of `$15,000` is pending approval, **When** the supervisor approves it, **Then** the customer's budget and the active deal amount are updated to `15000`.

---

### User Story 3 - Natural Aggregation & Typing Delays (Priority: P2)

The system should aggregate messages sent close together and simulate human typing speeds when replying.

**Why this priority**: Avoids spamming the customer with multiple rapid AI responses.

**Independent Test**: Send 3 messages within 10 seconds and check that they are received as one aggregated text by the AI after 30-50 seconds of silence, and that subsequent chunked messages are spaced out by 5-9 seconds.

**Acceptance Scenarios**:

1. **Given** a customer sends 2 messages in a row, **When** the system receives them, **Then** it waits 30 to 50 seconds since the last message before triggering the AI reply.
2. **Given** the AI generated a 2-paragraph response, **When** sending it back to the customer, **Then** the system sends paragraph 1, waits 5 to 9 seconds, and then sends paragraph 2.

---

### Edge Cases

- What happens if the customer memory does not exist?
  - The AI relies solely on the recent chat history and knowledge base context.
- What if the customer sends another message while the 30-50 seconds aggregation timer is running?
  - The timer is reset, and a new 30-50 seconds delay starts from the time of the new message.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST fetch and format the last 15 messages of the active open conversation as chat history context.
- **FR-002**: The system MUST fetch and format the customer's `CustomerMemory` as memory context.
- **FR-003**: The AI engine MUST receive both chat history and customer memory contexts in its prompt.
- **FR-004**: The message aggregator MUST wait for a randomized delay of 30 to 50 seconds of silence before processing.
- **FR-005**: The typing delay engine MUST wait for a clamped delay of 5 to 9 seconds (5000 to 9000 ms) between sending consecutive message chunks.
- **FR-006**: Both `CRMAutoUpdateEngine` and `ApprovalsController` MUST update the active open deal's Amount when a budget is updated.

### Key Entities

- **CustomerMemory**: Persists long-term facts, objections, and summaries.
- **MessageAggregatedEvent**: Carries the aggregated text of multiple incoming messages.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Message aggregation silence window is strictly between 30 and 50 seconds.
- **SC-002**: Typing delay between AI message chunks is strictly between 5 and 9 seconds.
- **SC-003**: Auto-applied or approved CRM budget changes reflect instantly on the active deal amount.
