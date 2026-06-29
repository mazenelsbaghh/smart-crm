# Research: Admin AI Behavior Settings

## Decision: Store structured AI behavior settings as JSON text on ProjectSettings

**Rationale**: `ProjectSettings` already owns per-project AI settings (`AiTonePreference`, `AiTargetAudience`, `SystemPrompt`, channel toggles). A single `AiBehaviorSettingsJson` column preserves tenant isolation, minimizes migration risk, and allows the settings schema to evolve without adding many nullable columns.

**Alternatives considered**:
- Separate normalized tables: more queryable but excessive for one settings document per project.
- Many columns on `ProjectSettings`: easier to inspect but creates schema churn for every UI field and channel override.
- Reuse `SystemPrompt` only: fails the requirement for organized admin inputs and validation.

## Decision: Use protected prompt rules + structured settings + secondary advanced prompt precedence

**Rationale**: CRM automation depends on required JSON fields and protected rules. Admin behavior settings should influence style and customer-facing text, not override schema, safety, pricing, or booking rules.

**Alternatives considered**:
- Let `SystemPrompt` replace the full template: caused the original bug where invalid JSON led to generic static fallback.
- Remove advanced prompt entirely: too restrictive for project-specific business behavior.

## Decision: Resolve settings through a dedicated AIBehaviorSettingsService

**Rationale**: Prompt generation, fallback messages, transitions, and reaction enforcement need the same defaults and channel override merge logic. A single service avoids duplicated JSON parsing and inconsistent fallback behavior.

**Alternatives considered**:
- Parse JSON directly inside each worker/controller: duplicates merging and validation logic.
- Put logic in `ProjectController`: mixes persistence validation with AI runtime behavior.

## Decision: Reject invalid templates before saving

**Rationale**: Admin-configured customer text must not break at send time. Validation will allow only supported placeholders and enforce length limits before persistence.

**Supported placeholders for v1**:
- `{customerName}`
- `{agentName}`
- `{projectName}`
- `{phoneNumber}`
- `{channel}`

**Alternatives considered**:
- Strip unknown placeholders at send time: hides admin mistakes.
- Save invalid templates and fall back later: makes production behavior unpredictable.
- No placeholders: too limited for transition/fallback messages.

## Decision: Reaction policy controls AI suggestion, persistence, and delivery

**Rationale**: User clarified "اتحكم ف اي حاجه". A disallowed reaction must not be sent even if the AI suggests it or a backend path receives it.

**Alternatives considered**:
- Prompt-only control: model may still output disallowed reactions.
- UI-only warning: backend paths remain unsafe.
- Facebook-only policy: does not cover WhatsApp auto-reaction and manual reaction endpoint.

## Decision: Frontend remains in existing Settings.tsx for v1

**Rationale**: The current settings page is the admin entry point. Adding structured sections there is the smallest complete user-facing change. A later refactor can extract subcomponents if the form grows further.

**Alternatives considered**:
- New dedicated AI behavior route: more navigation and permissions work.
- Keep raw textarea only: fails requirement for inputs and fixed/custom options.

## Decision: Existing auth/permissions are reused for this feature

**Rationale**: The current `ProjectController.UpdateSettings` endpoint does not expose fine-grained roles. This feature will not introduce a separate authorization model in v1; it preserves the current settings access behavior.

**Alternatives considered**:
- Owner/Admin-only enforcement now: desirable, but requires a broader auth/role audit outside this feature's current API pattern.
