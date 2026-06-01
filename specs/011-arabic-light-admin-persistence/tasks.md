# Tasks: Arabic Light Admin Platform With Persistent WhatsApp State

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`) completed in `spec.md`
- [x] Phase 2: Technical Planning (`speckit-plan`) completed in `plan.md`
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed in this file

## Implementation Tasks

### Frontend Shell: Arabic RTL Light Mode

- [x] T001 In `frontend/src/app/layout.tsx`, change metadata to Arabic product wording and set root `<html>` to `lang="ar"` and `dir="rtl"`.
- [x] T002 In `frontend/src/styles/variables.css`, replace dark HSL tokens with light admin tokens, set Arabic-first font stack, and remove glassmorphism defaults in favor of solid light panels.
- [x] T003 In `frontend/src/app/globals.css`, adjust shared button/input helpers to light mode with visible focus states and no neon naming side effects in rendered style.
- [x] T004 In `frontend/src/components/layout/Sidebar.tsx`, translate all primary navigation labels and sign-out text to Arabic.
- [x] T005 In `frontend/src/components/layout/Header.tsx`, translate project selector labels, fallback values, and role display to Arabic.
- [x] T006 In `frontend/src/components/layout/layout.module.css`, update shell, sidebar, header, dropdown, and active navigation styles for RTL light admin layout.

### Settings Package: Arabic WhatsApp and API Key

- [x] T007 In `frontend/src/packages/settings/Settings.tsx`, add typed project settings load state for `aiAutoReplyEnabled`, `timezone`, and `geminiApiKey`.
- [x] T008 In `frontend/src/packages/settings/Settings.tsx`, call `GET /api/projects/{activeProject.id}` when the active project changes and populate settings state.
- [x] T009 In `frontend/src/packages/settings/Settings.tsx`, update `handleSaveGeneralSettings` to call `PUT /api/projects/{activeProject.id}/settings` with AI enabled, timezone, and Gemini API key.
- [x] T010 In `frontend/src/packages/settings/Settings.tsx`, add an Arabic password input labeled for Gemini/API key and ensure no raw key is logged.
- [x] T011 In `frontend/src/packages/settings/Settings.tsx`, translate page title, WhatsApp state labels, QR instructions, buttons, success/error messages, and bot setting labels to Arabic.
- [x] T012 In `frontend/src/packages/settings/settings.module.css`, update settings cards, forms, QR panel, buttons, alerts, and status colors for light mode and RTL.

### Persistence and Docker

- [x] T013 In `docker-compose.yml`, set `AUTO_RESTORE_SESSIONS=true` for `whatsapp-gateway` while keeping `./whatsapp-gateway/sessions:/app/sessions`.
- [x] T014 In `.env.example`, document `AUTO_RESTORE_SESSIONS=true` for local restore expectations if the variable exists in compose.
- [x] T015 Verify gateway source still has bounded reconnect attempts and does not delete sessions during normal failed restore.

### Inbox and Customer Arabic Pass

- [x] T016 In `frontend/src/packages/inbox/Inbox.tsx`, translate major visible headings, filters, input placeholders, status labels, and customer detail labels to Arabic without changing API data flow.
- [x] T017 In `frontend/src/packages/inbox/inbox.module.css`, ensure inbox surfaces inherit light tokens and RTL-compatible alignment.

### CrmX Admin Redesign & Full Arabic Translation

- [x] T023 In `backend/src/Modules/Conversations/API/WebhookController.cs`, query `ProjectSettings` inside `ReceiveMessage` and broadcast `AITyping` (with `conversationId` and `isTyping: true`) to the SignalR group if `AiAutoReplyEnabled` is true.
- [x] T024 In `frontend/src/services/signalr.ts`, register the server-sent `AITyping` event and expose a `registerOnAITyping` callback function.
- [x] T025 In `frontend/src/packages/inbox/Inbox.tsx`, manage `aiTypingConversations` state, subscribe to the `AITyping` event, and clear the typing state when an AI or Agent message is received.
- [x] T026 In `frontend/src/packages/inbox/Inbox.tsx` (or style module), add a pulsing typing bubble at the bottom of the chat log when `isAiTyping` is active, showing "الذكاء الاصطناعي يجهز الرد..." in Arabic.
- [x] T027 In `frontend/src/components/layout/Sidebar.tsx`, update to destructure `user` from `useAuth()` and render a centered user profile card (circular avatar, full name, and green dot with "نشط" status). Categorize the navigation links into "لوحات وقوائم", "أدوات الإدارة", and "النظام والإعدادات". Add a copyright footer.
- [x] T028 In `frontend/src/components/layout/Header.tsx`, remove the user profile info, style the header bar to be deep blue, and render the white glassmorphic search input and white project selector dropdown.
- [x] T029 In `frontend/src/components/layout/layout.module.css` and `variables.css`, define styles for the deep blue header, white sidebar layout, active menu right-border highlight, and avatar profile styling.
- [x] T030 In `frontend/src/packages/crm/PipelineBoard.tsx`, translate all headers, labels, placeholders, buttons, and status options into Arabic.
- [x] T031 In `frontend/src/packages/management/FollowUps.tsx`, translate all table headers, filters, text labels, and buttons into Arabic.
- [x] T032 In `frontend/src/packages/management/Campaigns.tsx`, translate all table headers, modal fields, labels, and buttons into Arabic.
- [x] T033 In `frontend/src/packages/management/Workflows.tsx`, translate the triggers, actions, labels, modal inputs, and buttons into Arabic.
- [x] T034 In `frontend/src/packages/management/KnowledgeBase.tsx`, `Approvals.tsx`, and `Reports.tsx`, translate all UI content, headings, and buttons into Arabic.

### Verification

- [x] T035 Run `npx tsc --noEmit` from `frontend` and resolve introduced TypeScript errors.
- [x] T036 Run `dotnet build backend/backend.csproj --no-restore` and ensure the backend builds successfully.
- [x] T037 Run `pytest tests/phase_1/ -v` to verify that all core integration tests pass.
- [x] T038 Validate Docker Compose config and rebuild/restart affected services if needed.
- [x] T039 Browser smoke-test `/settings`, `/inbox`, `/crm/pipeline`, and other management pages to verify the CrmX layout, full Arabic translation, and real-time AI reply indication.

## Verification Notes

- [x] Initial Phase 1 core tests pass (except message aggregator which has test-on-test interference but passes individually).
- [x] CrmX Admin visual layout, full Arabic pages translation, and SignalR typing indicator verified.
