# Tasks: Messenger & Comments Integration

**Input**: Design documents from `/specs/025-messenger-comments-replies/`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/api-contracts.md

**Tests**: No automated test framework in place; verification via manual testing and build checks.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Arabic Clarification (`speckit-clarify`)
- [x] Phase 3: Technical Planning (`speckit-plan`)
- [x] Phase 4: Detailed Task Breakdown (`speckit-tasks`)

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Database migration, shared entity changes, new module scaffolding, event extensions

- [ ] T001 Add `Channel` string property (default `"WhatsApp"`) to `Conversation` entity in `backend/src/Modules/Conversations/Domain/Conversation.cs`
- [ ] T002 [P] Add `FacebookPostId` (string?), `FacebookCommentId` (string?), `ParentCommentId` (string?) properties to `Message` entity in `backend/src/Modules/Conversations/Domain/Message.cs`
- [ ] T003 [P] Add `FacebookPSID` (string?) and `FacebookName` (string?) properties to `Customer` entity in `backend/src/Modules/Conversations/Domain/Customer.cs`
- [ ] T004 [P] Add `MessengerAiAutoReplyEnabled` (bool, default false), `MessengerReplyDelay` (int, default 5), `CommentsAiAutoReplyEnabled` (bool, default false), `CommentsReplyDelay` (int, default 10) to `ProjectSettings` entity in `backend/src/Modules/Projects/Domain/ProjectSettings.cs`
- [ ] T005 Create `ConnectedPage` entity at `backend/src/Modules/Facebook/Domain/ConnectedPage.cs` with fields: `Id` (Guid PK), `ProjectId` (Guid FK, ITenantEntity), `FacebookPageId` (string), `PageName` (string), `PageAccessToken` (string), `UserAccessToken` (string?), `FacebookUserId` (string?), `IsActive` (bool, default true), `TokenExpiresAt` (DateTime?). Inherits `AuditableEntity, ITenantEntity`.
- [ ] T006 Add `DbSet<ConnectedPage>` to `AppDbContext` in `backend/src/Shared/Infrastructure/AppDbContext.cs`. Add EF configuration for `ConnectedPage` with index on `(ProjectId)` and unique index on `(FacebookPageId)`.
- [ ] T007 Add `Channel` (string) and `ChannelMetadata` (string?) properties to `MessageAggregatedEvent` in `backend/src/Shared/Events/MessageAggregatedEvent.cs`
- [ ] T008 [P] Add `Channel` (string) and `ChannelMetadata` (string?) properties to `AIReplyGeneratedEvent` in `backend/src/Shared/Events/AIReplyGeneratedEvent.cs`
- [ ] T009 Add `channel` (string) and `facebookPostId`, `facebookCommentId` (string?) fields to `Conversation` and `Message` TypeScript interfaces in `frontend/src/types/chat.ts`

