# Feature Specification: Frontend Management Pages & WhatsApp QR Connectivity

**Feature Branch**: `009-frontend-pages-whatsapp`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "implement remaining frontend pages (Follow-ups, Campaigns, Workflows, Knowledge Base, Approvals, Reports) and WhatsApp connection panel with QR code"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - WhatsApp QR Code Connectivity & Control (Priority: P1)
As a project manager or agent, I want a secure page in settings to connect my WhatsApp number by scanning a QR code, so that the platform can send and receive real-time messages.

**Why this priority**: WhatsApp connectivity is the fundamental communications channel for the platform. Without it, real-time message ingestion, AI auto-replies, and outgoing campaigns cannot function.

**Independent Test**: Connect to the Settings page, click "Start Session", verify the QR code is generated and rendered as an image, and see status transition to "Connected" with the correct phone number upon successful scan (or when mock-connected).

**Acceptance Scenarios**:
1. **Given** a project settings page is opened, **When** no active WhatsApp session exists, **Then** show status "Disconnected" with a "Link WhatsApp" button.
2. **Given** the user clicks "Link WhatsApp", **When** the session starts initializing, **Then** show status "Initializing" and fetch and render the QR code image from the gateway.
3. **Given** the QR code is scanned, **When** connection succeeds, **Then** update status to "Connected" and display the active WhatsApp phone number.
4. **Given** an active connection, **When** the user clicks "Unlink", **Then** terminate the session and return to "Disconnected".

---

### User Story 2 - AI Proposals Approval Queue (Priority: P2)
As a customer service supervisor, I want a centralized approvals queue page to review and accept/reject message replies drafted by the Gemini AI engine, so that we can prevent automated mistakes before they reach clients.

**Why this priority**: Ensures human-in-the-loop safety for high-risk customer communications, conforming to the project's risk-based approval principles.

**Independent Test**: Navigate to the Approvals page, view pending drafts, approve a draft and verify that it changes status and is dispatched, or reject a draft and verify that it is discarded.

**Acceptance Scenarios**:
1. **Given** a supervisor is logged in, **When** they open the Approvals page, **Then** display a list of all "Pending" AI-drafted replies with details (Customer Name, Original Message, AI Drafted Answer, Risk Reason).
2. **Given** a pending approval, **When** the supervisor clicks "Approve", **Then** dispatch the message immediately and mark the item as "Approved".
3. **Given** a pending approval, **When** the supervisor clicks "Reject", **Then** discard the draft and mark the item as "Rejected".

---

### User Story 3 - Follow-ups, Campaigns & Workflows Management (Priority: P2)
As an agent or marketer, I want to manage scheduled customer follow-up actions, schedule outbound marketing campaigns to target customer segments, and view trigger-action workflows so that operational tasks are automated.

**Why this priority**: Extends the CRM core with vital execution mechanics (follow-up scheduling, bulk outbound campaigns, and custom workflow rules).

**Independent Test**: Complete a pending follow-up on the Follow-ups page, schedule a new marketing campaign on the Campaigns page, and verify the list of active workflows on the Workflows page.

**Acceptance Scenarios**:
1. **Given** the Follow-ups page, **When** loaded, **Then** list all pending follow-ups with due dates, customer info, and a "Complete" button.
2. **Given** the Campaigns page, **When** the user fills the Campaign Form (Segment, Scheduled Time, Template text), **Then** save the campaign and show it in the grid with pending dispatch count.
3. **Given** the Workflows page, **When** loaded, **Then** list trigger-action automation rules (e.g. "Trigger follow-up when deal closes") showing active/inactive status.

---

### User Story 4 - Company Knowledge Base & Reports Dashboard (Priority: P3)
As an administrator, I want to manage company documents used by the AI brain for knowledge lookup and view dashboard reports containing Daily Operations and AI metrics, so that system intelligence is monitored.

**Why this priority**: Vital for managing the AI brain's semantic memory and evaluating customer operations performance.

**Independent Test**: Upload a text document on the Knowledge page, verify it is chunked and synced, and view graphs/metrics on the Reports page.

**Acceptance Scenarios**:
1. **Given** the Knowledge Base page, **When** a user uploads a `.txt` file, **Then** parse, list the document, and show a "Sync Brain" button to reindex the search database.
2. **Given** the Reports page, **When** loaded, **Then** show metrics cards (Daily Messages, AI response rate, Average response time, Conversion rate).

---

## Edge Cases

- **QR Code Expiry**: If the WhatsApp QR code expires before the user scans it, the UI MUST automatically fetch and display a new QR code without a full page refresh.
- **Connection Loss**: If the WhatsApp Gateway drops connection, the status indicator MUST update in real-time to "Disconnected" and prompt the user to link again.
- **Empty Queues**: If there are no pending approvals or follow-ups, the pages MUST display friendly empty-state illustrations rather than blank lists.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001 (WhatsApp Connection)**: The system MUST display a WhatsApp connection panel under `/settings` showing connection state (`Disconnected`, `Initializing`, `Connected`) and the connected phone number.
- **FR-002 (QR Generation)**: The system MUST query `/api/whatsapp/session/qr?projectId=...` when the session is `Initializing` and render the raw QR string as an image using an open QR API.
- **FR-003 (Status Polling)**: The system MUST poll `/api/whatsapp/session/status?projectId=...` every 5 seconds when setting up or checking connections.
- **FR-004 (Mock Connect)**: The system MUST provide a "Mock Connect" button in Settings to simulate a successful connection for testing.
- **FR-005 (Follow-up Completion)**: The system MUST support completing scheduled follow-up tasks from `/management/follow-ups`.
- **FR-006 (Campaign Dispatch)**: The system MUST allow scheduling campaigns to segments of contacts.
- **FR-007 (Workflow List)**: The system MUST display existing trigger-action workflows.
- **FR-008 (AI Approvals Execution)**: The supervisor queue MUST allow approving/rejecting drafts via `/api/projects/{projectId}/approvals/{id}/action` endpoints.
- **FR-009 (Knowledge Base Sync)**: The system MUST support document uploads and triggering a company brain sync.
- **FR-010 (Analytics Charts)**: The system MUST display analytical summaries for daily operations and AI replies.

### Key Entities

- **WhatsAppSession**: The active baileys connection socket instance and status details (Status, Phone).
- **ApprovalRequest**: The pending AI-generated reply draft (Id, CustomerName, OriginalMessage, ProposedContent, RiskScore, Status).
- **FollowUp**: A scheduled task mapped to a customer (Id, DueDate, Status, Notes, CustomerName).
- **Campaign**: An outbound marketing schedule (Id, Name, TemplateContent, Status, TargetSegment, SendCount).

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Users can view and load all new management pages in under 1 second.
- **SC-002**: Scanning the QR code updates the connection status to "Connected" within 2 seconds of the gateway establishing connection.
- **SC-003**: AI approvals can be processed (approved or rejected) in one click.
- **SC-004**: Document uploads immediately appear in the knowledge registry list.

---

## Assumptions

- **Gateway availability**: The WhatsApp Gateway container is active and reachable by the backend.
- **Single active session**: Each project supports a maximum of one linked WhatsApp number.
- **Static mock data**: The mock data for campaigns and workflows is modeled on backend structures.
