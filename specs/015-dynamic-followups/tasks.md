# Tasks: Dynamic Follow-up & Appointment Reminders

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`) completed in `spec.md`
- [x] Phase 2: Technical Planning (`speckit-plan`) completed in `plan.md`
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed in this file

## Implementation Tasks

### Foundational Tasks

- [x] T001 In C# model file [FollowUp.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/Domain/FollowUp.cs), add the following properties:
  ```csharp
  public string Type { get; set; } = "Nurturing"; // Nurturing, AppointmentReminder
  public DateTime? AppointmentTime { get; set; }
  ```
- [x] T002 Generate database migration in the backend folder:
  `dotnet ef migrations add AddFollowUpTypeAndAppointmentTime --project backend.csproj --startup-project backend.csproj`
  and apply it:
  `dotnet ef database update --project backend.csproj --startup-project backend.csproj`

---

### Backend Logic Updates

- [x] T003 In [CRMController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/API/CRMController.cs), modify `CreateFollowUpRequest` to include:
  ```csharp
  public string? Type { get; set; }
  public DateTime? AppointmentTime { get; set; }
  ```
- [x] T004 In [CRMController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/API/CRMController.cs), inside the `CreateFollowUp` endpoint, add the type-specific calculation logic:
  - If `Type` is `"AppointmentReminder"`, validate that `AppointmentTime` is present. Set `DueDate` to `AppointmentTime - 24 hours`. If the calculated `DueDate` is in the past, clamp it to `DateTime.UtcNow`. Set `AppointmentTime` in UTC database-side.
  - If `Type` is `"Nurturing"` or null/empty, set `Type` to `"Nurturing"`, set `DueDate` to the request's `DueDate`, and set `AppointmentTime = null`.
  - Populate these properties into the newly created `FollowUp` object.
- [x] T005 In [FollowUpScheduler.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/Services/FollowUpScheduler.cs), inside `CheckOverdueFollowUpsJobAsync`, update the default message selection:
  - If `followUp.Notes` is empty or null, check the `Type`:
    - If `Type == "AppointmentReminder"`, use: `"مرحباً، نود تذكيرك بموعد الكورس غداً. ننتظر حضورك!"`.
    - If `Type == "Nurturing"`, fallback to the default: `"مرحباً، أردنا فقط المتابعة معك لمعرفة ما إذا كان لديك أي استفسار آخر."`.

---

### Frontend UI Updates

- [x] T006 In [FollowUps.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/FollowUps.tsx):
  - Add `type?: 'Nurturing' | 'AppointmentReminder';` and `appointmentTime?: string;` to the `FollowUp` interface.
  - Add a new "نوع المتابعة" (Follow-up Type) column header and row cell in the table.
  - Render a styled badge: `"متابعة"` (blue/indigo styling) for `Nurturing`, and `"تذكير بموعد"` (green/teal styling) for `AppointmentReminder`.
  - In the "تاريخ الاستحقاق" cell, if `type == "AppointmentReminder"` and `appointmentTime` is present, display `الموعد: ` followed by the formatted `appointmentTime`.
- [x] T007 In [CustomerDetail.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/components/shared/CustomerDetail.tsx):
  - Add `type?: 'Nurturing' | 'AppointmentReminder';` and `appointmentTime?: string;` to the `FollowUp` interface.
  - Add React state variables `newFollowUpType` (default `"Nurturing"`) and `newAppointmentTime` (default `""`).
  - Render a `<select>` dropdown inside the "Schedule Follow-up" form to choose between:
    - `"متابعة لتنشيط العميل"` (maps to `"Nurturing"`)
    - `"تذكير بموعد / كورس"` (maps to `"AppointmentReminder"`)
  - If `newFollowUpType` is `"Nurturing"`, render the datetime input for due date (label: `"تاريخ المتابعة"`).
  - If `newFollowUpType` is `"AppointmentReminder"`, render the datetime input for the appointment date (label: `"تاريخ الموعد"`) and display a friendly helper text in Arabic: `"سيتم إرسال رسالة التذكير تلقائياً قبل هذا الموعد بـ 24 ساعة."`
  - In `handleAddFollowUp`, pass `type` and `appointmentTime` in the POST body. Reset form fields on success.
  - In the history list cards, render a small label badge indicating the type of follow-up and the target appointment time.

---

### Phase N: Rebuild & Verify

- [x] T008 Rebuild backend and frontend services:
  `docker compose up -d --build backend frontend`
- [x] T009 Run existing integration tests to ensure no regressions:
  `make test-phase-1` and `make test-phase-3`
- [x] T010 Add integration test cases to `tests/phase_1/test_follow_ups.py` testing reminder due date calculation, clamping if under 24 hours, and nurturing defaults. Run:
  `pytest tests/phase_1/test_follow_ups.py`
