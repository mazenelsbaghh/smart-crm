# API Contracts: Messenger & Comments Integration

**Feature**: 025-messenger-comments-replies  
**Date**: 2026-06-21

## Backend API Endpoints

### Facebook Module

#### 1. Facebook Webhook Verification
```
GET /api/webhooks/facebook
Query: hub.mode, hub.verify_token, hub.challenge
Response: hub.challenge (plain text) or 403
```

#### 2. Facebook Webhook Events
```
POST /api/webhooks/facebook
Headers: X-Hub-Signature-256
Body: Facebook webhook payload (messages / feed events)
Response: 200 OK
```

#### 3. Initiate Facebook OAuth Login
```
GET /api/facebook/oauth/login?projectId={projectId}
Auth: Bearer JWT
Response: 302 Redirect → Facebook Login Dialog
  (with scope=pages_show_list,pages_read_engagement,pages_manage_engagement,pages_messaging,pages_manage_metadata)
  (state param = projectId:csrfToken for security)
```
Frontend triggers this by opening a popup/redirect to this URL.

#### 4. Facebook OAuth Callback
```
GET /api/facebook/oauth/callback
Query: code, state
(No Auth - Facebook redirects here directly)
Action:
  1. Validate CSRF token from state
  2. Exchange code → User Access Token
  3. Exchange short-lived → long-lived User Token
  4. Call GET /me/accounts → list Pages
  5. Return page list to frontend for selection
Response: Redirect to frontend with page list (or HTML page for popup)
```

#### 5. Confirm Page Selection (after OAuth)
```
POST /api/projects/{projectId}/facebook/pages/confirm
Auth: Bearer JWT
Body:
{
  "facebookPageId": "string",
  "pageName": "string",
  "pageAccessToken": "string",
  "userAccessToken": "string",
  "facebookUserId": "string"
}
(All fields auto-populated from OAuth - NOT entered manually)
Response: 201 Created
{
  "id": "guid",
  "facebookPageId": "string",
  "pageName": "string",
  "isActive": true,
  "createdAt": "datetime"
}
Action: Also calls POST /{page-id}/subscribed_apps to subscribe webhooks
```

#### 6. Get Connected Pages
```
GET /api/projects/{projectId}/facebook/pages
Auth: Bearer JWT
Response: 200 OK
[
  {
    "id": "guid",
    "facebookPageId": "string",
    "pageName": "string",
    "isActive": true,
    "tokenExpiresAt": "datetime",
    "createdAt": "datetime"
  }
]
```

#### 5. Disconnect Facebook Page
```
DELETE /api/projects/{projectId}/facebook/pages/{pageId}
Auth: Bearer JWT
Response: 204 No Content
```

---

### Conversation API Extensions

#### 6. List Conversations by Channel
```
GET /api/projects/{projectId}/conversations
Auth: Bearer JWT
Query: 
  status: string (All | Open | Pending | Resolved | Closed)
  channel: string (WhatsApp | Messenger | FacebookComment)  ← NEW
  search: string
  before: datetime
  limit: int (default 20)
Response: 200 OK (same shape as existing, with channel field added)
```

#### 7. Send Messenger Reply (Manual)
```
POST /api/projects/{projectId}/conversations/{conversationId}/reply
Auth: Bearer JWT
Body:
{
  "content": "string",
  "channel": "Messenger"   ← NEW field
}
Response: 200 OK
```

#### 8. Reply to Comment (Composite Action)
```
POST /api/projects/{projectId}/conversations/{conversationId}/comment-reply
Auth: Bearer JWT
Body:
{
  "publicComment": "string",
  "privateDM": "string",
  "reaction": "LIKE" | "LOVE" | "WOW" | "HAHA" | null
}
Response: 200 OK
{
  "publicCommentSent": true,
  "privateDMSent": true,
  "reactionApplied": true
}
```

---

### Settings API Extensions

#### 9. Update Project Settings (Extended)
```
PUT /api/projects/{projectId}/settings
Auth: Bearer JWT
Body: (add new fields to existing payload)
{
  ...existing fields...,
  "messengerAiAutoReplyEnabled": boolean,   ← NEW
  "messengerReplyDelay": int,               ← NEW
  "commentsAiAutoReplyEnabled": boolean,    ← NEW
  "commentsReplyDelay": int                 ← NEW
}
```

---

## Webhook Payloads (Inbound from Facebook)

### Messenger Message Received
```json
{
  "object": "page",
  "entry": [{
    "id": "PAGE_ID",
    "time": 1234567890,
    "messaging": [{
      "sender": { "id": "PSID" },
      "recipient": { "id": "PAGE_ID" },
      "timestamp": 1234567890,
      "message": {
        "mid": "msg_id",
        "text": "Hello"
      }
    }]
  }]
}
```

### Comment Received (via feed subscription)
```json
{
  "object": "page",
  "entry": [{
    "id": "PAGE_ID",
    "time": 1234567890,
    "changes": [{
      "field": "feed",
      "value": {
        "item": "comment",
        "comment_id": "COMMENT_ID",
        "parent_id": "POST_ID",
        "from": { "id": "USER_PSID", "name": "User Name" },
        "message": "Nice post!",
        "post_id": "POST_ID",
        "verb": "add",
        "created_time": 1234567890
      }
    }]
  }]
}
```

---

## SignalR Events (New)

### ReceiveMessage (Extended)
Existing event — now includes channel context:
```json
{
  "id": "guid",
  "conversationId": "guid",
  "senderType": "Customer",
  "content": "string",
  "createdAt": "datetime",
  "status": "Delivered",
  "channel": "Messenger | FacebookComment",    ← NEW
  "facebookPostId": "string | null",           ← NEW
  "facebookCommentId": "string | null"         ← NEW
}
```

## Frontend Routes (New)

| Route | Component | Description |
|-------|-----------|-------------|
| `/inbox` | `Inbox.tsx` | WhatsApp conversations (existing, unchanged) |
| `/inbox/messenger` | `MessengerInbox.tsx` | Messenger DM conversations |
| `/inbox/comments` | `CommentsInbox.tsx` | Facebook Comment threads |
