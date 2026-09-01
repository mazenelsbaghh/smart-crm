# Jobs, Decisions and Safety Contract

## Decision pipeline

1. **Eligibility** builds immutable, windowed evidence and returns `WAIT` before model use when volume, attribution cutoff, learning, cooldown, tracking, connection, envelope or provider state is not mature.
2. **Strategist** chooses one action from the closed catalog using server-provided IDs/options and returns hypothesis, expected effect, risk, evaluation window and rollback.
3. **Auditor** independently evaluates the proposal and raw evidence and returns `APPROVE | REJECT | WAIT | ESCALATE` with reason codes.
4. **Judge** runs only for configured escalation/disagreement classes.
5. **Safety Engine** re-resolves project, plan, destination, provider state, envelope version, ledger, capability, tracking, cooldown and stop state immediately before external mutation.
6. **Execution** creates one durable command and provider-operation chain. Unknown mutation results reconcile before any resend.
7. **Impact** compares a stored baseline and matured outcome window and records `Positive | Negative | Inconclusive | Reverted`.

The model cannot provide a raw provider ID, new destination, new commercial fact, special category, ungranted audience source or out-of-envelope budget. Unsupported actions fail closed.

## Closed autonomous action catalog

- `CreatePlan`, `ValidatePlan`, `ProvisionPausedHierarchy`, `ActivateHierarchy`
- `ReconcileProviderState`, `QuarantineDrift`
- `StartExperiment`, `StopExperiment`, `PromoteWinner`, `RetireLoser`
- `ReplaceCreative`, `AdjustAudienceSuggestion`
- `ReserveBudget`, `ReleaseBudget`, `ReallocateBudget`, `IncreaseBudget`, `DecreaseBudget`
- `PauseManaged`, `ResumeManaged`
- `ChangeOptimizationGoal`
- `Wait`, `Escalate`

Every action has a typed eligibility rule, risk class, command mapper and impact evaluator. Generic `NoChange` is represented as `Wait` with exact reason codes.

## Safety invariants

- Every plan/ad set/creative/ad destination is the same authorized WhatsApp identity.
- Placement mode is dynamic provider-validated Advantage+, never a stale Facebook-only list.
- Hard audience controls and special-category constraints match the envelope and plan.
- Customer/list/lookalike sources have an active grant and legal basis.
- Active committed allocations never exceed `usableCap`; observed/forecast spend risk blocks increases.
- New allocation reserves atomically against every applicable daily plus monthly/total ledger using `max(observedSpend + delayedSpendEstimate, committedAmount + delta) <= usableCap`.
- No change exceeds period remaining, maximum increase or cooldown.
- No financial mutation runs with stale/unsafe connection, capability, tracking, provider state or envelope.
- Campaign, ad set and ad are `VerifiedPaused`, the creative is `Verified`, and field-level invariants have no blocking diff before activation.
- Same `(ProjectId, IdempotencyKey)` causes at most one external mutation.
- `Unknown` result reconciles before retry; state-hash mismatch becomes `Stale`.
- Sparse, delayed or low-coverage evidence produces `WAIT`, not pause/scale.
- Pause is reversible; permanent provider delete is unsupported.
- Monitoring/reconciliation continues while Autopilot is disabled or financially frozen.

## Recurrence

| Dispatcher | Frequency | Behavior |
|---|---:|---|
| Conversion/outbox retry | Every minute | Deliver pending eligible events with bounded retry |
| Spend guard | Every 5 minutes | Pull freshest managed spend and forecast cap/reserve risk |
| Provider reconciliation | Every 10 minutes | Read hierarchy, review/effective state and field drift |
| Insights pull | Every 15 minutes | Incremental overlapping window with dedupe |
| Tracking health | Every 15 minutes | Referral/outcome coverage, delay, conflict, WABA/Dataset and CAPI acceptance |
| Decision cycle | Hourly | Eligibility, review, safety and command |
| Impact review | Every 2 hours | Evaluate only due matured decisions |
| Fatigue | Every 6 hours | Sufficient impressions/frequency/outcome decline |
| Portfolio rebalance | Daily 04:00 project local time | Release/reserve next-period allocation atomically |
| Experiment proposal | Every 2 days | Only when sample and budget capacity exist |
| Strategy review | Monday 05:00 project local time | Offer, goal fallback, audience and creative portfolio |
| Retention | Daily 05:43 project local time | Compact allowed telemetry, preserve audit |

