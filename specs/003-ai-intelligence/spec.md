# Feature Specification: AI Intelligence, CRM Auto-Updates, Assignment & Smart Messaging

**Feature Branch**: `003-ai-intelligence`

**Created**: 2026-05-24

**Status**: Draft

**Input**: User description: "AI becomes smart — marketing brain, human-like messaging, CRM auto-update, assignment engine, scheduler, and notifications."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - AI Marketing Brain & Reply Style Selection (Priority: P1)
The AI system analyzes incoming messages to understand customer psychology, buyer intent, trust levels, and objections. It selects a tailored reply style (Fast, Casual, Sales, Support, VIP, Complaint, Follow-up) and responds with high marketing effectiveness.

**Why this priority**: Core AI intelligence layer to make replies relevant and sales-oriented.

**Independent Test**: Can be verified by sending customer messages of different types (e.g. buying interest vs. complaint) and asserting the selected reply style and tone in the AI response log.

**Acceptance Scenarios**:
1. **Given** a customer message expressing intent to purchase immediately, **When** the AI processes it, **Then** the reply style is identified as "Sales" or "Fast", and the response includes a clear Call to Action (CTA) and urgency elements.
2. **Given** a customer message complaining about service, **When** the AI processes it, **Then** the reply style is identified as "Complaint" or "Support", and the response includes empathy and objection handling.

---

### User Story 2 - Smart Human-Like Messaging (Priority: P1)
When the AI generates a reply, it decides whether to split the reply into multiple messages (chunks) to look like a human typing, with realistic delays between them, and respects anti-ban throttling limits.

**Why this priority**: Avoids WhatsApp bans and increases user engagement by making the bot feel human.

**Independent Test**: Can be verified by sending a prompt that generates a long multi-part response and verifying that the webhook gateway receives multiple chunk requests with delays.

**Acceptance Scenarios**:
1. **Given** an AI response containing two distinct paragraphs, **When** the human messaging engine processes it, **Then** it sends two separate messages with a delayed typing gap between them.

---

### User Story 3 - AI CRM Auto-Updates & Entity Extraction (Priority: P1)
The AI analyzes incoming messages to extract key customer metadata (city, budget, interests, dates) and proposes CRM updates.

**Why this priority**: Minimizes manual data entry and automates lead capturing.

**Independent Test**: Can be verified by sending a message containing customer details (e.g., "I want to buy a house in Cairo with a budget of 2M EGP next month") and asserting that a CRM update proposal is generated and applied.

**Acceptance Scenarios**:
1. **Given** a customer message mentioning a location and budget, **When** the AI extracts the entities, **Then** it publishes a `CRMUpdateSuggested` event, which is processed to update the customer's CRM profile automatically.

---

### User Story 4 - AI Intent & Sentiment Analysis (Priority: P1)
The AI classifies the conversation's active intent (inquiry, complaint, purchase, follow-up, greeting) and customer sentiment (positive, neutral, negative, angry) to update the lead score.

**Why this priority**: Necessary for proper priority routing and filtering.

**Independent Test**: Send angry messages and verify that the conversation sentiment is marked as "negative/angry" and lead score is adjusted.

**Acceptance Scenarios**:
1. **Given** an angry customer message, **When** analyzed, **Then** the sentiment is set to "negative" or "angry" and the conversation is flagged for immediate human attention.

---

### User Story 5 - Assignment Engine (Priority: P1)
Conversations are routed to available agents based on agent presence, current workload, or special priority rules (e.g. VIP or complaints to supervisors).

**Why this priority**: Ensures efficient work distribution and SLA enforcement.

**Independent Test**: Invoke the routing endpoint and verify that the conversation is assigned to the agent with the lowest active load.

**Acceptance Scenarios**:
1. **Given** a new hot lead, **When** the assignment engine runs, **Then** the conversation is assigned to the available sales agent with the least workload.

---

### User Story 6 - Scheduler Engine (Priority: P1)
A background scheduler (Hangfire) handles recurring background jobs like recalculating lead/health scores, WhatsApp session health checks, queue monitoring, and overdue follow-up flags.

