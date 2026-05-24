# Feature Specification: Core Foundation

**Feature Branch**: `phase/1-core-foundation`

**Created**: 2026-05-24

**Status**: Draft

**Input**: User description: "Phase 1: Auth, Projects, WhatsApp Gateway & Basic Conversations"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - User Authentication & Authorization (Priority: P1)

An owner or administrator can register a new account, login, receive a secure JWT token, and refresh their session to interact with protected endpoints.

**Why this priority**: Fundamental requirement for security and role-based routing. Prevents unauthorized access to backend operations.

**Independent Test**: Can be fully tested using HTTP client calls (register, login, access protected endpoints with valid/invalid tokens, refresh token, logout).

**Acceptance Scenarios**:
1. **Given** no existing user with email "admin@smartcore.com", **When** a registration request is sent to `POST /api/auth/register`, **Then** a new user is created with a hashed password, and the API returns a success message.
2. **Given** an existing user with email "admin@smartcore.com", **When** a login request is sent to `POST /api/auth/login` with correct credentials, **Then** the API returns an Access Token (JWT) and a Refresh Token.
3. **Given** an expired Access Token but a valid Refresh Token, **When** sent to `POST /api/auth/refresh`, **Then** a new Access Token and Refresh Token pair is returned.
4. **Given** an active Refresh Token, **When** sent to `POST /api/auth/logout`, **Then** the refresh token is blacklisted/revoked, and subsequent refresh requests fail.

---

### User Story 2 - Project Creation & Settings Isolation (Priority: P1)

A authenticated user can create a Project, view project details, update settings, and ensure that all customer and conversation data query results are strictly isolated by Project ID.

**Why this priority**: Critical multi-tenancy requirement. Data leakage between different projects must be prevented from the first day.

**Independent Test**: Can be tested by creating projects under User A and User B, verifying User A cannot view or access User B's project data or settings.

**Acceptance Scenarios**:
1. **Given** an authenticated user, **When** a request is sent to `POST /api/projects` with a project name and settings, **Then** a new project is created and linked to the user.
2. **Given** a user associated with Project A, **When** they request `GET /api/projects`, **Then** only Project A is returned in the list.
3. **Given** User A is only authorized on Project A, **When** User A attempts to request `GET /api/projects/{id_of_project_b}`, **Then** the system returns a 403 Forbidden error.

---

### User Story 3 - WhatsApp Gateway Connection (Priority: P1)

An administrator can request the Baileys gateway to start a session, fetch the QR code for authentication, link a WhatsApp account, and verify the connection status.

**Why this priority**: Foundational component for WhatsApp connectivity. Without this gateway, no messages can be sent or received.

**Independent Test**: Can be verified by running the gateway server mock/tests, initializing a session, and fetching connection status endpoints.

**Acceptance Scenarios**:
1. **Given** a running WhatsApp gateway service, **When** a request is made to `POST /api/whatsapp/session/start` for Project A, **Then** a new Baileys session is initialized.
2. **Given** a session starting up, **When** a request is made to `GET /api/whatsapp/session/qr`, **Then** a QR code (as text/base64 string) is returned to scan in the WhatsApp app.
3. **Given** a successfully linked session, **When** `GET /api/whatsapp/session/status` is queried, **Then** the status reports "Connected" along with the connected phone number.

---

### User Story 4 - Real-time Message Ingestion & Aggregation (Priority: P1)

When a customer sends a sequence of WhatsApp messages to the connected number, the gateway forwards them to the backend webhook, which saves them and groups consecutive messages within a time window into a single aggregated event.

**Why this priority**: Required to prevent the AI from generating separate responses for every single line a user sends in rapid succession (e.g., "Hi", "I have a question", "about pricing").

**Independent Test**: Can be tested by posting multiple message payloads to the webhook, waiting 5 seconds, and verifying a single `MessageAggregated` event is emitted.

**Acceptance Scenarios**:
1. **Given** a new customer number, **When** they send their first message, **Then** a new Customer profile is auto-created, a new Conversation is started, and the message is saved.
2. **Given** a conversation, **When** a customer sends 3 messages within 3 seconds, **Then** the messages are accumulated in Redis, and after a 5-second silence window, a single `MessageAggregated` event containing all 3 message contents is emitted to RabbitMQ.

---

### User Story 5 - AI Auto-Response via Gemini (Priority: P1)

When a `MessageAggregated` event is received, the system retrieves customer context, calls the Gemini 3.5 Flash API with a structured prompt, generates a response, and sends it back to the customer's WhatsApp number.

**Why this priority**: core intelligence capability of the product. Demonstrates end-to-end automated conversation handling.

**Independent Test**: Can be verified by posting an aggregated message event, mocking/calling Gemini API, and asserting that a reply event is published and sent.

