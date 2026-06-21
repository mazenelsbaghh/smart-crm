# Feature Specification: Messenger Comments and Replies

**Feature Branch**: `025-messenger-comments-replies`

**Created**: 2026-06-21

**Status**: Draft

**Input**: User description: "عايز اضيف الرد علي الماسنجر و يكون ليه تابه مختلفه و الرد عليه يبقي مظبطو وعلي الكومنتات كمان"

## Clarifications

### Session 2026-06-21

- Q: How should the system reply to Facebook comments? → A: Reply by writing a public comment AND sending a private Messenger DM at the same time, and also apply a reaction (like) to the customer's comment.
- Q: Inbox tabs layout → A: Completely independent pages for each channel (a page for WhatsApp, an independent page for Messenger, and a page for Facebook comments).
- Q: AI Auto-Reply Settings integration → A: Separate AI Auto-Reply configurations (toggles and delays) for each of the three channels (WhatsApp, Messenger DMs, and Comments) to allow custom rules for each.
- Note: Integration is for Facebook **Pages** only (not personal accounts). Messenger DMs, comments, and reactions all go through the Page's API.
- Q: How should users connect their Facebook Page? → A: Via a "Login with Facebook" OAuth flow. The user clicks a button, logs in, grants permissions, selects a Page, and the system automatically gets the Access Token and subscribes to webhooks. No manual token entry.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Viewing and Replying to Messenger DMs (Priority: P1)

As a CRM agent, I want to see incoming Messenger direct messages in a dedicated Messenger Inbox page (e.g. `/inbox/messenger`) and be able to reply to them manually.

**Why this priority**: Core channel expansion to support Messenger alongside WhatsApp.

**Independent Test**: Can be tested by sending a message to the connected Facebook Page Messenger, verifying it appears in the Messenger Inbox page, and replying to it from the CRM.

**Acceptance Scenarios**:

1. **Given** a connected Facebook Page, **When** a customer sends a message on Messenger, **Then** it appears in the Messenger Inbox page conversation list.
2. **Given** a Messenger conversation is open, **When** the agent types a reply and clicks send, **Then** the message is sent to the customer on Messenger and appears in the chat log.

---

### User Story 2 - Managing Facebook Comments in Inbox (Priority: P2)

As a CRM agent, I want to see incoming comments on Facebook Page posts in a dedicated Facebook Comments Inbox page (e.g. `/inbox/comments`) and be able to reply to them.

**Why this priority**: Expand customer interaction to public comments, ensuring no lead is missed.

**Independent Test**: Can be tested by posting a comment on a page post, verifying it appears in the Comments Inbox page, and replying from the CRM.

**Acceptance Scenarios**:

1. **Given** a post on the connected page, **When** a customer comments on the post, **Then** a conversation thread is created/updated in the Comments Inbox page, and the system automatically likes/reacts to the comment.
2. **Given** a comment conversation is open, **When** the agent replies, **Then** the reply is posted as a public comment on the post AND a private DM is sent to the customer on Messenger.

---

### User Story 3 - AI Auto-Replies for Messenger and Comments (Priority: P3)

As a page owner, I want the AI auto-reply engine to automatically respond to Messenger DMs and comments using the CRM knowledge base.

**Why this priority**: Automation saves time and captures leads 24/7.

**Independent Test**: Can be tested by sending a DM or comment and verifying that the AI responds automatically with relevant info from the knowledge base.

**Acceptance Scenarios**:

1. **Given** AI auto-reply is enabled for Messenger, **When** a customer sends a DM, **Then** the AI generates and sends a reply after the configured delay.
2. **Given** AI auto-reply is enabled for Comments, **When** a customer comments, **Then** the AI generates and posts a reply.

### Edge Cases

- **Post Deletion**: What happens when a user comments on a post, and then the post is deleted? The system should mark the conversation as read-only or archive it.
- **Messenger 24-hour window**: Facebook limits replies to Messenger messages to 24 hours after the user's last message. If the agent tries to reply after 24 hours, the system should display a clear warning/error.
- **Comment editing**: If a customer edits their comment, does the system update the message content in the chat log? (Assumption: Yes, it updates the existing message or appends an edited flag).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST support connecting a Facebook Page to retrieve Messenger messages and comments.
- **FR-002**: Inbox UI MUST display completely independent pages for each channel (e.g., `/inbox` for WhatsApp, `/inbox/messenger` for Messenger DMs, and `/inbox/comments` for Facebook Comments).
- **FR-003**: System MUST support separate AI Auto-Reply configurations (enable/disable toggle and response delay) for each channel independently: WhatsApp, Messenger DMs, and Facebook Comments.
- **FR-004**: Messenger conversations MUST enforce the Facebook 24-hour reply window limit, showing a warning to agents.
- **FR-005**: The system MUST store comments as message logs, linked to the post ID and customer profile, allowing full history tracking in the CRM.

### Key Entities *(include if feature involves data)*

- **ConnectedPage**: Represents the linked Facebook Page (Page ID, Access Token, Name).
- **Conversation**: Existing entity, extended/adapted to support Channel type (`WhatsApp`, `Messenger`, `FacebookComment`).
- **Message**: Existing entity, extended/adapted to store comment metadata (Post ID, Comment ID, Parent Comment ID if nested).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Messenger messages and comments are synced to the CRM within 5 seconds of receipt.
- **SC-002**: Agent replies to Messenger and comments are delivered back to Facebook in under 2 seconds.
- **SC-003**: AI auto-replies to Messenger messages are triggered and sent within the configured delay window (default 10-30 seconds).

## Assumptions

- **Page-only integration**: The system connects to Facebook **Pages** only (not personal accounts). All Messenger DMs, comments, and reactions are handled through the Page's Graph API and webhook subscriptions.
- We will integrate with Facebook/Meta Graph API endpoints for Messenger and Comments.
- The existing Customer entity will be linked via Facebook Page-Scoped User ID (PSID) as the primary identifier instead of a phone number when the channel is Messenger/Comments.
- A Facebook webhook handler will be created on the backend to receive real-time Messenger messages, comment events, and delivery status updates.
