# Feature Test Matrix Reference

Use this reference during Phase 9 to decide which tests are mandatory for the finished feature.

## Minimum Matrix

Every feature must record at least one verification line for each applicable row:

| Area | Required Evidence |
|------|-------------------|
| User story happy path | Command and expected successful observable result |
| Permission/access failure | Unauthorized or wrong-role behavior is blocked |
| Validation failure | Invalid input produces a controlled error |
| Persistence/state | Data is saved, updated, or rolled back correctly |
| Regression | The original bug or requested behavior is directly covered |
| UI/E2E | User can complete the changed frontend flow when UI changed |
| Build | Backend and frontend build/type/lint checks relevant to touched code |

## Domain-Specific Rows

Add these rows when relevant:

- Purchases/payment/entitlements: unpaid blocked, paid/granted allowed, duplicate/concurrent purchase safe.
- Homework: admin creates/updates homework, student cannot access before entitlement, student can submit after entitlement, duplicate submission blocked.
- Exams: start blocked before entitlement, start after entitlement, submit answers, grading/result state, retry limits.
- Media/video: provider URL valid, playback access controlled, anti-download/watch-limit rules preserved.
- Notifications/background jobs: enqueue, retry/failure, idempotency, observable status.

## Evidence Format

Append to `achievements.md`:

```markdown
### Feature Test Evidence / إثبات اختبارات الفيتشر
- [x] <area>: `<command>` → <result>
- [x] <area>: Manual/browser check at <path> → <result>
```

Never mark Phase 9 complete with only "not run" evidence unless the user explicitly stopped execution and the final report says the workflow is incomplete.