**Why this priority**: Necessary for automating operations and periodic data consistency checks.

**Independent Test**: Schedule a follow-up and verify that Hangfire runs the background check and marks it "Missed" if overdue.

**Acceptance Scenarios**:
1. **Given** a pending follow-up whose due date has passed, **When** the scheduler runs, **Then** the follow-up status is updated to "Missed" within the execution interval.

---

### User Story 7 - SignalR Notifications Engine (Priority: P1)
Real-time notification alerts (VIP customer activity, SLA breaches, complaints) are pushed to agents via a SignalR hub.

**Why this priority**: Essential for immediate agent response to critical issues.

**Independent Test**: Establish a SignalR connection and trigger an SLA breach to verify the client receives the push alert.

**Acceptance Scenarios**:
1. **Given** a SignalR client connected as an agent, **When** an SLA breach event occurs, **Then** a notification is pushed instantly to the client.

---

### User Story 8 - Basic Reports (Priority: P2)
Generate daily operations reports, follow-up status summaries, and AI performance reports.

**Why this priority**: High-level reporting for management.

**Independent Test**: Call the report API endpoints and verify they return correct aggregated JSON data.

**Acceptance Scenarios**:
1. **Given** completed conversations and follow-ups in the system, **When** requesting the daily report, **Then** it returns the count of active, resolved, and missed items.

---

### Edge Cases
- **Gemini Rate Limits / Outages**: If the Gemini API is down, the system must fall back to a default professional reply without failing, log the error, and notify the administrator.
- **Empty Message Chunks**: If message chunking results in empty or invalid strings, they must be discarded and only valid content sent.
- **No Available Agents**: If all agents are offline, routing should assign the conversation to a fallback project queue and flag it as unassigned.
- **Double Scheduler Execution**: Ensure background tasks are idempotent so multiple instances of the scheduler service do not process the same job twice.

## Requirements *(mandatory)*

### Functional Requirements
- **FR-001**: System MUST analyze customer messages to identify intent, sentiment, reply style, and CRM entities.
- **FR-002**: System MUST support splitting AI replies into multiple message chunks with configurable typing delays.
- **FR-003**: System MUST automatically generate CRM updates based on AI extraction and trigger updates or approval requests.
- **FR-004**: System MUST track agent presence and assign conversations based on least-busy workload and priority routing rules.
- **FR-005**: System MUST integrate Hangfire to handle background schedules, health checks, and metrics recalculations.
- **FR-006**: System MUST expose a secure Hangfire Dashboard under `/hangfire` for project administrators.
- **FR-007**: System MUST push real-time SignalR notifications for VIP alerts, complaints, and SLA breaches.
- **FR-008**: System MUST provide API endpoints for daily, follow-up, and AI performance reports.

### Key Entities
- **AgentPresence**: Tracks agent online/offline status and current active conversation load.
- **CRMUpdateProposal**: Represents a suggested update (e.g. key-value update) extracted by AI, with audit trail and status (Applied, PendingApproval, Rejected).
- **JobSchedule**: Hangfire job registration tracking periodic system tasks.
- **NotificationAlert**: Real-time notification entity pushed to agents.

## Success Criteria *(mandatory)*

### Measurable Outcomes
- **SC-001**: Intent and sentiment analysis must complete and yield a structured output within 1.5 seconds of message aggregation completion.
- **SC-002**: Message chunking must respect a minimum delay of 1 second and maximum 5 seconds between consecutive message sends.
- **SC-003**: CRM entity extraction accuracy must be 90% or higher for basic parameters (city, budget, interests).
- **SC-004**: SignalR notifications must be delivered to online clients in less than 200ms from event publication.
- **SC-005**: Report endpoints must return aggregated data for up to 10,000 records in under 300ms.

## Assumptions
- The Google Gemini 3.5 Flash model is used for all prompt analyses and replies.
- Redis is available for presence tracking and temporary session states.
- PostgreSQL database stores Hangfire job logs and core persistent states.
- Client applications can establish SignalR WebSockets connections over port 5000/5001.
