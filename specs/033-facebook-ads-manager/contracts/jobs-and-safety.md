# Jobs, Decisions and Safety Contract

## Decision pipeline

1. Deterministic eligibility builds immutable evidence and may return `WAIT` before any model call.
2. Strategist returns a closed JSON proposal: action, target, expected state, proposed value, evidence IDs, hypothesis, evaluation window and risk class.
3. Auditor independently returns `APPROVE | REJECT | WAIT | ESCALATE` with reason codes.
4. Judge runs only for escalation classes or configured disagreement.
5. Safety Engine re-resolves all identifiers/numbers and checks project, connection, tracking, envelope, placement, cap, cooldown, evidence, external-state hash and stop status.
6. Approved action becomes one durable command with a unique idempotency key. Unsupported action types fail closed.

The model cannot supply a new price, destination, placement, project ID or financial boundary. Those values are selected from server-created identifiers.

## Safety invariants

- `publisher_platforms == [facebook]` for every delivery command.
- Active allocation sum never exceeds `dailyCap - safetyReserve`.
- No increase exceeds envelope maximum/cooldown or period remaining.
- No financial mutation runs with stale/unhealthy tracking or connection.
- No command runs during Emergency Stop or outside envelope time/market/offer/resource scope.
- Same `(ProjectId, IdempotencyKey)` produces at most one external mutation.
- Unknown provider result reconciles before retry.
- Provider state hash mismatch produces `Stale`, sync and a new decision—not blind apply.
- Pause is reversible; delete is unsupported.

## Recurrence

| Dispatcher | Frequency | Behavior |
|---|---:|---|
| Spend monitor | 5 min | Pull/reconcile spend, forecast cap, trigger stop |
| Provider sync | 10 min | Campaign/ad-set/ad configured/effective state |
| Insights | 15 min | Incremental time-window pull with overlap/dedupe |
| Tracking health | 15 min | Intake freshness, Dataset delivery, dedupe/error rate |
| Decision cycle | hourly | Evidence → review → safety → command |
| Impact review | 2 hours | Evaluate decisions whose windows matured |
| Fatigue | 6 hours | Frequency/performance/creative-age eligibility |
| Rebalance | daily 04:00 Cairo | Next-day allocation and ledger rollup |
| Tests | every 1–3 days | Propose only if budget/evidence capacity exists |
| Strategy | Monday 05:00 Cairo | Profile/funnel/fallback review |

Each global dispatcher enumerates eligible project IDs, then enqueues one project job. Project jobs use a Redis lease and a database unique `(ProjectId, JobType, TimeBucket)` record, and recheck all guards after lock acquisition.

## Stop behavior

- **Autopilot disabled**: stop new decisions/commands; show the exact last safe state of existing owned ads.
- **Financial freeze**: block mutations while still allowing read-only sync and incident recovery.
- **Emergency Stop**: immediately block commands, suspend envelope, cancel pending unsent commands and enqueue idempotent pauses for all system-owned active ads. Resume requires Owner/Admin, healthy dependencies and a fresh reconciliation.
