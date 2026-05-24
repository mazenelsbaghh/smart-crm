# Feature Specification: Frontend Dashboard, Realtime & Production Hardening

**Feature Branch**: `007-frontend-production`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "Frontend Dashboard, Realtime & Production Hardening"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Realtime Agent Inbox (Priority: P1)

Support agents require a consolidated workspace to manage communications in real-time, review AI suggestions, upload media attachments, and track customer contexts.

**Why this priority**: High priority because the core value proposition of the system relies on active agent engagement and response workflows.

**Independent Test**: An agent logs into the application, opens the inbox page, receives a message from a customer, observes the UI update in real-time, reviews the suggested AI reply, edits the response, and clicks send.

**Acceptance Scenarios**:

1. **Given** the agent is logged in and viewing the inbox page, **When** a WhatsApp message is received from a customer, **Then** the conversation list is reordered with the active conversation at the top, showing a preview of the message and incrementing the unread indicator.
2. **Given** the agent is viewing a conversation, **When** they type a reply or click on an AI-suggested response, **Then** they can send the response, which is immediately appended to the conversation history and sent to the customer.
3. **Given** the agent is in a chat, **When** they drag and drop or attach a media file (image/document), **Then** the file is uploaded, a preview is displayed, and it is sent to the customer.

---

### User Story 2 - Dashboard KPI & CRM Management (Priority: P2)

Administrators and supervisors need to see a high-level operational overview and manage the customer sales pipeline.

**Why this priority**: Crucial for management to monitor performance, manage agent assignments, and progress leads.

**Independent Test**: A supervisor logs in, views the dashboard metrics cards, clicks through to the CRM Kanban board, and drags a customer card from "Contacted" to "Qualified".

**Acceptance Scenarios**:

1. **Given** the supervisor is logged in, **When** they open the dashboard, **Then** they see updated counts for new customers, open conversations, pending follow-ups, and active campaigns.
2. **Given** the user is viewing the CRM pipeline board, **When** they drag a customer card between stages, **Then** the pipeline stage is updated in the database, and the card snaps to the new column.
3. **Given** the user is viewing a customer profile, **When** they update lead score, tags, or add notes, **Then** the changes are saved and immediately reflected in the customer view.

---

### User Story 3 - Production Hardening & Deployment Automation (Priority: P3)

The DevOps engineer needs to run the application in a secure production environment and execute automated backups.

**Why this priority**: Essential for system stability, security, data durability, and disaster recovery.

**Independent Test**: An external client makes rapid requests to verify rate limits, and the deployment script is executed to run backup and restore routines.

**Acceptance Scenarios**:

1. **Given** the backend services are running in production mode, **When** a client exceeds the defined request rate limit, **Then** the system returns an HTTP 429 Too Many Requests response with a `Retry-After` header.
2. **Given** the backup script is executed on the server, **When** the operation completes, **Then** a compressed package containing the PostgreSQL database dump, Redis snapshot, and MinIO storage files is created and saved to the configured backup location.
3. **Given** the restore script is executed with a valid backup package, **When** the operation completes, **Then** the database and file storage are restored to the exact state at the time of the backup.

### Edge Cases

- **Realtime Connection Disconnect**: If the agent's websocket connection drops, the UI must display a reconnection warning banner and attempt to reconnect in the background. Once reconnected, it must fetch any missed messages.
- **Concurrent Agent Assignment**: If two agents open the same conversation, a warning or lock indicator should display to show who is currently editing/replying.
- **Malformed Uploads**: If an agent attempts to upload an unsupported file type or a file exceeding size limits, the system must show a validation error message and block the upload.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a secure dashboard UI showing KPI counts for new customers, open conversations, pending follow-ups, and active campaigns.
- **FR-002**: The system MUST support real-time message updates in the inbox without page refresh.
- **FR-003**: The system MUST display AI-generated reply suggestions for agents within the conversation panel.
- **FR-004**: The system MUST provide a Kanban-style pipeline management interface to track and transition customer leads through defined pipeline stages.
- **FR-005**: The system MUST restrict dashboard and management access to authenticated users based on their role claims.
- **FR-006**: The system MUST automatically enforce request rate limits to protect public and authenticated endpoints.
- **FR-007**: The system MUST provide automated script-based utilities for system backup and recovery.
- **FR-008**: The system MUST configure secure CORS policies to restrict API access to trusted client origins.

### Key Entities *(include if feature involves data)*

- **FrontendSession**: Represents an active user session, storing auth and refresh tokens.
- **PipelineStage**: Represents the CRM stages (New, Contacted, Qualified, Proposal, Negotiation, Won, Lost) that a customer can transition through.
- **BackupArchive**: Represents a system snapshot package containing database, cache, and object storage backups.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Support agents see incoming WhatsApp messages in the inbox UI in under 500ms of receipt by the backend gateway.
- **SC-002**: Page navigation transitions load in under 1 second.
- **SC-003**: System backup executes completely in under 5 minutes for a standard production load.
- **SC-004**: Automated restore returns the system to a fully functional state in under 10 minutes.
- **SC-005**: The UI conforms to premium design rules, including harmonious colors, fluid animations, responsive behavior (mobile/tablet/desktop), and zero layout shifts.

## Assumptions

- **A-001**: Users have a modern web browser supporting WebSockets and modern CSS features.
- **A-002**: Nginx will act as the reverse proxy for SSL termination and static file routing.
- **A-003**: The client application will be deployed as a Docker container side-by-side with backend services.
