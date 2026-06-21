# Implementation Plan: Messenger & Comments Integration

**Branch**: `025-messenger-comments-replies` | **Date**: 2026-06-21 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/025-messenger-comments-replies/spec.md)

**Input**: Feature specification from `/specs/025-messenger-comments-replies/spec.md`

## Summary

Add Facebook Messenger DM and Facebook Comment reply channels to the CRM alongside existing WhatsApp. Each channel gets a dedicated independent inbox page (`/inbox/messenger` and `/inbox/comments`). Comment replies trigger three simultaneous actions: public comment + private DM + reaction. AI Auto-Reply settings are separated per channel (WhatsApp, Messenger, Comments).

## Technical Context

**Language/Version**: C# / .NET 8 (ASP.NET Core), TypeScript / Next.js 14  
**Primary Dependencies**: Entity Framework Core, RabbitMQ (MassTransit/custom IEventBus), SignalR, Redis, Axios, Facebook Graph API v20.0  
**Storage**: PostgreSQL (primary with pgvector), Redis (caching, aggregation), Elasticsearch (search)  
**Testing**: Manual verification via Facebook test accounts (no unit test framework currently in place)  
**Target Platform**: Ubuntu server (Docker Compose), Web browser  
**Project Type**: Web application (modular monolith backend + Next.js frontend)  
**Performance Goals**: Messages/comments synced within 5s, replies delivered within 2s  
**Constraints**: Facebook 24h messaging window for Messenger, rate limits on Graph API

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Modular Monolith | ✅ PASS | New `Modules/Facebook` module with clean domain boundaries. Inter-module communication via RabbitMQ events. |
| II. Multi-Tenant Isolation | ✅ PASS | `ConnectedPage` and all queries are scoped to `ProjectId`. `ITenantEntity` interface applied. |
| III. Gemini 3.5 Flash Unified AI | ✅ PASS | AI auto-reply for Messenger/Comments uses the existing `AIMarketingBrain` + Gemini pipeline. No new AI services needed. |
| IV. Human-Like Messaging | ✅ PASS | Messenger reply sender uses the existing `IHumanMessagingEngine` for typing delays. Comment replies are posted without delays (public comments don't need simulation). |
| V. Risk-Based Approval | ✅ PASS | Comment replies (public-facing) are agent-initiated. AI auto-replies use the same risk framework. No new high-risk actions introduced. |

**Post-Phase 1 Re-check**: All gates still pass after design. The `Channel` field extension is backward-compatible with existing WhatsApp data.

## Project Structure

### Documentation (this feature)

```text
specs/025-messenger-comments-replies/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── api-contracts.md
└── tasks.md             # Phase 2 output (created by /speckit-tasks)
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── Facebook/                    # NEW MODULE
│   │   │   ├── Domain/
│   │   │   │   └── ConnectedPage.cs     # New entity
│   │   │   ├── API/
│   │   │   │   ├── FacebookWebhookController.cs   # Webhook receive/verify
│   │   │   │   ├── FacebookPageController.cs      # Get/disconnect connected pages
│   │   │   │   └── FacebookOAuthController.cs      # NEW: OAuth login + callback + page selection
│   │   │   ├── Services/
│   │   │   │   ├── IFacebookGraphService.cs       # Graph API abstraction
│   │   │   │   ├── FacebookGraphService.cs        # Send msg, reply comment, react
│   │   │   │   ├── IFacebookOAuthService.cs       # NEW: OAuth token exchange service
│   │   │   │   └── FacebookOAuthService.cs        # NEW: Exchange code→token, /me/accounts
│   │   │   └── Workers/
│   │   │       └── FacebookReplySender.cs         # Handles AIReplyGeneratedEvent for FB channels
│   │   ├── Conversations/
│   │   │   ├── Domain/
│   │   │   │   ├── Conversation.cs      # MODIFIED: +Channel field
│   │   │   │   ├── Message.cs           # MODIFIED: +FacebookPostId, +FacebookCommentId, +ParentCommentId
│   │   │   │   └── Customer.cs          # MODIFIED: +FacebookPSID, +FacebookName
│   │   │   └── API/
│   │   │       ├── ConversationController.cs  # MODIFIED: +channel filter
│   │   │       └── WebhookController.cs       # UNCHANGED (WhatsApp only)
│   │   ├── Projects/
│   │   │   ├── Domain/
│   │   │   │   └── ProjectSettings.cs   # MODIFIED: +Messenger/Comments AI settings
│   │   │   └── API/
│   │   │       └── ProjectController.cs # MODIFIED: expose new settings fields
│   │   ├── AI/
│   │   │   └── Workers/
│   │   │       └── AIReplyWorker.cs     # MODIFIED: channel-aware routing
│   │   └── WhatsApp/
│   │       └── Workers/
│   │           └── ReplySender.cs       # MODIFIED: filter to WhatsApp channel only
│   └── Shared/
│       ├── Events/
│       │   ├── MessageAggregatedEvent.cs   # MODIFIED: +Channel, +ChannelMetadata
│       │   └── AIReplyGeneratedEvent.cs    # MODIFIED: +Channel, +ChannelMetadata
│       └── Infrastructure/
│           └── AppDbContext.cs             # MODIFIED: +DbSet<ConnectedPage>

frontend/
├── src/
│   ├── app/(dashboard)/
│   │   ├── inbox/
│   │   │   ├── page.tsx                # UNCHANGED (WhatsApp)
│   │   │   ├── messenger/
│   │   │   │   └── page.tsx            # NEW: Messenger inbox page
│   │   │   └── comments/
│   │   │       └── page.tsx            # NEW: Comments inbox page
│   │   └── settings/
│   │       └── page.tsx                # Uses Settings.tsx (modified)
│   ├── packages/
│   │   ├── inbox/
│   │   │   ├── Inbox.tsx               # UNCHANGED (WhatsApp inbox)
│   │   │   ├── MessengerInbox.tsx      # NEW: Messenger inbox component
│   │   │   ├── CommentsInbox.tsx       # NEW: Comments inbox component
│   │   │   ├── shared/                 # NEW: Shared inbox components
│   │   │   │   ├── ConversationList.tsx
│   │   │   │   ├── ChatPanel.tsx
│   │   │   │   └── CustomerSidebar.tsx
│   │   │   ├── inbox.module.css        # MODIFIED: shared + channel-specific styles
│   │   │   └── messenger.module.css    # NEW
│   │   │   └── comments.module.css     # NEW
│   │   └── settings/
│   │       ├── Settings.tsx            # MODIFIED: add Messenger/Comments AI toggle sections
│   │       └── FacebookConnect.tsx     # NEW: "ربط صفحة فيسبوك" OAuth button + page selector
│   ├── components/layout/
│   │   └── Sidebar.tsx                 # MODIFIED: add nav items for Messenger + Comments
│   └── types/
│       └── chat.ts                     # MODIFIED: add channel field to types

backend/Program.cs                      # MODIFIED: register Facebook module DI + event subscriptions
```

**Structure Decision**: Web application with modular monolith backend (new `Facebook` module) and Next.js frontend (new inbox pages under `/inbox/messenger` and `/inbox/comments`).

## Complexity Tracking

No constitution violations. All design decisions align with existing patterns.

| Decision | Why This Way | Alternative Rejected Because |
|----------|-------------|------------------------------|
| Single `Channel` field on Conversation | Avoids table duplication, simple query filtering | Separate tables per channel would duplicate schema and break shared components |
| New Facebook module instead of extending WhatsApp | Clean domain separation per constitution principle I | Putting FB code in WhatsApp module violates module boundaries |
| Extending existing events vs new events | Reuses AIReplyWorker pipeline, less code | New event types would require duplicating the entire AI pipeline |
