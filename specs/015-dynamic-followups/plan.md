# Implementation Plan: Dynamic Follow-up & Appointment Reminders

**Branch**: `015-dynamic-followups` | **Date**: 2026-06-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/015-dynamic-followups/spec.md`

## Summary

This plan updates the follow-up scheduling logic in the CRM module to support two distinct types of follow-ups:
1. **Nurturing (متابعة)**: Scheduled to send a message at the exact due date.
2. **Appointment/Course Reminder (تذكير بالموعد/الكورس)**: Scheduled to send a reminder exactly 24 hours before the booked appointment date-time. If the appointment is less than 24 hours away, the message is triggered immediately.

We will update the `FollowUp` entity in C#, generate a EF Core database migration, update the `CRMController` endpoint to handle the custom calculations and validations, modify the `FollowUpScheduler` Hangfire job to send appropriate messages, and enhance the frontend `CustomerDetail` and `FollowUps` UI with localized Arabic selectors and badges.

## Technical Context

**Language/Version**: C# (.NET 9.0), TypeScript / React (Next.js)

**Primary Dependencies**: Microsoft.EntityFrameworkCore, Hangfire, Lucide-react

**Storage**: PostgreSQL

**Testing**: Pytest integration tests in `tests/phase_1/test_follow_ups.py`

**Target Platform**: Linux Server (Docker/Docker Compose)

**Project Type**: Web Application (Frontend + Backend)

## Constitution Check

- **Modular Monolith Architecture**: The follow-up scheduler and domain boundary are completely confined within the `Modules/CRM` module. The updates do not violate boundary constraints.
- **Strict Multi-Tenant Project Isolation**: All follow-ups are queried and created scoped to the current tenant's `ProjectId`.

## Project Structure

### Documentation (this feature)

```text
specs/015-dynamic-followups/
├── spec.md              # Feature specification
├── plan.md              # This file
├── checklists/
│   └── requirements.md  # Specification Quality Checklist
└── tasks.md             # Task checklist (to be generated in Phase 3)
```

### Source Code

#### Backend

```text
backend/
└── src/
    └── Modules/
        └── CRM/
            ├── Domain/
            │   └── FollowUp.cs             # Add Type and AppointmentTime
            ├── API/
            │   └── CRMController.cs        # Add CreateFollowUp calculations & request params
            └── Services/
                └── FollowUpScheduler.cs    # Add type-specific messages
```

#### Frontend

```text
frontend/
└── src/
    ├── packages/
    │   └── management/
    │       └── FollowUps.tsx               # Type badge + table column
    └── components/
        └── shared/
            └── CustomerDetail.tsx          # Type dropdown + show/hide date input fields
```

---

## Proposed Changes

### 1. Database & Domain Models

#### [MODIFY] [FollowUp.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/Domain/FollowUp.cs)
- Add C# properties for follow-up type and appointment target date:
  ```csharp
  public string Type { get; set; } = "Nurturing"; // Nurturing, AppointmentReminder
  public DateTime? AppointmentTime { get; set; }
  ```

#### [NEW] [Migration] Create EF Core Migration
- Generate a new migration `AddFollowUpTypeAndAppointmentTime` to add the `Type` and `AppointmentTime` columns to the `FollowUps` table in PostgreSQL.

---

### 2. Backend Logic & Services

#### [MODIFY] [CRMController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/API/CRMController.cs)
- Update `CreateFollowUpRequest` to include:
  ```csharp
  public string? Type { get; set; }
  public DateTime? AppointmentTime { get; set; }
  ```
- Update `CreateFollowUp` endpoint:
  - If `Type == "AppointmentReminder"`:
    - Validate that `AppointmentTime` is present.
    - Set `AppointmentTime` as UTC.
    - Calculate `DueDate = AppointmentTime - 24 hours`.
    - If `DueDate < DateTime.UtcNow`, clamp it to `DateTime.UtcNow` (trigger immediately).
  - If `Type == "Nurturing"` or null/empty:
    - Set `Type = "Nurturing"`.
    - `DueDate` is set directly to the requested `DueDate`.
    - Set `AppointmentTime = null`.

#### [MODIFY] [FollowUpScheduler.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/Services/FollowUpScheduler.cs)
- Update `CheckOverdueFollowUpsJobAsync` message generation:
  - If `Notes` is empty/null and `Type == "AppointmentReminder"`, use a default Arabic course reminder message:
    `"مرحباً، نود تذكيرك بموعد الكورس غداً. ننتظر حضورك!"` (or dynamically formatted with the appointment time if possible).
  - Otherwise, fallback to the default nurturing message:
    `"مرحباً، أردنا فقط المتابعة معك لمعرفة ما إذا كان لديك أي استفسار آخر."`

---

### 3. Frontend UI Implementation

#### [MODIFY] [CustomerDetail.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/components/shared/CustomerDetail.tsx)
- Update `FollowUp` interface to add `type` and `appointmentTime`.
- Add states for the schedule form:
  - `newFollowUpType` (default: `"Nurturing"`)
  - `newAppointmentTime` (string)
- Render a select box in the "Schedule Follow-up" form:
  - Options: `"متابعة لتنشيط العميل"` (`Nurturing`) and `"تذكير بموعد / كورس"` (`AppointmentReminder`).
- Conditionally render inputs:
  - If `Nurturing`: Show DateTime input for due date ("تاريخ المتابعة").
  - If `AppointmentReminder`: Show DateTime input for appointment date ("تاريخ الموعد") and display a helper description explaining it sends 24 hours earlier.
- Update `handleAddFollowUp` payload to send the selected `type` and `appointmentTime`.
- Render the type badges (`متابعة` / `تذكير بموعد`) and target appointment time in the scheduled list.

#### [MODIFY] [FollowUps.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/FollowUps.tsx)
- Update `FollowUp` interface to include `type` and `appointmentTime`.
- Add a new "نوع المتابعة" (Follow-up Type) column to the table.
- Render styled badges for types:
  - `Nurturing` -> `"متابعة"` (blue/indigo badge).
  - `AppointmentReminder` -> `"تذكير بموعد"` (green/teal badge).
- Under the date column, if `type == "AppointmentReminder"`, render the target appointment time (e.g. `الموعد: DD/MM/YYYY hh:mm`).

---

## Verification Plan

### Automated Tests
- Run tests in `tests/phase_1/test_follow_ups.py` to ensure existing follow-up scenarios still pass.
- Write new integration test assertions in `tests/phase_1/test_follow_ups.py` checking:
  1. Creating a `Nurturing` follow-up executes exactly at `DueDate`.
  2. Creating an `AppointmentReminder` follow-up for 25 hours away sets `DueDate` to 1 hour from now.
  3. Creating an `AppointmentReminder` follow-up for 10 hours away sets `DueDate` to the current time (UtcNow).

### Manual Verification
- Open the application UI.
- Navigate to a customer profile.
- Schedule a "Nurturing" follow-up and verify it creates successfully.
- Schedule an "Appointment Reminder" for tomorrow and verify the send date is calculated correctly as today.
