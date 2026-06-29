# Implementation Plan: Admin AI Behavior Settings

**Branch**: `031-admin-ai-settings` | **Date**: 2026-06-27 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/specs/031-admin-ai-settings/spec.md)

**Input**: Feature specification from `/specs/031-admin-ai-settings/spec.md`

## Summary

Add structured, admin-editable AI behavior settings so project admins can control staff identity, signatures, tone/audience, per-channel behavior, reaction policy, and fallback/transition messages from the settings UI. Keep protected AI invariants code-owned: JSON response schema, CRM/follow-up schema, pricing guard, group booking safety, tenant isolation, and channel routing.

The implementation will extend `ProjectSettings` with an `AiBehaviorSettingsJson` text column, expose it through `ProjectController`, add a resolver/validator in `Modules.AI.Services`, and route prompt assembly, fallback messages, Messenger-to-WhatsApp transition messages, Facebook public comment fallback, and reaction enforcement through the resolved settings. The frontend settings page will add organized input sections while keeping the existing advanced prompt as secondary instructions.

## Technical Context

**Language/Version**: C# / .NET 9.0 backend; TypeScript / React 19 / Next.js 16 frontend

**Primary Dependencies**: ASP.NET Core API, Microsoft.EntityFrameworkCore 9.0.0, Npgsql EF Core 9.0.1, RabbitMQ event bus, Redis, SignalR, Next.js, Axios, lucide-react

**Storage**: PostgreSQL via EF Core. Add `ProjectSettings.AiBehaviorSettingsJson` as `text` JSON payload for structured settings.

**Testing**: pytest integration tests under `tests/phase_1`; `dotnet build backend/backend.csproj`; `npm run build` from `frontend/`

**Target Platform**: Linux server / Docker Compose

**Project Type**: Web Service with Next.js admin UI

**Performance Goals**: AI behavior resolution is in-memory JSON deserialization per message, target < 5ms per reply path; prompt cache key changes when behavior JSON changes.

**Constraints**: Strict `ProjectId` isolation; protected AI schema and safety rules cannot be overridden by admin input; existing project settings API must remain backward-compatible for existing tests/clients.

**Scale/Scope**: One structured settings document per project; channel overrides for WhatsApp, Messenger, FacebookComment.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular Monolith Architecture**: PASS. Changes stay inside `Modules/Projects`, `Modules/AI`, `Modules/Facebook`, `Modules/Conversations`, and existing frontend settings package. Cross-module behavior uses existing settings reads and events.
- **Strict Multi-Tenant Project Isolation**: PASS. AI behavior is stored on `ProjectSettings`, which already implements `ITenantEntity` and is accessed by `ProjectId`.
- **Gemini 3.5 Flash Unified AI Engine**: PASS. No new AI provider or OCR/STT integration.
- **Human-Like Messaging and Aggregation**: PASS. Existing aggregation and typing delays remain unchanged; only configurable text/reaction behavior changes.
- **Risk-Based Action Approval System**: PASS. This feature changes low/medium-risk configuration; protected rules prevent high-risk AI schema/CRM changes from admin prompt overrides.

## Project Structure

### Documentation (this feature)

```text
specs/031-admin-ai-settings/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── api.md
└── tasks.md
```

### Source Code (repository root)

```text
backend/
├── Migrations/
│   ├── 20260627000000_AddAiBehaviorSettingsToProjectSettings.cs
│   └── AppDbContextModelSnapshot.cs
└── src/
    ├── Modules/
    │   ├── Projects/
    │   │   ├── Domain/ProjectSettings.cs
    │   │   └── API/ProjectController.cs
    │   ├── AI/
    │   │   ├── Services/AIBehaviorSettings.cs
    │   │   ├── Services/AIBehaviorSettingsService.cs
    │   │   ├── Services/AIMarketingBrain.cs
    │   │   └── Workers/AIReplyWorker.cs
    │   ├── Facebook/
    │   │   └── Workers/FacebookReplySender.cs
    │   └── Conversations/
    │       └── API/ConversationController.cs
    └── Shared/Infrastructure/AppDbContext.cs

frontend/
└── src/packages/settings/
    ├── Settings.tsx
    └── settings.module.css

tests/
└── phase_1/
    └── test_admin_ai_settings.py
```

**Structure Decision**: Extend the existing modular monolith and existing settings page/API rather than creating a new settings module. The feature is configuration for an existing project-level domain object.

## Phase 0 Research Summary

See [research.md](research.md). Key decisions:
- Store structured behavior as JSON text on `ProjectSettings` for backward-compatible rollout.
- Add a resolver service that merges defaults, shared settings, channel overrides, and advanced prompt text.
- Reject invalid templates before persistence.
- Enforce reaction policy both at AI suggestion and backend send/save paths.

## Phase 1 Design Summary

See [data-model.md](data-model.md) and [contracts/api.md](contracts/api.md). The API extends existing `GET /api/projects/{id}` and `PUT /api/projects/{id}/settings` with `settings.aiBehavior`.

## Risk / Failure Modes

- **Invalid admin template leaks to customers**: Validate placeholders and length in `ProjectController` before saving.
- **Stale Gemini context cache after settings change**: Include resolved AI behavior JSON in static prompt content/hash.
- **Legacy `SystemPrompt` breaks JSON schema**: Treat as secondary instructions appended below protected schema; do not accept full replacement as default behavior.
- **Reaction bypass through manual endpoints**: Apply reaction policy in `AIReplyWorker`, `FacebookReplySender`, `ConversationController.CommentReply`, and `ConversationController.ReactToMessage`.
- **UI becomes one huge form**: Use sections with compact controls and stable layout; avoid nested cards.

## Test Commands

```bash
dotnet build backend/backend.csproj
pytest tests/phase_1/test_admin_ai_settings.py
pytest tests/phase_1/test_ai_gemini.py::test_ai_gemini_reaction_and_no_buttons
pytest tests/phase_1/test_messenger_whatsapp_followup.py::test_messenger_to_whatsapp_transition
cd frontend && npm run build
```

## Post-Design Constitution Check

- **Modular Monolith Architecture**: PASS. New service lives in AI module; settings persistence stays in Projects.
- **Strict Multi-Tenant Project Isolation**: PASS. Every read/write remains scoped by project settings.
- **Gemini 3.5 Flash Unified AI Engine**: PASS. No model/provider split introduced.
- **Human-Like Messaging and Aggregation**: PASS. Timing behavior remains unchanged.
- **Risk-Based Action Approval System**: PASS. Validation and protected precedence reduce AI/admin prompt risk.
