# Implementation Plan: WhatsApp Group Automation & Post-Session Workflows

**Branch**: `032-whatsapp-group-automation` | **Date**: 2026-06-29 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/032-whatsapp-group-automation/spec.md)

## Summary

This feature automates the lifecycle of WhatsApp groups for scheduled group sessions (Waves) and coordinates post-session follow-ups. 

1. **Addon Activation**: Admins toggle "WhatsApp Group Automation" from the Settings UI under "Addons".
2. **Group Creation & Setup**: The night before a session (at 11:00 PM Cairo time), the backend calls the WhatsApp Gateway to create a locked WhatsApp group (`wave [Name]`), sets settings to admin-only, adds the manager as an admin, and gets the invite link.
3. **Session Reminders**: The system immediately creates reminder follow-up messages for all booked students containing the invite link.
4. **Human Notification**: If a customer requests a human, the AI detects this and sends a direct WhatsApp notification to the manager (+20 106 869 0092) with customer details.
5. **Post-Session Funnel**: 2 days after the session, an automated follow-up asks students if they attended and if they subscribed. If they subscribed, the AI flags it, and the backend automatically blacklists them.

---

## User Review Required

> [!IMPORTANT]
> The manager's phone number (+20 106 869 0092) will be added as a configurable setting in `ProjectSettings` (field name `GroupAutomationManagerPhone`, default: `+201068690092`), allowing it to be edited from the API, with fallback to the default number.

> [!WARNING]
> Since we are running the local development environment inside Docker and port 80 is occupied by another local project, we will run integration tests against the remote Hostinger production server using `TEST_API_BASE_URL=http://147.93.86.206/api` (this was proven successful with the 031 test suite).

---

## Open Questions

- *None at this stage. Requirements are fully specified.*

---

## Proposed Changes

### Component: WhatsApp Gateway

Extend the gateway to support group creation, locking settings, and invite link retrieval via Baileys socket connection.

#### [MODIFY] [baileys-manager.js](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/whatsapp-gateway/src/baileys-manager.js)
- Implement and export `createGroup(projectId, subject, participants)`:
  - Format participant phone numbers to `@s.whatsapp.net`.
  - Call `sock.groupCreate(subject, participants)`.
  - Request invite code via `sock.groupInviteCode(groupJid)`.
  - Update group settings to locked: announcement and group settings editing restricted to admins via `sock.groupSettingUpdate(groupJid, 'announcement', 'locked')` and `sock.groupSettingUpdate(groupJid, 'locked', 'locked')`.
  - Return `{ jid, inviteLink }`.
  - Support mock session fallback return for integration testing.

#### [MODIFY] [index.js](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/whatsapp-gateway/src/index.js)
- Expose `POST /api/whatsapp/group/create` invoking `createGroup`.

---

### Component: C# Backend (Database & Domain Models)

Add new database properties and a database migration.

#### [MODIFY] [ProjectSettings.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Projects/Domain/ProjectSettings.cs)
- Add `public bool IsWhatsAppGroupAutomationEnabled { get; set; } = false;`
- Add `public string GroupAutomationManagerPhone { get; set; } = "+201068690092";`

#### [MODIFY] [GroupAppointment.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/GroupAppointments/Domain/GroupAppointment.cs)
- Add `public string? WhatsAppGroupJid { get; set; }`
- Add `public string? WhatsAppGroupInviteLink { get; set; }`

#### [NEW] [Migration File](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/Migrations/20260629000000_AddWhatsAppGroupAutomationToProjectSettingsAndGroupAppointment.cs)
- Add column `IsWhatsAppGroupAutomationEnabled` to `ProjectSettings`.
- Add column `GroupAutomationManagerPhone` to `ProjectSettings`.
- Add columns `WhatsAppGroupJid` and `WhatsAppGroupInviteLink` to `GroupAppointments`.

#### [MODIFY] [AppDbContextModelSnapshot.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/Migrations/AppDbContextModelSnapshot.cs)
- Update model snapshot to include the new columns.

---

### Component: C# Backend (API & Project Settings)

Expose the new settings in `ProjectController`.

#### [MODIFY] [ProjectController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Projects/API/ProjectController.cs)
- Update settings response DTO to return `isWhatsAppGroupAutomationEnabled` and `groupAutomationManagerPhone`.
- Update settings request DTO to accept `isWhatsAppGroupAutomationEnabled` and `groupAutomationManagerPhone`.
- Apply updates to `ProjectSettings` domain entity before database save.

---

### Component: C# Backend (AI Brain & Worker)

Extend AI reply flow to support human transfer notification, subscription blacklist, and session reminder overrides.

#### [MODIFY] [AIMarketingBrain.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Services/AIMarketingBrain.cs)
- Update `MarketingAnalysisResult` class:
  - Add `public bool RequestHuman { get; set; } = false;`
  - Add `public bool BlacklistCustomer { get; set; } = false;`
