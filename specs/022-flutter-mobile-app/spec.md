# Feature Specification: Flutter Mobile Application for Smart CRM

**Feature Branch**: `022-flutter-mobile-app`

**Created**: 2026-06-10

**Status**: Draft

**Input**: User description: "حول الفرونت كامل سيبوا و اعمل ابلكيشن بفلاتر كامل شغال علي الباك ايند اللي مرفوع و عايزوا ف كل الشاشات و مظبوط و اعمل كل التاستات بتاعتك و هد الاسكل دي اعمل بيها الشكل /impeccable"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Secure Access and Project Context Management (Priority: P1)

Agents and Admins need a secure gateway to log in, register, and switch between their isolated customer projects, ensuring proper data isolation.

**Why this priority**: Core security foundation. Without secure access and proper tenant scoping, no other business logic can be safely conducted.

**Independent Test**: Can be fully tested by registering a new user, logging in with credentials, verifying a JWT token is received, and selecting/switching between projects which updates the active environment context.

**Acceptance Scenarios**:
1. **Given** the login screen, **When** the user enters valid credentials and taps Login, **Then** they are redirected to the project selector screen.
2. **Given** the login screen, **When** the user enters invalid credentials, **Then** an error message is displayed and they remain on the login page.
3. **Given** the project selector screen, **When** the user selects a project, **Then** the global application context is updated with that project's ID and they are directed to the main dashboard.

---

### User Story 2 - Real-time Chat Inbox & AI-Assisted Agent Operations (Priority: P1)

Agents need a mobile chat view to receive real-time messages from WhatsApp, send manual replies, view AI-suggested answers, approve/reject suggested RAG answers, update customer labels, and schedule follow-ups.

**Why this priority**: Main business operation interface. Real-time communication and AI auto-reply management is the core value proposition of the system.

**Independent Test**: An agent can open an active conversation, read real-time updates via WebSocket (SignalR), type a reply, see a suggested reply from the AI Company Brain, and approve/reject it.

**Acceptance Scenarios**:
1. **Given** the inbox list, **When** a new message arrives from a WhatsApp customer, **Then** the conversation item updates in real time and moves to the top of the list with an unread badge.
2. **Given** an open chat, **When** the agent receives an AI-suggested response, **Then** they can tap "Approve & Send" to dispatch it or "Reject/Edit" to rewrite it.
3. **Given** an open chat, **When** the agent taps on the customer's detail side sheet, **Then** they can add tags, change labels, toggle blacklist status, and schedule a custom follow-up.

---

### User Story 3 - Mobile CRM Customer & Deal Pipeline Management (Priority: P2)

CRM users need a visual pipeline board (Kanban or drag-and-drop stages) and a customer management directory to track lead scores, add tags, log customer notes, record budgets, log interests, and manage deal stages.

**Why this priority**: Critical for lead tracking and sales pipeline visibility.

**Independent Test**: A user can navigate to the CRM view, see list of customers with lead scores, click a customer to edit their metadata, view the Kanban deal stages, and move a deal to a new stage.

**Acceptance Scenarios**:
1. **Given** the customer list, **When** a user clicks on a customer, **Then** the app opens the profile detailing their lead score, budget, interests, and past notes, allowing immediate editing.
2. **Given** the sales pipeline view, **When** a user drags/moves a deal card from "Open" to "Won", **Then** the system updates the deal status and recalculated metrics immediately.
3. **Given** a customer profile, **When** a user flags the customer as blacklisted, **Then** future auto-replies are disabled and the customer is visually marked.

---

### User Story 4 - Visual Group Appointments Calendar & Booking (Priority: P2)

Agents and customers need a unified, visually rich calendar to schedule, view, and manage group appointments.

**Why this priority**: Essential for service-based businesses using the CRM to book group sessions and coordinate calendars.

**Independent Test**: Can be tested by navigating to the Bookings calendar, selecting a date, viewing available times, and scheduling a new group appointment which updates the visual calendar.

