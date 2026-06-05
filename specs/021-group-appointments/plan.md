# Technical Plan: Group Appointments Add-on (إضافة مواعيد المجموعات)

**Feature Branch**: `021-group-appointments` | **Date**: 2026-06-05 | **Spec**: [spec.md](spec.md)

## Summary

Implement a new "Add-ons" (الاضافات) module containing "Group Appointments" (مواعيد المجموعات) which allows administrators to define slots and capacity constraints, and enables customers to book their spot via a public booking page. Overbooking is prevented via atomic capacity checks, and new bookings automatically sync with the CRM.

## Technical Context

- **Backend**: C# ASP.NET Core API (.NET 9)
- **Frontend**: Next.js, React 18, TypeScript, Tailwind CSS
- **Storage**: PostgreSQL with EF Core

## Constitution Check

| Principle | Check | Status / Justification |
| :--- | :--- | :--- |
| **I. Modular Monolith** | Keep Group Appointments decoupled from core conversations | **PASSED**. Uses events and separate controllers; references AppDbContext directly as a unified database context. |
| **II. Project Isolation** | Multi-tenant isolation by `ProjectId`. | **PASSED**. Enforced via global query filters on `ITenantEntity`. |
| **III. Gemini Unified AI** | Ensure AI is aware of active bookings. | **PASSED**. By syncing bookings to the CRM and generating Customer tags, Gemini can read customer context dynamically. |
| **IV. Human-Like Messaging** | WhatsApp bot notifications | **PASSED**. Agents are notified of bookings to reply manually if needed. |
| **V. Risk-Based Approval** | Admin configuration toggling. | **PASSED**. Features can be toggled on/off by the admin. |

## Proposed Changes

- **Backend**:
  - `Modules/Projects/Domain/ProjectSettings.cs`: Add `IsGroupAppointmentsEnabled` property.
  - `Modules/GroupAppointments/Domain/GroupAppointment.cs`: Entity for slot.
  - `Modules/GroupAppointments/Domain/GroupAppointmentBooking.cs`: Entity for booking.
  - `Shared/Infrastructure/AppDbContext.cs`: Add DbSets and mapping configurations.
  - `Modules/GroupAppointments/API/GroupAppointmentsController.cs`: CRUD and public booking actions.
- **Frontend**:
  - `src/packages/settings/Addons.tsx`: View to toggle add-ons.
  - `src/packages/settings/GroupAppointmentsManager.tsx`: Manage groups and view bookings.
  - `src/app/booking/[projectId]/page.tsx`: Public booking form.