- Update JSON Prompt Template:
  - Add `""requestHuman"": true | false,`
  - Add `""blacklistCustomer"": true | false,`
  - Add instructions under `Guidelines for requestHuman`:
    - Set `requestHuman` to `true` if customer asks to talk to human, transfer to support, call manager, etc.
  - Add instructions under `Guidelines for blacklistCustomer`:
    - Set `blacklistCustomer` to `true` if customer confirms they have subscribed/registered in the paid course.
  - Update post-session response guidelines:
    - If customer did not attend, AI offers available active group slots.
    - If customer attended and subscribed, congratulate them and set `blacklistCustomer = true`.

#### [MODIFY] [AIReplyWorker.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Workers/AIReplyWorker.cs)
- Read `settings.IsWhatsAppGroupAutomationEnabled` and `settings.GroupAutomationManagerPhone`.
- If `analysisResult.RequestHuman` is true:
  - Trigger direct WhatsApp notification message to manager: `"العميل [Name] ([Phone]) طلب التحدث مع شخص طبيعي."`.
- If `analysisResult.BlacklistCustomer` is true:
  - Set `customer.IsBlacklisted = true`.
  - Save customer state.

---

### Component: C# Backend (Automation Lifecycle & Schedulers)

Schedule the group creation and reminder dispatch.

#### [MODIFY] [FollowUpScheduler.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/Services/FollowUpScheduler.cs)
- Register recurring Hangfire job `"whatsapp-group-automation-lifecycle"` in `StartAsync`.
  - Run every day at 11:00 PM Cairo local time: `"0 23 * * *"` using the Cairo timezone.
- Implement `RunWhatsAppGroupAutomationLifecycleJobAsync()`:
  - Query Cairo Date of "tomorrow" (current Cairo date + 1 day).
  - Convert tomorrow's start and end boundaries to UTC.
  - Fetch active `GroupAppointment` sessions scheduled for tomorrow.
  - For each session:
    - Check if `IsWhatsAppGroupAutomationEnabled` is true in `ProjectSettings`.
    - If group is already created (`WhatsAppGroupJid` is set), skip.
    - If not created:
      - Call gateway `/api/whatsapp/group/create` to create a group named `wave [Name]` adding `settings.GroupAutomationManagerPhone`.
      - Save returned `WhatsAppGroupJid` and `WhatsAppGroupInviteLink` to the `GroupAppointment` record.
      - Fetch all active `GroupAppointmentBooking` records for this session.
      - For each booked student:
        - If customer is blacklisted, skip.
        - Create a pending `FollowUp` record:
          - Type: `AppointmentReminder`
          - DueDate: `DateTime.UtcNow` (send immediately)
          - Notes: Rendered reminder template.
            - Online: `"أهلاً يا {customerName}، هذا هو رابط الجروب الذي سيرسل عليه رابط الحصة: {groupInviteLink}"`
            - Offline: `"أهلاً يا {customerName}، هذا هو رابط الجروب: {groupInviteLink}. نحن بانتظاركم!"`
        - Create another pending `FollowUp` record for the 2-day follow-up:
          - Type: `Nurturing`
          - DueDate: `appointment.DateTime.AddDays(2)` (2 days after session)
          - Notes: `"طمننا يا فندم، هل حضرت السيشن واشتركت معانا؟"`

---

### Component: Frontend UI (Settings Page)

Expose the new WhatsApp Group Automation settings.

#### [MODIFY] [Addons.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/settings/Addons.tsx)
- Expose the WhatsApp Group Automation card UI with a toggle switch connected to `isWhatsAppGroupAutomationEnabled`.
- Expose a text input for the manager's phone number (`groupAutomationManagerPhone`), visible when the toggle is active.

#### [MODIFY] [Settings.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/settings/Settings.tsx)
- Add state variable `isWhatsAppGroupAutomationEnabled` and `groupAutomationManagerPhone`.
- Bind values to DTO and save payload on settings load and save API calls.
- Render `<Addons>` with the new toggle and callbacks.

---

## Verification Plan

### Automated Tests
Run integration tests against the remote Hostinger production server by setting the target API URL.

1. **Verify toggling Settings**:
   - `PUT /api/projects/{id}/settings` updates `isWhatsAppGroupAutomationEnabled` and `groupAutomationManagerPhone`.
2. **Verify Group creation lifecycle**:
   - Call the Hangfire job logic directly to verify it creates a Baileys group on gateway mock, sets invite JID/link, and creates `FollowUp` reminders.
3. **Verify AI Auto-Blacklist & human request detection**:
   - Send messages like `"أريد التحدث مع بني آدم"` and verify requestHuman is true and manager is notified.
   - Send messages like `"نعم اشتركت في الدورة"` and verify customer becomes blacklisted.

### Manual Verification
- Deploy to production.
- Open the settings page, select the Addons tab, enable WhatsApp Group Automation, save.
- Schedule a session, manually trigger the lifecycle job, and verify the WhatsApp group is created, invite links are fetched, and reminders are sent.
