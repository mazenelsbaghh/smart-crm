# Implementation Plan: Group Appointments & Follow-up Extensions

**Branch**: `023-group-appointments-followup-extensions` | **Date**: 2026-06-11 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `specs/023-group-appointments-followup-extensions/spec.md`

## Summary

This plan outlines the enhancements to the Group Appointments module, the Follow-up engine, the AI marketing brain, and the WhatsApp gateway. We will enforce single-group-booking per student, add attendance/payment tracking toggles, conditionally stop AI replies on payment, adapt follow-up messages on attendance, support Tone settings (Creative/Salesy) for follow-up notes, allow CSV export in the frontend, and disable automatic read receipt (seen status) inside the WhatsApp gateway.

## Technical Context

**Language/Version**: .NET 9.0 (C#) for Backend, Node.js v20+ for WhatsApp Gateway, TypeScript/React for Web Frontend.

**Primary Dependencies**: Microsoft.EntityFrameworkCore 9.0.0, Baileys (for Node.js socket), Gemini 3.5 Flash (via GeminiClient), Lucide React.

**Storage**: PostgreSQL with EF Core, Redis.

**Testing**: pytest (Python test harness).

**Target Platform**: Docker-based deployment.

**Project Type**: Multi-tenant web service.

**Performance Goals**: Instant client-side CSV generation, minimal latency for AI reply suppression.

**Constraints**: Strict tenancy isolation (each booking/follow-up must adhere to ProjectId).

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Core Principle | Status | Justification |
|---|---|---|
| I. Modular Monolith Architecture | PASS | All group booking logic remains inside `Modules.GroupAppointments`. CRM/Follow-up changes remain inside `Modules.CRM` and communicate with `Modules.AI` correctly. |
| II. Strict Multi-Tenant Project Isolation | PASS | Single booking check and all database/API operations are filtered strictly by `ProjectId`. |
| III. Gemini 3.5 Flash AI Engine | PASS | We will use Gemini 3.5 Flash for follow-up rewrites. |
| IV. Human-Like Messaging | PASS | Follow-up messages sent through the gateway will follow normal delivery paths. |
| V. Risk-Based Action Approval System | PASS | Marking attendance/payment and exporting CSV are standard low/medium risk database updates and UI reads. |

## Project Structure

### Documentation (this feature)

```text
specs/023-group-appointments-followup-extensions/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
└── quickstart.md        # Phase 1 output
```

### Source Code

```text
backend/src/
├── Modules/
│   ├── GroupAppointments/
│   │   ├── Domain/
│   │   │   └── GroupAppointmentBooking.cs   # Modifying: add IsAttended, IsPaid
│   │   └── API/
│   │       └── GroupAppointmentsController.cs# Modifying: single booking check, patch/update booking endpoint, include fields in response
│   ├── CRM/
│   │   ├── Domain/
│   │   │   └── FollowUp.cs                   # Modifying: add Tone field
│   │   ├── Services/
│   │   │   └── FollowUpScheduler.cs         # Modifying: pass IsAttended and Tone to RewriteFollowUpNotesAsync
│   │   └── API/
│   │       └── CRMController.cs             # Modifying: update Create/Update requests, pass args to RewriteFollowUpNotesAsync
│   ├── AI/
│   │   ├── Services/
│   │   │   ├── IAIMarketingBrain.cs         # Modifying: adjust RewriteFollowUpNotesAsync signature
│   │   │   └── AIMarketingBrain.cs          # Modifying: implement RewriteFollowUpNotesAsync with IsAttended and Tone prompt adjustments
│   │   └── Workers/
│   │       └── AIReplyWorker.cs             # Modifying: skip auto-reply if customer has paid group booking
whatsapp-gateway/src/
└── baileys-manager.js                        # Modifying: remove or disable sock.readMessages() call

frontend/src/
└── packages/
    └── settings/
        └── GroupAppointmentsManager.tsx      # Modifying: add Attended/Paid toggles and CSV Export button
```

**Structure Decision**: Modular Monolith matching repository layout. Backend changes in C# modules, frontend changes in React/Next.js package, and gateway changes in Node.js.
