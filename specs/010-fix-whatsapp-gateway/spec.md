# Feature Specification: Fix WhatsApp Gateway Message Sending and Receiving

**Feature Branch**: `010-fix-whatsapp-gateway`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "fix whatsapp-gateway message sending and receiving"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Sending Messages from Agent to Customer (Priority: P1)

An agent (or AI auto-reply system) can send a text message to a customer's WhatsApp phone number through the gateway, and the customer receives it.

**Why this priority**: Core business capability. Without sending, the system cannot respond to customers.

**Independent Test**: Can be tested by posting to the gateway's `/api/whatsapp/send` endpoint for a connected session and verifying that the message is sent successfully.

**Acceptance Scenarios**:
1. **Given** a connected WhatsApp session for Project A, **When** a request is sent to `POST /api/whatsapp/send` with a valid recipient number (e.g. "+1234567890" or "1234567890") and message text, **Then** the gateway sanitizes the phone number to the correct JID format `1234567890@s.whatsapp.net`, calls Baileys `sock.sendMessage`, and returns a status of "Sent" and the message ID.
2. **Given** a WhatsApp session that is not connected, **When** a request is sent to `POST /api/whatsapp/send`, **Then** the gateway returns a 500 error indicating the session is not active.

---

### User Story 2 - Receiving Messages from Customer (Priority: P1)

When a customer sends a message to the connected WhatsApp number, the gateway detects it and forwards the message data (text/media, sender, timestamp) to the backend webhook.

**Why this priority**: Necessary for receiving customer queries and starting conversation threads or AI auto-replies.

**Independent Test**: Can be tested by simulating/receiving an incoming WhatsApp message via the Baileys socket and verifying that the gateway forwards the request to `http://backend:5000/api/webhooks/whatsapp/message`.

**Acceptance Scenarios**:
1. **Given** a running WhatsApp gateway connected to a phone number, **When** a message is received from a customer, **Then** the gateway extracts the sender's phone number, message text/type, and timestamp, and makes a POST request to the backend webhook.

---

### User Story 3 - Session Auto-Restore on Startup (Priority: P2)

When the WhatsApp gateway is started or restarted, it automatically restores any previously logged-in sessions by reading saved credentials, rather than forcing the user to re-scan the QR code.

**Why this priority**: Essential for system reliability. If the gateway container restarts, existing sessions must not be lost.

**Independent Test**: Can be verified by running the gateway, connecting a session, restarting the gateway, and confirming that the session status is automatically restored to "Connected" or "Initializing" without requiring a new QR scan.

**Acceptance Scenarios**:
1. **Given** a previously authenticated session in the `/app/sessions` directory, **When** the gateway server starts up, **Then** it iterates through all folders in the sessions directory, reads the saved credentials, and automatically initializes the connection socket.

---

## Edge Cases

- **Special Characters/Formatting in Phone Number**: The recipient number might contain spaces, hyphens, or a leading `+`. The system must sanitize these out before appending `@s.whatsapp.net`.
- **Media Messages**: Incoming messages might not be plain text (e.g. image, voice notes, stickers). The gateway must identify the type and provide a fallback text or notify the backend of the correct media type.
- **Connection Outage**: When the Baileys connection drops temporarily, the gateway must attempt reconnection automatically.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The gateway MUST sanitize the recipient's phone number to remove any non-numeric characters (like `+`, spaces, hyphens, etc.) and append `@s.whatsapp.net` to build a valid JID.
- **FR-002**: The gateway MUST check if the session is connected and throws a descriptive error if the socket is not initialized or the status is not 'Connected'.
- **FR-003**: The gateway MUST handle the socket's `creds.update` event and call the `saveCreds` function to persist credentials.
- **FR-004**: The gateway MUST support incoming message extraction from all common message structures (e.g., `conversation`, `extendedTextMessage`, `imageMessage`, `audioMessage`, `documentMessage`, `videoMessage`, etc.).
- **FR-005**: The gateway MUST catch and log any connection errors when calling `sock.sendMessage` and return appropriate JSON error payloads to the caller.

### Key Entities *(include if feature involves data)*

- **Session**: State of a Baileys socket connection associated with a specific `projectId`.
- **Message**: Outgoing or incoming text or media message payload.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of outgoing messages with valid phone numbers (even if they contain formatting like `+` or spaces) are successfully parsed and sent via Baileys.
- **SC-002**: 100% of incoming text and media message types are properly classified and forwarded to the backend webhook.
- **SC-003**: Saved sessions are successfully restored on gateway startup in under 5 seconds per session.

## Assumptions

- We assume the backend webhook (`/api/webhooks/whatsapp/message`) is available and handles the forwarded payload.
- We assume the sessions are stored in `/app/sessions` which is a mounted persistent volume.
