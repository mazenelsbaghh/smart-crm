# Tasks: WhatsApp Group Automation & Post-Session Workflows

- [ ] **Setup & Database Changes**
  - [ ] T001: Add `IsWhatsAppGroupAutomationEnabled` and `GroupAutomationManagerPhone` to `ProjectSettings` domain model.
  - [ ] T002: Add `WhatsAppGroupJid` and `WhatsAppGroupInviteLink` to `GroupAppointment` domain model.
  - [ ] T003: Re-create Entity Framework migration `AddWhatsAppGroupAutomationToProjectSettingsAndGroupAppointment` and compile.
  - [ ] T004: Update `AppDbContextModelSnapshot.cs` to reflect the new properties.

- [ ] **WhatsApp Gateway Changes**
  - [ ] T005: Implement and export `createGroup` in `baileys-manager.js` (including mock session support and group settings lock).
  - [ ] T006: Expose `POST /api/whatsapp/group/create` in `index.js`.

- [ ] **API Controller Changes**
  - [ ] T007: Update Settings DTOs in `ProjectController.cs` to expose `isWhatsAppGroupAutomationEnabled` and `groupAutomationManagerPhone`.
  - [ ] T008: Save updated settings fields to `ProjectSettings` in `ProjectController.cs` PUT method.

- [ ] **Lifecycle Scheduling & Reminders**
  - [ ] T009: Register recurring Hangfire job `"whatsapp-group-automation-lifecycle"` at 11:00 PM Cairo time in `FollowUpScheduler.cs`.
  - [ ] T010: Implement `RunWhatsAppGroupAutomationLifecycleJobAsync` in `FollowUpScheduler.cs` (query tomorrow's active appointments, call gateway group create, save JID/InviteLink).
  - [ ] T011: Schedule immediate `AppointmentReminder` follow-ups and 2-day `Nurturing` follow-ups in `FollowUpScheduler.cs` for booked students.

- [ ] **AI Prompt & Response Parsing**
  - [ ] T012: Add `RequestHuman` and `BlacklistCustomer` booleans to `MarketingAnalysisResult` class in `AIMarketingBrain.cs`.
  - [ ] T013: Instruct Gemini prompt in `AIMarketingBrain.cs` to parse `requestHuman` and `blacklistCustomer` JSON properties.
  - [ ] T014: Intercept `analysisResult.RequestHuman` and send WhatsApp notification to the manager in `AIReplyWorker.cs`.
  - [ ] T015: Intercept `analysisResult.BlacklistCustomer` and set `customer.IsBlacklisted = true` in `AIReplyWorker.cs`.

- [ ] **Frontend Settings UI**
  - [ ] T016: Add state and binding for the Group Automation checkbox and manager phone input under "Addons" tab in `Settings.tsx`.
  - [ ] T017: Display toggle switch and phone input for WhatsApp Group Automation in `Addons.tsx`.

- [ ] **Verification & Testing**
  - [ ] T018: Implement integration test suite `tests/phase_1/test_whatsapp_group_automation.py` covering settings toggle, group creation lifecycle, human request redirection, and auto-blacklisting.
  - [ ] T019: Deploy combined code to Hostinger production server using `deploy.sh`.
  - [ ] T020: Run tests against remote production API and confirm all scenarios pass.