**Checkpoint**: All entity and event changes are in place. Ready for module implementation.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Facebook module services, OAuth flow, webhook infrastructure — MUST be complete before any user story

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T010 Create `IFacebookGraphService` interface at `backend/src/Modules/Facebook/Services/IFacebookGraphService.cs` with methods: `SendMessageAsync(pageId, pageAccessToken, recipientPSID, message)`, `ReplyToCommentAsync(pageAccessToken, commentId, message)`, `ReactToCommentAsync(pageAccessToken, commentId, reactionType)`, `SendPrivateReplyAsync(pageId, pageAccessToken, commentId, message)`, `SubscribePageToAppAsync(pageId, pageAccessToken)`, `GetUserPagesAsync(userAccessToken)` returning list of `{PageId, PageName, AccessToken}`
- [ ] T011 Implement `FacebookGraphService` at `backend/src/Modules/Facebook/Services/FacebookGraphService.cs`. Use `HttpClient` to call Facebook Graph API v20.0 endpoints: `POST /{page-id}/messages` for DMs, `POST /{comment-id}/comments` for public replies, `POST /{comment-id}/reactions` for reactions, `POST /{page-id}/subscribed_apps` for webhook subscription, `GET /me/accounts` for page listing. Include error handling and logging.
- [ ] T012 [P] Create `IFacebookOAuthService` interface at `backend/src/Modules/Facebook/Services/IFacebookOAuthService.cs` with methods: `GetLoginUrl(projectId, csrfToken)` returns string, `ExchangeCodeForTokenAsync(code)` returns UserAccessToken, `ExchangeForLongLivedTokenAsync(shortLivedToken)` returns long-lived token
- [ ] T013 Implement `FacebookOAuthService` at `backend/src/Modules/Facebook/Services/FacebookOAuthService.cs`. Read `FACEBOOK_APP_ID`, `FACEBOOK_APP_SECRET`, `FACEBOOK_OAUTH_REDIRECT_URI` from `IConfiguration`. Build OAuth URL with scope `pages_show_list,pages_read_engagement,pages_manage_engagement,pages_messaging,pages_manage_metadata`. Exchange code via `GET https://graph.facebook.com/v20.0/oauth/access_token`. Exchange for long-lived via same endpoint with `grant_type=fb_exchange_token`.
- [ ] T014 Create `FacebookOAuthController` at `backend/src/Modules/Facebook/API/FacebookOAuthController.cs`. Endpoints: `GET /api/facebook/oauth/login?projectId={id}` → generates CSRF token, stores in Redis with TTL 10min, redirects to Facebook Login Dialog. `GET /api/facebook/oauth/callback?code={code}&state={state}` → validates CSRF, exchanges code for token, calls `/me/accounts`, returns page list as JSON (or redirects to frontend with data).
- [ ] T015 Create `FacebookPageController` at `backend/src/Modules/Facebook/API/FacebookPageController.cs`. Endpoints: `POST /api/projects/{projectId}/facebook/pages/confirm` → receives selected page data from OAuth flow, creates `ConnectedPage`, calls `SubscribePageToAppAsync`. `GET /api/projects/{projectId}/facebook/pages` → list connected pages for project. `DELETE /api/projects/{projectId}/facebook/pages/{pageId}` → deactivate connected page.
- [ ] T016 Create `FacebookWebhookController` at `backend/src/Modules/Facebook/API/FacebookWebhookController.cs`. `GET /api/webhooks/facebook` → webhook verification (respond with `hub.challenge` if `hub.verify_token` matches). `POST /api/webhooks/facebook` → validate `X-Hub-Signature-256` using App Secret. Parse JSON payload: if `entry[].messaging` exists → Messenger message event; if `entry[].changes[].field === "feed"` and `value.item === "comment"` → comment event. For each event: resolve `ConnectedPage` by `PageId`, set tenant context, route to appropriate handler.
- [ ] T017 Register all Facebook module services in DI at `backend/Program.cs`: `AddScoped<IFacebookGraphService, FacebookGraphService>()`, `AddScoped<IFacebookOAuthService, FacebookOAuthService>()`, `AddScoped<FacebookReplySender>()`. Add `FACEBOOK_APP_ID`, `FACEBOOK_APP_SECRET`, `FACEBOOK_VERIFY_TOKEN`, `FACEBOOK_OAUTH_REDIRECT_URI` to configuration expectations.

**Checkpoint**: Facebook module is scaffolded. OAuth login flow and webhook handling are ready. Graph API service can send messages, reply to comments, and react.

---

## Phase 3: User Story 1 — Messenger DM Inbox (Priority: P1) 🎯 MVP

**Goal**: Receive Messenger DMs via webhook, display them in `/inbox/messenger`, and reply to them manually from the CRM.

**Independent Test**: Send a DM to the connected Facebook Page on Messenger → verify it appears in `/inbox/messenger` → reply from CRM → verify it's delivered on Messenger.

### Implementation for User Story 1

