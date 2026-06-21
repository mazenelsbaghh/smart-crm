# Research: Messenger & Comments Integration

**Feature**: 025-messenger-comments-replies  
**Date**: 2026-06-21

## Research Tasks & Findings

### 1. Facebook Graph API — Messenger DM Integration

**Decision**: Use Facebook Graph API v20.0 Send API via `POST /{page-id}/messages`

**Rationale**: The Send API is the official and only way to send messages from a Facebook Page to users via Messenger. We use the Page Access Token to authenticate.

**Key endpoints**:
- **Receive messages**: Webhook subscription on `messages` field → POST notifications to our server
- **Send reply**: `POST /v20.0/{page-id}/messages` with `{ recipient: { id: PSID }, message: { text: "..." } }`
- **24-hour window**: Facebook restricts sending messages to users who haven't messaged in the last 24 hours (standard messaging). After 24h, only message tags (e.g., `CONFIRMED_EVENT_UPDATE`) are allowed.

**Permissions required**: `pages_messaging`, `pages_manage_metadata`, `pages_show_list`

**Alternatives considered**:
- WhatsApp Cloud API for Messenger — Not applicable (different channel)
- Third-party services (ManyChat API) — Rejected: adds dependency, cost, and less control

---

### 2. Facebook Graph API — Comments Integration

**Decision**: Subscribe to `feed` webhook field to receive comment events

**Rationale**: Facebook does not have a dedicated "comments" webhook subscription. Comments are delivered as part of the `feed` subscription, where the `item` field equals `"comment"`.

**Key endpoints**:
- **Receive comments**: Webhook subscription on `feed` field → filter events where `item === "comment"`
- **Reply to comment (public)**: `POST /v20.0/{comment-id}/comments` with `{ message: "..." }`
- **React to comment**: `POST /v20.0/{comment-id}/reactions` with `{ type: "LIKE" }`
- **Send private DM from comment**: `POST /v20.0/{page-id}/messages` with `{ recipient: { comment_id: "{comment-id}" }, message: { text: "..." } }`

**Permissions required**: `pages_manage_engagement`, `pages_read_engagement`, `pages_messaging`

**Alternatives considered**:
- Polling comments via `GET /{post-id}/comments` — Rejected: Not real-time, wastes API calls
- Instagram Graph API — Out of scope for this feature

---

### 3. Webhook Verification & Security

**Decision**: Implement Facebook webhook verification endpoint with hub.verify_token challenge-response + X-Hub-Signature-256 payload validation

**Rationale**: Facebook requires:
1. A GET endpoint that responds to verification challenge during setup
2. POST endpoint signature validation using HMAC-SHA256 with the App Secret

**Implementation**: Single controller with GET (verify) and POST (receive events) endpoints

---

### 4. Channel-Aware Architecture Pattern

**Decision**: Add a `Channel` enum property to the existing `Conversation` entity instead of creating separate tables per channel

**Rationale**: 
- The existing `Conversation` + `Message` + `Customer` entities already represent the inbox model well
- Adding a `Channel` discriminator field (`WhatsApp`, `Messenger`, `FacebookComment`) avoids duplicating the entire conversation/message schema
- Queries can easily filter by channel to power the independent inbox pages
- Follows the DRY principle and keeps the existing codebase intact

**Alternatives considered**:
- Separate tables per channel (MessengerConversation, CommentThread) — Rejected: massive duplication, breaks shared components
- Polymorphic inheritance with EF Core TPH — Rejected: adds unnecessary complexity for a simple discriminator

---

### 5. Customer Identity — PSID vs Phone Number

**Decision**: Add a nullable `FacebookPSID` field to the `Customer` entity and use it as the identifier for Messenger/Comment channels

**Rationale**:
- WhatsApp customers are identified by phone number
- Messenger/Comment customers are identified by Facebook Page-Scoped User ID (PSID)
- A single customer may have both (cross-channel linking via optional matching)
- PhoneNumber can remain the primary identifier for WhatsApp; PSID for Facebook channels

---

### 6. ConnectedPage Entity

**Decision**: Create a new `ConnectedPage` entity in the existing `Integrations` module (or extend `ProjectIntegration`)

**Rationale**: 
- Each project can connect one or more Facebook Pages
- The `ConnectedPage` stores: PageId, PageName, PageAccessToken (encrypted), AppSecret, VerifyToken
- This is separate from the generic `ProjectIntegration` because it requires Facebook-specific fields (PageId, PSID scope, webhook verification)
- However, to stay aligned with the modular monolith pattern, we can create it inside a new `Facebook` module