**Acceptance Scenarios**:
1. **Given** the Booking tab, **When** the user views the monthly/weekly calendar, **Then** all booked sessions are displayed with clear visual coding (open, filled, pending approval).
2. **Given** the appointment booking form, **When** the user enters details and submits, **Then** the appointment is booked, a slot is consumed, and the calendar updates in real time.

---

### User Story 5 - Executives Dashboard Analytics & Configuration (Priority: P3)

Administrators need to view aggregated performance charts (sales, AI auto-reply accuracy, team response times) and modify system integrations (WhatsApp gateway connections, RAG settings).

**Why this priority**: Enables decision makers to monitor performance and adjust global system parameters.

**Independent Test**: An admin can load the dashboard, view line/bar charts of system performance, navigate to project settings, and edit the RAG confidence threshold.

**Acceptance Scenarios**:
1. **Given** the dashboard, **When** it loads, **Then** it presents clean, visual charts of sales, message volumes, and AI auto-reply accuracy.
2. **Given** the project settings panel, **When** the user toggles "AI Auto-Reply Enabled" and clicks Save, **Then** the configuration updates on the backend.

---

### Edge Cases

- **Offline / Flaky Network Connectivity**: When the device loses internet connection, the app must display a non-intrusive banner indicating offline status, queue outbound messages, and gracefully prevent actions requiring backend sync.
- **WebSocket Reconnection**: If the SignalR connection drops, the app must initiate exponential backoff reconnection attempts without crashing the UI.
- **Multi-Tenant Session Expiry**: If the JWT token expires, the app must attempt a background refresh using the refresh token. If both fail, the app must immediately clear user context and route to the login screen with a descriptive prompt.
- **Large Chat History Rendering**: The chat list must support virtualization or lazy-loading (infinite scroll) to prevent memory issues when loading hundreds of messages.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support user registration, login, token-based session persistence, and token auto-refresh.
- **FR-002**: The system MUST enforce project isolation by attaching the active project ID (`X-Project-Id`) to every outgoing API request.
- **FR-003**: The inbox page MUST update conversations and messages in real time using SignalR.
- **FR-004**: The inbox page MUST support manual typing, AI suggestion display, suggestion approval/rejection, customer tagging, blacklist toggling, and follow-up scheduling.
- **FR-005**: The CRM page MUST render a searchable list of customers, a detailed customer profile editor, and a visual deal pipeline boards.
- **FR-006**: The booking page MUST provide a visual calendar interface displaying active slots, and allow creating and editing group appointments.
- **FR-007**: The dashboard page MUST display visual graphs of sales data, AI accuracy, and team activities.
- **FR-008**: The settings page MUST support editing project settings, toggling AI auto-replies, setting lead score thresholds, and configuring WhatsApp gateway connections.
- **FR-009**: The user interface MUST dynamically adapt to both portrait and landscape orientations, as well as tablet form factors.

### Key Entities

- **User**: Represents the authenticated operator (Agent, Administrator).
- **Project**: Represents the isolated business tenant workspace.
- **Customer**: Represents a WhatsApp client interacting with the system, storing attributes such as name, phone, lead score, budget, interests, and tags.
- **Message**: Represents a single WhatsApp text or media exchange.
- **Deal**: Represents a sales opportunity associated with a customer, linked to a pipeline stage.
- **Appointment**: Represents a scheduled group slot on the calendar.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Agents can log in and view their primary inbox in under 2 seconds from app launch under a standard 4G connection.
- **SC-002**: Inbound WhatsApp messages are displayed in the inbox list within 300ms of being received by the gateway.
- **SC-003**: Dynamic layout remains unbroken, with 0% overflow errors on screen sizes ranging from 4-inch phones to 12.9-inch tablets.
- **SC-004**: System maintains 100% tenant isolation—messages or customer details from one Project ID are never leaked to another.

## Assumptions

- **A-001**: The backend services and APIs are fully operational and accessible over HTTPS.
- **A-002**: The backend supports SignalR connections on a mobile-accessible URL.
- **A-003**: The mobile device has access to Google Play Services / Apple APNs if push notifications are configured in the future.