- [ ] T018 [US1] In `FacebookWebhookController.cs`, implement Messenger message handler: extract `sender.id` (PSID), `message.text`, `message.mid`. Resolve or create `Customer` by `FacebookPSID` (set `FacebookName` from profile if available). Resolve or create `Conversation` with `Channel = "Messenger"`. Save `Message` with `Direction = "Incoming"`. Broadcast via SignalR `ReceiveMessage` with `channel: "Messenger"`. Pass to `IMessageAggregator.AggregateMessageAsync` with channel context.
- [ ] T019 [US1] Modify `ConversationController.ListConversations` in `backend/src/Modules/Conversations/API/ConversationController.cs` to accept `[FromQuery] string channel = "WhatsApp"` parameter and filter by `Conversation.Channel`. Default to `"WhatsApp"` so existing inbox is unchanged. Add `channel` to response DTO.
- [ ] T020 [US1] Modify `ConversationController.SendReply` endpoint (or create new route `POST /api/projects/{projectId}/conversations/{conversationId}/reply`) to support `channel` field in request body. If channel is `"Messenger"`: look up `ConnectedPage` for the project, call `FacebookGraphService.SendMessageAsync` instead of WhatsApp gateway. Save outgoing message and broadcast via SignalR.
- [ ] T021 [US1] Create `FacebookReplySender` worker at `backend/src/Modules/Facebook/Workers/FacebookReplySender.cs`. Implements `IIntegrationEventHandler<AIReplyGeneratedEvent>`. Check `@event.Channel`: if `"Messenger"` → use `IFacebookGraphService.SendMessageAsync` with typing delays from `IHumanMessagingEngine`; if `"FacebookComment"` → handle in US2. Save outgoing messages, broadcast via SignalR. If channel is `"WhatsApp"` or null, skip (handled by existing `ReplySender`).
- [ ] T022 [US1] Register `FacebookReplySender` event subscription in `backend/Program.cs`: `eventBus.Subscribe<AIReplyGeneratedEvent, FacebookReplySender>()`.
- [ ] T023 [US1] Modify existing `ReplySender` in `backend/src/Modules/WhatsApp/Workers/ReplySender.cs` to skip events where `Channel` is not null and not `"WhatsApp"` (add early return: `if (!string.IsNullOrEmpty(@event.Channel) && @event.Channel != "WhatsApp") return;`).
- [ ] T024 [US1] Create `MessengerInbox` component at `frontend/src/packages/inbox/MessengerInbox.tsx`. Follow the same pattern as existing `Inbox.tsx` but: fetch conversations with `channel=Messenger` query param; display Facebook profile names; show 24h messaging window indicator per conversation (calculated from last customer message timestamp). Reuse existing CSS module styles where possible.
- [ ] T025 [US1] Create Next.js page at `frontend/src/app/(dashboard)/inbox/messenger/page.tsx` that renders `<MessengerInbox />`.
- [ ] T026 [US1] Add "صندوق الماسنجر" nav item to Sidebar in `frontend/src/components/layout/Sidebar.tsx` under "لوحات وقوائم" group, with path `/inbox/messenger` and `MessageCircle` icon from lucide-react. Also add to mobile drawer nav in `frontend/src/app/(dashboard)/layout.tsx`.

**Checkpoint**: Messenger DMs are received, displayed, and manually replied to. `/inbox/messenger` page works independently.

---

## Phase 4: User Story 2 — Facebook Comments Inbox (Priority: P2)

**Goal**: Receive Facebook Page post comments via webhook, display them in `/inbox/comments`, and reply with public comment + private DM + reaction simultaneously.

**Independent Test**: Comment on a post on the connected Page → verify it appears in `/inbox/comments` → reply from CRM → verify public reply comment, private DM, and reaction are all applied.

### Implementation for User Story 2

- [ ] T027 [US2] In `FacebookWebhookController.cs`, implement comment handler: when `entry[].changes[].field === "feed"` and `value.item === "comment"` and `value.verb === "add"`: extract `comment_id`, `post_id`, `from.id` (PSID), `from.name`, `message`. Resolve or create `Customer` by PSID. Resolve or create `Conversation` with `Channel = "FacebookComment"`. Save `Message` with `FacebookPostId = post_id`, `FacebookCommentId = comment_id`, `Direction = "Incoming"`. Auto-react to comment by calling `FacebookGraphService.ReactToCommentAsync(token, commentId, "LIKE")`. Broadcast via SignalR. Pass to aggregator if AI auto-reply is enabled for comments.
- [ ] T028 [US2] Create composite comment reply endpoint at `POST /api/projects/{projectId}/conversations/{conversationId}/comment-reply` in a new or existing controller. Request body: `{ publicComment: string, privateDM: string?, reaction: string? }`. Implementation: look up `ConnectedPage`, find the latest incoming message's `FacebookCommentId`, then: (1) `ReplyToCommentAsync` for public comment, (2) `SendPrivateReplyAsync` for DM if provided, (3) `ReactToCommentAsync` if reaction provided. Save outgoing messages for each action. Return `{ publicCommentSent: bool, privateDMSent: bool, reactionApplied: bool }`.
- [ ] T029 [US2] In `FacebookReplySender.cs`, add handling for `Channel == "FacebookComment"`: parse `ChannelMetadata` JSON to extract `commentId` and `postId`. Call `ReplyToCommentAsync` for public reply. Call `SendPrivateReplyAsync` for private DM. Save outgoing messages and broadcast via SignalR.
- [ ] T030 [US2] Create `CommentsInbox` component at `frontend/src/packages/inbox/CommentsInbox.tsx`. Differences from Messenger inbox: fetch with `channel=FacebookComment`. Show post context (post_id) in conversation header. Display comment thread view (comments as messages). Reply input shows three-action panel: "رد عام" (public comment text), "رسالة خاصة" (private DM text), and "ريأكت" (reaction type selector). Submit button calls the composite `/comment-reply` endpoint.
- [ ] T031 [US2] Create Next.js page at `frontend/src/app/(dashboard)/inbox/comments/page.tsx` that renders `<CommentsInbox />`.
- [ ] T032 [US2] Add "صندوق التعليقات" nav item to Sidebar in `frontend/src/components/layout/Sidebar.tsx` under "لوحات وقوائم" group, with path `/inbox/comments` and `MessageSquareMore` icon from lucide-react. Also add to mobile drawer nav.

