# Tasks: WhatsApp Group Automation & Post-Session Workflows

- [x] **Setup & Database Changes**
  - [x] T001: Add `IsWhatsAppGroupAutomationEnabled` and `GroupAutomationManagerPhone` to `ProjectSettings` domain model.
  - [x] T002: Add `WhatsAppGroupJid` and `WhatsAppGroupInviteLink` to `GroupAppointment` domain model.
  - [x] T003: Re-create Entity Framework migration `AddWhatsAppGroupAutomationToProjectSettingsAndGroupAppointment` and compile.
  - [x] T004: Update `AppDbContextModelSnapshot.cs` to reflect the new properties.

- [x] **WhatsApp Gateway Changes**
  - [x] T005: Implement and export `createGroup` in `baileys-manager.js` (including mock session support and group settings lock).
  - [x] T006: Expose `POST /api/whatsapp/group/create` in `index.js`.

- [x] **API Controller Changes**
  - [x] T007: Update Settings DTOs in `ProjectController.cs` to expose `isWhatsAppGroupAutomationEnabled` and `groupAutomationManagerPhone`.
  - [x] T008: Save updated settings fields to `ProjectSettings` in `ProjectController.cs` PUT method.

- [x] **Lifecycle Scheduling & Reminders**
  - [x] T009: Register recurring Hangfire job `"whatsapp-group-automation-lifecycle"` at 11:00 PM Cairo time in `FollowUpScheduler.cs`.
  - [x] T010: Implement `RunWhatsAppGroupAutomationLifecycleJobAsync` in `FollowUpScheduler.cs` (query tomorrow's active appointments, call gateway group create, save JID/InviteLink).
  - [x] T011: Schedule immediate `AppointmentReminder` follow-ups and 2-day `Nurturing` follow-ups in `FollowUpScheduler.cs` for booked students.

- [x] **AI Prompt & Response Parsing**
  - [x] T012: Add `RequestHuman` and `BlacklistCustomer` booleans to `MarketingAnalysisResult` class in `AIMarketingBrain.cs`.
  - [x] T013: Instruct Gemini prompt in `AIMarketingBrain.cs` to parse `requestHuman` and `blacklistCustomer` JSON properties.
  - [x] T014: Intercept `analysisResult.RequestHuman` and send WhatsApp notification to the manager in `AIReplyWorker.cs`.
  - [x] T015: Intercept `analysisResult.BlacklistCustomer` and set `customer.IsBlacklisted = true` in `AIReplyWorker.cs`.

- [x] **Frontend Settings UI**
  - [x] T016: Add state and binding for the Group Automation checkbox and manager phone input under "Addons" tab in `Settings.tsx`.
  - [x] T017: Display toggle switch and phone input for WhatsApp Group Automation in `Addons.tsx`.

- [x] **Verification & Testing**
  - [x] T018: Implement integration test suite `tests/phase_1/test_whatsapp_group_automation.py` covering settings toggle, group creation lifecycle, human request redirection, and auto-blacklisting.
  - [x] T019: Deploy combined code to Hostinger production server using `deploy.sh`.
  - [x] T020: Run tests against remote production API and confirm all scenarios pass.
