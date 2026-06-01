# Backend Master Plan

**Last Updated**: 2026-05-25

## Chronological Log

### 2026-05-25: AI Auto-Reply Context Contextualization, Delay Tuning & Auto-CRM Deal Sync (Completed)
- **Goal**:
  1. Inject the recent chat history (last 15 messages formatted chronologically) and Customer Memory (Facts, Objections, Summary) into the Gemini auto-reply prompt so that it responds intelligently with full customer context.
  2. Increase the message aggregation silence window to 30-50 seconds to prevent sending multiple quick responses.
  3. Increase the chunk typing simulation delay to 5-9 seconds between messages.
  4. Ensure that auto-applied CRM budget suggestions (high confidence) or manually approved budget updates in `ApprovalsController` also sync with active deals.
- **Updates**:
  - Modified `IAIMarketingBrain.AnalyzeAndGenerateReplyAsync` to accept `chatHistory` and `customerMemory` parameters, and inject them into the system prompt.
  - Updated `AIReplyWorker.HandleAsync` to fetch `CustomerMemory` and the last 15 conversation messages, formatting them cleanly before calling the AI engine.
  - Updated `MessageAggregator.cs` to wait for a randomized delay between 30 and 50 seconds before verifying and publishing aggregated messages.
  - Updated `HumanMessagingEngine.cs` to clamp the calculated chunk typing delay between 5000 and 9000 ms.
  - Updated `CRMAutoUpdateEngine.cs` and `ApprovalsController.cs` to synchronize the active open deal's Amount when a customer's budget is updated.

### 2026-05-25: CRM Customer Budget Persistence and AI Sync Brain Seeding Fixes (Completed)
- **Goal**: 
  1. Allow setting or clearing the customer's budget via the update endpoint, ensuring setting it to `null` is saved to PostgreSQL and the active deal's amount is synchronized.
  2. Fix the "مزامنة ذكاء AI" (Sync AI Brain) behavior so it does not delete the user's manual knowledge documents and replace them with mock data templates; instead, it should only seed templates if no documents exist, and otherwise re-index/regenerate embeddings for existing documents.
- **Updates**:
  - Modified `UpdateCustomerRequest` to use a backing field and flag (`IsBudgetSet`) to differentiate between omitting the `Budget` property and explicitly setting it to `null`.
  - Updated `CRMController.UpdateCustomer` to save budget changes (including `null`) if `IsBudgetSet` is true.
  - Synchronized `Deal.Amount` for the customer's active open deal to match the updated budget.
  - Modified `AICompanyBrain.SyncBrainAsync` to only seed the 3 default policy templates if the database is empty, and otherwise re-index any existing user documents that lack chunks or embeddings.

### 2026-05-25: SignalR AI Typing Indicator Broadcast (Completed)
- **Goal**: Send a real-time SignalR event when an incoming WhatsApp message is received and AI auto-reply is enabled, so the frontend can notify agents that the AI is responding.
- **Updates**:
  - Update `WebhookController.cs` to check `ProjectSettings.AiAutoReplyEnabled` upon message ingestion.
  - Broadcast `AITyping` message (passing `conversationId` and `isTyping: true`) to the tenant SignalR group (`project_{projectId}`).
  - Add integration tests or ensure existing flows are compatible.

## Chronological Log

### 2026-05-25: WA Web Version Fetching and Webhook Payload Validation Fixes (Completed)
- **Goal**: Resolve SyntaxError in `whatsapp-gateway` version fetching, fix backend webhook 400 Bad Request validation errors, and clean up integration test timing issues.
- **Updates**:
  - Replaced the nonexistent named export `fetchLatestWaWebVersion` with `fetchLatestBaileysVersion` from `@itsukichan/baileys` to fetch correct WhatsApp Web version strings.
  - Made the `Name` and `MessageType` fields in `IncomingMessagePayload` DTO nullable (`string?`) in C# `WebhookController.cs`, resolving model state validation errors for partial JSON webhook requests.
  - Increased sleep times in `test_ai_gemini.py` integration tests to 10 seconds to account for the simulated human typing delay (3.3 seconds) on auto-reply generation.
  - Standardized the test phone number generator in `test_ai_gemini.py` to be digits-only, preventing mismatches caused by letters getting stripped during gateway JID sanitization.
  - Verified all 9/9 Phase 1 core tests pass successfully.

### 2026-05-25: WhatsApp Gateway Message Sending and Receiving Fixes (Completed)
- **Goal**: Address message transmission failure in `whatsapp-gateway` by introducing strict JID sanitization, socket state validation, dynamic session path resolution, and robust message unwrapping.
- **Updates**:
  - Implemented phone number sanitization to strip raw inputs (`+`, spaces, dashes) to digits-only before building the WhatsApp JID (`number@s.whatsapp.net`).
  - Added connection state check in `sendMessage` throwing descriptive error if the socket is not initialized or disconnected.
  - Enhanced Baileys `messages.upsert` event handler to unwrap ephemeral or view-once wrapper messages and correctly extract content for text, images, and voice notes.
  - Added try-catch blocks around webhook delivery to prevent gateway crashes during backend outages.
  - Configured dynamic fallback path resolution for sessions directory (`/app/sessions` or `./sessions`) to support seamless local macOS and Docker host environments.