**Checkpoint**: Facebook comments are received, displayed with post context, and replied to with 3-action reply (public comment + DM + reaction). `/inbox/comments` works independently.

---

## Phase 5: User Story 3 — AI Auto-Replies for Messenger & Comments (Priority: P3)

**Goal**: AI auto-reply engine automatically responds to Messenger DMs and comments using knowledge base, with per-channel settings.

**Independent Test**: Enable Messenger AI auto-reply in settings → send DM → verify AI response after configured delay. Same for comments.

### Implementation for User Story 3

- [ ] T033 [US3] Modify `AIReplyWorker.HandleAsync` in `backend/src/Modules/AI/Workers/AIReplyWorker.cs` to be channel-aware: read `@event.Channel` (default `"WhatsApp"` if null/empty). Check per-channel AI settings: if channel is `"Messenger"` → check `settings.MessengerAiAutoReplyEnabled` and use `settings.MessengerReplyDelay`; if `"FacebookComment"` → check `settings.CommentsAiAutoReplyEnabled` and use `settings.CommentsReplyDelay`; if `"WhatsApp"` → use existing `settings.AiAutoReplyEnabled` and `settings.ReplyDelay`. Skip AI reply if the channel's toggle is off. Pass `Channel` through to `AIReplyGeneratedEvent`.
- [ ] T034 [US3] Modify `AIReplyWorker` customer lookup to also search by `FacebookPSID` when the channel is Messenger/FacebookComment (in addition to existing `PhoneNumber` lookup for WhatsApp). Use: `var customer = channel == "WhatsApp" ? await dbContext.Customers.FirstOrDefaultAsync(c => c.PhoneNumber == @event.Sender) : await dbContext.Customers.FirstOrDefaultAsync(c => c.FacebookPSID == @event.Sender);`
- [ ] T035 [US3] Modify `ProjectController` settings endpoints in `backend/src/Modules/Projects/API/ProjectController.cs` to expose new fields: `messengerAiAutoReplyEnabled`, `messengerReplyDelay`, `commentsAiAutoReplyEnabled`, `commentsReplyDelay` in both GET and PUT endpoints. Update `ProjectSettingsRequest` DTO.
- [ ] T036 [US3] Modify `Settings.tsx` in `frontend/src/packages/settings/Settings.tsx` to add two new AI auto-reply sections below the existing WhatsApp toggle: "الرد التلقائي للماسنجر" (toggle + delay input) and "الرد التلقائي للتعليقات" (toggle + delay input). Load and save the new fields via the existing settings API.

**Checkpoint**: AI auto-replies work independently per channel with separate enable/disable and delay configurations.

---

## Phase 6: Facebook Page Connection UI (OAuth Flow)

**Goal**: Settings page allows connecting a Facebook Page via "Login with Facebook" button.

- [ ] T037 Create `FacebookConnect` component at `frontend/src/packages/settings/FacebookConnect.tsx`. Shows: connected pages list (fetched from `GET /api/projects/{projectId}/facebook/pages`) with status and disconnect button. "ربط صفحة فيسبوك" button that opens popup to `GET /api/facebook/oauth/login?projectId={id}`. After popup completes (detected via `window.postMessage` or polling), fetch updated pages list. Show page name, status indicator, and token expiry warning if < 7 days.
- [ ] T038 Integrate `FacebookConnect` component into `Settings.tsx` at `frontend/src/packages/settings/Settings.tsx`. Add a new section "ربط فيسبوك" after the existing settings sections. Render `<FacebookConnect />` component inside it.
- [ ] T039 In `FacebookOAuthController.cs`, implement the callback to return an HTML page that calls `window.opener.postMessage({type: 'facebook-oauth-success', pages: [...]})` and closes itself, so the Settings page receives the page list without full-page redirect.

