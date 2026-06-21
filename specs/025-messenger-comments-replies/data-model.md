# Data Model: Messenger & Comments Integration

**Feature**: 025-messenger-comments-replies  
**Date**: 2026-06-21

## Entity Changes

### Modified Entities

#### Conversation (existing)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| Channel | string | **NEW** | `"WhatsApp"` (default), `"Messenger"`, `"FacebookComment"` |

**Migration note**: Default existing rows to `"WhatsApp"`. Add index on `(ProjectId, Channel, Status)`.

---

#### Message (existing)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| FacebookPostId | string? | **NEW** | Post ID for comment messages (null for DMs/WhatsApp) |
| FacebookCommentId | string? | **NEW** | Comment ID (null for non-comment messages) |
| ParentCommentId | string? | **NEW** | Parent comment ID for nested replies (null for top-level) |

---

#### Customer (existing)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| FacebookPSID | string? | **NEW** | Page-Scoped User ID from Facebook. Null for WhatsApp-only customers |
| FacebookName | string? | **NEW** | Display name from Facebook profile (may differ from WhatsApp name) |

**Cross-channel linking**: When a customer with a known phone number also interacts via Messenger/Comments, both identifiers are stored on the same `Customer` record.

---

#### ProjectSettings (existing)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| MessengerAiAutoReplyEnabled | bool | **NEW** | Default: false |
| MessengerReplyDelay | int | **NEW** | Default: 5 (seconds) |
| CommentsAiAutoReplyEnabled | bool | **NEW** | Default: false |
| CommentsReplyDelay | int | **NEW** | Default: 10 (seconds) |

---

### New Entities

#### ConnectedPage (Modules/Facebook/Domain)

| Field | Type | Required | Notes |
|-------|------|----------|-------|
| Id | Guid (PK) | Yes | Auto-generated |
| ProjectId | Guid (FK) | Yes | Tenant isolation via ITenantEntity |
| FacebookPageId | string | Yes | Facebook's Page ID (from OAuth `/me/accounts`) |
| PageName | string | Yes | Display name of the Page (from OAuth) |
| PageAccessToken | string | Yes | Long-lived Page Access Token (auto-obtained via OAuth, encrypted at rest) |
| UserAccessToken | string? | No | Long-lived User Access Token (for token refresh, encrypted) |
| FacebookUserId | string? | No | ID of the Facebook user who connected the page |
| IsActive | bool | Yes | Default: true |
| TokenExpiresAt | DateTime? | No | When the long-lived token expires (~60 days) |
| CreatedAt | DateTime | Yes | Auto-set |
| UpdatedAt | DateTime | Yes | Auto-set |

**Relationships**:
- `ConnectedPage` → `Project` (many-to-one via ProjectId)
- One project can have multiple connected pages (future-proof), but initially we enforce 1 page per project

**Inherits**: `AuditableEntity`, `ITenantEntity`

---

## Event Changes

### Modified Events

#### MessageAggregatedEvent (existing)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| Channel | string | **NEW** | `"WhatsApp"`, `"Messenger"`, `"FacebookComment"` |
| ChannelMetadata | string? | **NEW** | JSON with channel-specific data (e.g., comment_id, post_id) |

#### AIReplyGeneratedEvent (existing)

| Field | Type | Change | Notes |
|-------|------|--------|-------|
| Channel | string | **NEW** | Same as above — routes reply to correct sender |
| ChannelMetadata | string? | **NEW** | JSON passed through from aggregated event |

---

## Database Indexes

```text
Conversations:   IX_Conversations_ProjectId_Channel_Status (ProjectId, Channel, Status)
Customers:       IX_Customers_ProjectId_FacebookPSID (ProjectId, FacebookPSID) WHERE FacebookPSID IS NOT NULL
ConnectedPages:  IX_ConnectedPages_ProjectId (ProjectId)
ConnectedPages:  IX_ConnectedPages_FacebookPageId (FacebookPageId) UNIQUE
Messages:        IX_Messages_FacebookCommentId (FacebookCommentId) WHERE FacebookCommentId IS NOT NULL
```

## State Transitions

### Conversation Status (unchanged)
```
Open → Pending → Resolved → Closed
         ↑                     |
         └─────────────────────┘ (Reopen on new message)
```

### ConnectedPage Lifecycle
```
Created (IsActive=true) → Deactivated (IsActive=false) → Deleted
```

## Validation Rules

1. **ConnectedPage.PageAccessToken**: Must not be empty; stored encrypted
2. **ConnectedPage.FacebookPageId**: Must be unique across all projects
3. **Conversation.Channel**: Must be one of: `"WhatsApp"`, `"Messenger"`, `"FacebookComment"`
4. **Customer.FacebookPSID**: If present, must be unique within a project scope
5. **Message.FacebookCommentId**: If present, must map to a valid comment thread
