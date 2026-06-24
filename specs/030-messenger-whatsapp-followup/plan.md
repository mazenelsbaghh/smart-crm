# Implementation Plan: Messenger & WhatsApp Follow-Up Integration

**Branch**: `030-messenger-whatsapp-followup` | **Date**: 2026-06-24 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/030-messenger-whatsapp-followup/spec.md)

**Input**: Feature specification from `/specs/030-messenger-whatsapp-followup/spec.md`

## Summary

Implement Facebook Messenger follow-up tracking, automatic phone number extraction from Messenger messages, transition flow to WhatsApp welcome template message, and fallback flow on WhatsApp sending failure. In addition, Messenger AI responder must include the "first session free" reminder.

We will achieve this by:
1. Enhancing `AIReplyWorker.cs` to intercept phone numbers in incoming Messenger messages using a regex pattern, updating the customer profile, triggering the WhatsApp message via WhatsApp Gateway, sending a confirmation/failure back to Messenger, and stopping scheduled Messenger follow-ups.
2. Extending `FollowUpScheduler.cs` to check if a customer has a `FacebookPSID` but no `PhoneNumber` and routing the follow-up message to Messenger instead of WhatsApp.
3. Enhancing `AIReplyWorker.cs` to append a "first session free" rule to the static reference prompt context if the communication channel is Messenger.

## Technical Context

**Language/Version**: C# / .NET 9.0

**Primary Dependencies**: Microsoft.EntityFrameworkCore (9.0.0), Hangfire.AspNetCore (1.8.23), RabbitMQ.Client (7.2.1), StackExchange.Redis (2.13.1), SignalR

**Storage**: PostgreSQL (EF Core)

**Testing**: pytest

**Target Platform**: Linux server / Docker

**Project Type**: Web Service (ASP.NET Core API)

**Performance Goals**: WhatsApp trigger latency < 5s; 100% database updates within transaction.

**Constraints**: Strict project isolation via tenant context.

**Scale/Scope**: Unified follow-up and multi-channel synchronization.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular Monolith Architecture**: PASS. All changes reside inside `Modules/AI`, `Modules/CRM`, and `Modules/Facebook` namespaces and reference the common `AppDbContext` model.
- **Strict Multi-Tenant Project Isolation**: PASS. Tenancy is strictly isolated and all database queries implicitly apply the project filter or construct context with `ProjectId` resolved from settings.
- **Gemini 3.5 Flash Unified AI Engine**: PASS. The AI routing for text utilizes Gemini 3.5 Flash.
- **Human-Like Messaging and Aggregation**: PASS. Typing alerts and messaging delays conform to human-like patterns.
- **Risk-Based Action Approval System**: PASS. No high-risk data modifications are done without proper validation.

## Project Structure

### Documentation (this feature)

```text
specs/030-messenger-whatsapp-followup/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
└── quickstart.md        # Phase 1 output
```

### Source Code (repository root)

```text
backend/
└── src/
    ├── Modules/
    │   ├── AI/
    │   │   ├── Workers/
    │   │   │   └── AIReplyWorker.cs         # Intercept Messenger messages, transition flows, and inject prompt rules
    │   ├── CRM/
    │   │   ├── Services/
    │   │   │   └── FollowUpScheduler.cs     # Send Messenger follow-ups if PhoneNumber is missing but FacebookPSID exists
    └── Shared/
        └── Infrastructure/
            └── AppDbContext.cs
```

**Structure Decision**: Monolithic backend service updates. No frontend changes are strictly required since standard inbox views already display conversation feeds.