**Acceptance Scenarios**:
1. **Given** a `MessageAggregated` event, **When** the AI worker processes it, **Then** it fetches recent conversation context, calls Gemini 3.5 Flash, and publishes an `AIReplyGenerated` event.
2. **Given** an `AIReplyGenerated` event, **When** the reply sender processes it, **Then** the response is sent to the customer via the WhatsApp Gateway API.

---

### User Story 6 - Customer Profile & Follow-Up Management (Priority: P2)

An agent can view customer CRM details, update customer metadata (tags, notes, city), and schedule manual follow-up reminders.

**Why this priority**: Essential CRM dashboard features for agent oversight and proactive sales outreach.

**Independent Test**: Can be tested by retrieving customer listings, editing metadata, creating a follow-up, and verifying the background job triggers alerts.

**Acceptance Scenarios**:
1. **Given** a customer profile, **When** an agent updates their city and adds a tag, **Then** the customer details are updated in the database.
2. **Given** a customer, **When** an agent creates a follow-up with a due date, **Then** a follow-up record is created in a "Pending" status.
3. **Given** a pending follow-up that has passed its due date, **When** the background scheduler runs, **Then** the follow-up is marked as "Missed".

---

## Edge Cases

- **WhatsApp Session Disconnection**: If the Baileys session drops (due to internet failure, phone power off, or session logged out from phone), the gateway must attempt auto-reconnection and report the error state correctly in the status API.
- **Webhook Retry Storm**: If the backend is down, the gateway webhook dispatcher must retry sending incoming messages with exponential backoff to avoid data loss.
- **Concurrent Message Ingestion**: Multiple messages sent concurrently from the same user must be locked and processed sequentially to prevent duplicate customer/conversation creation.
- **Gemini API Outage/Rate-limit**: If the Gemini API is rate-limited or fails, the AI worker must retry with exponential backoff, or fall back to notifying supervisors/agents instead of failing silently.
- **Expired/Malformed JWT**: The API must return appropriate HTTP status codes (401 Unauthorized for expired, 403 Forbidden for insufficient permissions) rather than generic 500 errors.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST expose REST API endpoints for user registration, login, refresh tokens, and logout.
- **FR-002**: System MUST hash user passwords using a secure algorithm (BCrypt/PBKDF2) before storage.
- **FR-003**: System MUST isolate all project settings, conversations, messages, and customers using a tenant-based Project ID.
- **FR-004**: System MUST allow CRUD operations on Projects and Project Settings.
- **FR-005**: System MUST run a Node.js Baileys gateway that connects to WhatsApp, persists session credentials, and exposes REST APIs for session management and sending messages.
- **FR-006**: System MUST expose a public webhook endpoint on the ASP.NET Core backend to receive WhatsApp events (message, status, receipt).
- **FR-007**: System MUST automatically create a new Customer profile and Conversation thread if an incoming message is received from a phone number not in the database.
- **FR-008**: System MUST implement a Redis-based message aggregator that waits for a 5-second silence window from a sender before publishing a single aggregated message event.
- **FR-009**: System MUST run an AI background worker that processes aggregated messages, formats them with customer context, queries the Gemini 3.5 Flash API, and queues a reply.
- **FR-010**: System MUST allow manual scheduling of follow-up tasks for customers with a due date and alert agents when they become overdue.

### Key Entities *(include if feature involves data)*

- **User**: Represents a platform user (Owner, Admin, Supervisor, Agent). Contains email, password hash, status, and role.
- **Project**: Represents a tenant. Contains project name, status, and settings.
- **ProjectSettings**: Configuration for a project (e.g., AI auto-reply enabled, timezone, business hours).
- **Customer**: Represents a WhatsApp contact. Contains phone number, name, city, tags, notes, lead score, and Project ID.
- **Conversation**: Represents a chat thread. Contains status (Open/Pending/Resolved/Closed), last message timestamp, and Project ID.
- **Message**: Represents an individual text or media message. Contains message ID, direction (Incoming/Outgoing), content, type, timestamp, and Conversation ID.
- **FollowUp**: Represents a scheduled task. Contains customer ID, task details, due date, status (Pending/Done/Missed).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of incoming WhatsApp messages from new contacts auto-generate customer records and conversation threads in under 2 seconds.
- **SC-002**: Message aggregator groups multiple messages sent within a 5-second window into a single event with 100% accuracy.
- **SC-003**: AI auto-response generation (excluding network latency of external APIs) is processed and queued within 1.5 seconds of aggregation window closure.
- **SC-004**: Access to project data is 100% isolated: queries using User credentials of Project A must return zero records from Project B.

## Assumptions

- We assume the Google Gemini API key will be configured in environment variables and will be accessible by the AI Worker.
- WhatsApp media file handling in this phase is restricted to saving reference metadata (text, image, voice type indicator); full media download and streaming to MinIO is scheduled for Phase 5.
- Next.js frontend UI dashboard is out of scope for Phase 1; all verification will be done via HTTP endpoints and pytest automation.