**Checkpoint**: Users can connect/disconnect Facebook Pages via the Settings page using a simple OAuth flow.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Edge cases, 24h window enforcement, SignalR channel filtering, CSS/UX polish

- [ ] T040 Add 24h Messenger window enforcement: in `ConversationController.SendReply` and `FacebookReplySender`, check if the last incoming message from the customer is older than 24 hours. If so, return a warning response to the agent or skip the AI reply. Show a visual indicator in `MessengerInbox.tsx` when a conversation is outside the 24h window.
- [ ] T041 [P] Handle comment edit webhook events in `FacebookWebhookController.cs`: when `value.verb === "edited"`, update the existing message content where `FacebookCommentId` matches. Broadcast update via SignalR.
- [ ] T042 [P] Handle post deletion: when `value.verb === "remove"` and `value.item === "comment"`, mark the message as deleted. When a post is deleted, mark conversations linked to that post as "Archived" or "Closed".
- [ ] T043 [P] Create CSS module `frontend/src/packages/inbox/messenger.module.css` for Messenger-specific styles (Facebook blue accent color, 24h window badge, typing indicators). Create `frontend/src/packages/inbox/comments.module.css` for Comments-specific styles (post context card, triple-action reply panel, reaction picker).
- [ ] T044 Update SignalR `ReceiveMessage` event handling in both `MessengerInbox.tsx` and `CommentsInbox.tsx` to filter by channel: only update conversation lists when the incoming message's channel matches the current page.

---

## Phase 8: Quality Assurance & Final Verification

- [ ] T045 Deep architectural critique: review all new code against spec.md, plan.md, and constitution.md. Fix authorization gaps, missing tenant isolation, error handling, and UI regressions.
- [ ] T046 Run `clean-code-guard` against all changed production code files.
- [ ] T047 Run `test-guard` against any test files (if applicable).
- [ ] T048 Build verification: run `dotnet build` for backend and `npm run build` for frontend. Fix all compile errors and warnings.
- [ ] T049 Feature tests: run full feature test matrix covering all 3 user stories, OAuth flow, edge cases (24h window, comment edit, post deletion). Expected result: all acceptance scenarios from spec.md pass — Messenger DMs appear in `/inbox/messenger`, comment replies trigger public reply + DM + reaction, AI auto-reply respects per-channel settings.
- [ ] T050 Final verification and summary report: `dotnet build` passes with zero errors, `npm run build` passes with zero errors, all feature test matrix items verified.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — can start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user stories
- **Phase 3 (US1 - Messenger)**: Depends on Phase 2
- **Phase 4 (US2 - Comments)**: Depends on Phase 2. Can run in parallel with Phase 3 if separate developers.
- **Phase 5 (US3 - AI Auto-Reply)**: Depends on Phases 3 + 4 (needs both channels working)
- **Phase 6 (OAuth UI)**: Depends on Phase 2 (OAuth backend). Can run in parallel with Phases 3-5.
- **Phase 7 (Polish)**: Depends on Phases 3, 4, 5
- **Phase 8 (QA)**: Depends on all previous phases

### User Story Dependencies

- **US1 (Messenger)**: After Phase 2 — No dependencies on US2 or US3
- **US2 (Comments)**: After Phase 2 — Can use US1's `FacebookReplySender` but is independently testable
- **US3 (AI Auto-Reply)**: After Phase 2 — Modifies shared `AIReplyWorker`, best done after US1+US2

### Parallel Opportunities

- T002, T003, T004 can run in parallel (different entity files)
- T007, T008 can run in parallel (different event files)
- T010, T012 can run in parallel (different service interfaces)
- T041, T042, T043 can run in parallel (independent polish tasks)
- Phase 6 (OAuth UI) can run in parallel with Phases 3-5

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (entity + event changes)
2. Complete Phase 2: Foundational (Facebook module services)
3. Complete Phase 3: User Story 1 (Messenger inbox)
4. **STOP and VALIDATE**: Send a DM to the Page → verify it appears → reply from CRM
5. Deploy/demo if ready

### Incremental Delivery

1. Setup + Foundational → Facebook module ready
2. Add US1 → Messenger inbox works → Deploy
3. Add US2 → Comments inbox works → Deploy
4. Add US3 → AI auto-reply per channel → Deploy
5. Add OAuth UI → Easy page connection → Deploy
6. Polish → Edge cases and UX → Final release

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- Each user story should be independently completable and testable
- Commit after each task or logical group
- Stop at any checkpoint to validate story independently
- Default `Channel` to `"WhatsApp"` in all existing data to avoid breaking changes