**Decision**: Create a new `Modules/Facebook` module for clean domain separation

---

### 7. AI Auto-Reply — Per-Channel Settings

**Decision**: Add per-channel AI settings to `ProjectSettings` entity

**New fields**:
- `MessengerAiAutoReplyEnabled` (bool, default false)
- `MessengerReplyDelay` (int, default 5)
- `CommentsAiAutoReplyEnabled` (bool, default false)
- `CommentsReplyDelay` (int, default 10)

**Rationale**: The user explicitly requested separate AI auto-reply configurations per channel. The existing `AiAutoReplyEnabled` and `ReplyDelay` remain for WhatsApp only.

---

### 8. Event-Driven Message Flow

**Decision**: Follow the existing pattern: Webhook → Aggregator → MessageAggregatedEvent → AIReplyWorker → AIReplyGeneratedEvent → Channel-specific ReplySender

**New events**:
- `MessengerMessageAggregatedEvent` — Carries channel context
- `FacebookCommentReceivedEvent` — Carries comment-specific metadata
- `MessengerReplyGeneratedEvent` — Triggers Facebook Send API
- `CommentReplyGeneratedEvent` — Triggers public comment + DM + reaction

**Rationale**: Reuses the proven event-driven architecture. The AIReplyWorker already generates responses; we just need channel-aware routing and new ReplySenders.

**Key insight**: Instead of creating separate aggregated events, we can **extend MessageAggregatedEvent with a Channel field** so the existing AIReplyWorker can handle all channels. Then AIReplyGeneratedEvent also gets a Channel field, and we add a new `FacebookReplySender` that handles Messenger + Comment replies.

---

### 9. Frontend Architecture — Independent Pages

**Decision**: Create three independent Next.js pages under `(dashboard)`:
- `/inbox` → WhatsApp (existing, unchanged)
- `/inbox/messenger` → Messenger DM conversations
- `/inbox/comments` → Facebook Comment threads

**Rationale**: User explicitly requested independent pages, not tabs. Each page will reuse the core inbox component pattern but with channel-specific filtering and UI differences (e.g., comment threads show post context, reactions).

**Implementation**: Create a shared `BaseInbox` component and channel-specific wrappers.

---

### 10. Comment Thread Display

**Decision**: Comments will be grouped by Post. Each post becomes a "conversation" with comments as "messages". The reply UI shows three action buttons: Public Reply, Private DM, React.

**Rationale**: This matches the user's requirement of replying via public comment + private DM + reaction simultaneously.

---

### 11. Facebook OAuth Login Flow (Page Connection UX)

**Decision**: Implement a "Login with Facebook" OAuth 2.0 flow so users can connect their Pages with one click instead of manually entering tokens.

**Rationale**: The user explicitly requested an easy, seamless connection experience. Manual token entry is error-prone and requires technical knowledge. The OAuth flow:
1. User clicks "ربط صفحة فيسبوك" button in Settings
2. Frontend opens Facebook Login Dialog popup/redirect with required permissions
3. User logs in and grants permissions
4. Facebook redirects back with an authorization `code`
5. Backend exchanges `code` for a User Access Token (server-side, secure)
6. Backend calls `GET /me/accounts` to list all Pages the user admins
7. User selects which Page to connect (if multiple)
8. Backend exchanges the short-lived User Token for a long-lived one (60 days)
9. Backend extracts the Page Access Token and stores it as a `ConnectedPage`
10. Backend subscribes the Page to the app via `POST /{page-id}/subscribed_apps`

**OAuth URL**:
```
https://www.facebook.com/v20.0/dialog/oauth?
  client_id={FACEBOOK_APP_ID}
  &redirect_uri={BACKEND_URL}/api/facebook/oauth/callback
  &scope=pages_show_list,pages_read_engagement,pages_manage_engagement,pages_messaging,pages_manage_metadata
  &response_type=code
  &state={projectId}:{csrf_token}
```

**Permissions requested**:
- `pages_show_list` — List user's Pages
- `pages_read_engagement` — Read comments
- `pages_manage_engagement` — Reply to comments, react
- `pages_messaging` — Send/receive Messenger DMs
- `pages_manage_metadata` — Subscribe to webhook fields

**Security**:
- `state` param carries projectId + CSRF token to prevent CSRF attacks
- Token exchange happens server-side only (App Secret never exposed to frontend)
- Page Access Token stored encrypted in PostgreSQL

**Alternatives considered**:
- Manual token entry via UI form — Rejected: user explicitly wanted easy login flow
- Facebook Login for Business (business config) — Overkill for current scale, may add later