Each global dispatcher enumerates project IDs and enqueues one project job. A project job uses the active envelope's validated IANA timezone to resolve local business schedules and DST boundaries, acquires Redis lease `advertising:{job}:{projectId}`, claims a unique UTC database time bucket, then rechecks all guards. Lease loss, invalid timezone or stale bucket prevents mutation.

## Tracking-health state

`Healthy` requires all applicable thresholds:

- fresh connection/capability and WABA/Dataset state;
- supported Cloud API/coexistence referral proof for Business Messaging CAPI; Baileys protobuf field presence is not proof;
- referral capture coverage above configured minimum;
- attributable outcome coverage above the current goal minimum;
- exact-match rate at or above the versioned tracking policy minimum; provider-reported match evidence is separate and optional;
- acceptable median event delay and missing-referral rate;
- no unresolved dedupe conflicts;
- acceptable Business Messaging event acceptance/failure rate;
- correction rate inside the evidence policy.

`ExactMatchRate = eligible in-thread outcomes with an eligible exact ctwa/WABA/destination touch / all eligible in-thread outcomes`. The active `TrackingHealthPolicy` versions every threshold per goal. Insufficient denominator is `Unknown`. `Degraded` permits read-only sync and may allow a declared upper-funnel fallback. `Unsafe` deterministically freezes financial change and MUST activate Emergency Stop with trigger `TrackingUnsafe`. `Unknown` never counts as healthy. An incident recovers only after a new measured healthy snapshot.

## Spend guard

The five-minute job must request the freshest observable spend or a provider account/campaign summary. It may not rely solely on the 15/30-minute analytical snapshots. It updates observed spend, delayed-spend estimate, forecast, safety reserve and incident evidence. For every applicable period, guarded exposure is `max(observedSpend + delayedSpendEstimate, committedAmount)`; a new delta is allowed only if the same formula with `committedAmount + delta` remains inside usable cap. It then:

- blocks increases near the usable cap;
- pauses owned delivery on hard-cap/abnormal forecast according to policy;
- never claims exact-to-the-cent stopping when provider reporting is delayed.

## Normal disable

- Every disable creates an audited request with actor/time/affected ownership records.
- Default `PauseManaged`: block decisions/new commands, cancel unsent non-protective commands and enqueue idempotent pauses for all owned active hierarchy.
- Explicit per-request `LeaveRunning`: require Owner/Admin continuing-spend acknowledgement, block autonomous mutations but keep existing provider delivery, spend guard, insights, tracking and reconciliation active. UI shows continuing spend persistently.
- Re-enable requires fresh reconciliation and active envelope.

## Emergency Stop

1. Persist the active emergency record and suspend the envelope transactionally.
2. Block command claims immediately and cancel pending unsent non-protective commands.
3. Create one protective pause decision and idempotent commands for every owned active provider object required to stop delivery.
4. Reconcile each pause and show per-object progress/failure.
5. Continue read-only monitoring and retry only safe protective operations.
6. Resume only by Owner/Admin after health, provider state and envelope are freshly validated.

Triggers are `Manual`, `AbnormalSpend`, `CapRisk`, `CrossProjectGuard`, `TrackingUnsafe`, `Provider`, `RepeatedFinancialCommand` and `LostAuthorization`. Emergency commands take precedence over ordinary cooldown/learning rules but remain project-scoped, idempotent, ownership-checked and non-destructive.