### 2026-05-25: Phase 6 Frontend Dashboard, Realtime & Production Hardening (Completed)
- **Goal**: Hardening backend authentication, SignalR presence and endpoints, configuring CORS rules, database/Redis connection pooling, rate limiting response structures, and structured exception handling middleware.
- **Updates**:
  - Enabled robust CORS policies matching allowed origins in `Program.cs` for smooth integration with the Next.js frontend application.
  - Implemented client-compliant SignalR presence hubs (`JoinProjectGroup` and `UpdatePresence` handlers) in `NotificationHub.cs` to track agent presence in Redis and support multi-tenant project grouping.
  - Modified `WebhookController.cs` to broadcast incoming WhatsApp messages to the project's WebSocket group via `ReceiveMessage`.
  - Updated `CRMController.cs` to add support for agent responses (`POST api/conversations/{id}/messages`) and customer profile updates (`PUT api/customers/{id}`).
  - Marked all reference properties in `UpdateCustomerRequest` DTO as nullable (`string?` and `string[]?`) to support optional JSON parameters in C#'s strict non-nullable context environment.
  - Extended `ListMessages` query in `ConversationController.cs` to return both frontend-optimized and backwards-compatible message property schemas (re-exposing `direction` and `timestamp` fields).

### 2026-05-25: Phase 5 Shared Assets, Media Engine & Audit Trail (Completed)
- **Goal**: Implement Phase 5 backend modules: Shared Asset Management (MinIO storage integration, hashing file deduplication, signed download URLs, database registry), Media Transformation Engine (SixLabors.ImageSharp thumbnailing and WhatsApp optimization via Hangfire MediaWorker), Audit & System Logging (Serilog structured logging, audit trails, Elasticsearch query integration), and System Health & Telemetry metrics APIs.
- **Core Abstractions & Files**:
  - `Asset.cs`, `AssetVariant.cs` under `Modules/Media/Domain`.
  - `MinIoStorageService.cs` under `Modules/Media/Services`.
  - `ImageTransformer.cs` and `MediaWorker.cs` under `Modules/Media/Services` & `Jobs`.
  - `AssetsController.cs` under `Modules/Media/API`.
  - `AuditLog.cs` under `Modules/Audit/Domain`.
  - `AuditService.cs` and `AuditController.cs` under `Modules/Audit/Services` & `API`.
  - `SystemHealthService.cs` and `SystemHealthController.cs` under `Modules/SystemHealth/Services` & `API`.
  - Migration code `AddMediaAndAudit` and integration tests inside `tests/phase_5/`.

### 2026-05-25: Phase 4 Campaigns, Advanced Analytics & Reporting (Completed)
- **Goal**: Implement Phase 4 backend modules: Campaign Engine (A/B testing, anti-ban throttling, Segment, Campaign, CampaignRecipient tables), Advanced Analytics & Reports System (AnalyticsSnapshot table, Hangfire Daily snapshot job), and Elasticsearch Integration (RabbitMQ indexing events consumer, multi-tenant search API).
- **Core Abstractions & Files**:
  - `Segment.cs`, `Campaign.cs`, `CampaignRecipient.cs` under `Modules/Campaigns/Domain` (and CRM/Domain).
  - `CampaignAIService.cs` for Gemini campaign copywriting under `Modules/Campaigns/Application/Services`.
  - `CampaignSenderJob.cs` using Hangfire and randomized anti-ban delay (5-15s) under `Modules/Campaigns/Jobs`.
  - `CampaignsController.cs` under `Modules/Campaigns/API`.
  - `AnalyticsSnapshot.cs` under `Modules/Analytics/Domain`.
  - `AnalyticsEngine.cs` for metric calculations under `Modules/Analytics/Application/Services`.
  - `DailyAnalyticsJob.cs` using Hangfire CRON schedule under `Modules/Analytics/Jobs`.
  - `AnalyticsController.cs` under `Modules/Analytics/API`.
  - `ElasticsearchIndexerWorker.cs` RabbitMQ event consumer under `Modules/Search/Workers`.
  - `SearchService.cs` using `Elastic.Clients.Elasticsearch` SDK under `Modules/Search/Application/Services`.
  - `SearchController.cs` under `Modules/Search/API`.
  - `PipelineStage.cs` and `Deal.cs` under `Modules/CRM/Domain`.
  - `CRMAdvancedController.cs` under `Modules/CRM/API`.
  - Migration code `AddCampaignsAndAnalytics` and integration tests inside `tests/phase_4/`.

### 2026-05-24: Phase 2 Service Orchestration & Scheduler (Completed)
- **Goal**: Support Hangfire Dashboard, SignalR WebSocket transport, and environment-level settings for Phase 2.
- **Updates**:
  - Integrated Hangfire storage using PostgreSQL connection string in `docker-compose.yml` and `appsettings.json`.
  - Exposed `/hangfire` route through Nginx proxy configuration if routing rules require it.
  - Exposed SignalR endpoints (`/hubs/notifications`) through Nginx proxy, ensuring proper WebSocket upgrade headers (`Upgrade`, `Connection`).
  - Add `make scheduler-status` command to verify Hangfire background jobs and RabbitMQ queues.
