# Research: Autonomous WhatsApp AI Media Buyer

**Research date**: 2026-08-18 (Africa/Cairo)

This document replaces the Facebook-only assumptions in the original feature. The user accepted dynamic Advantage+ placement eligibility while requiring WhatsApp to remain the destination of every managed advertisement.

## 1. Repair strategy: compile, validate and reconcile a provider plan

**Decision**: replace the two ad-creation paths with one `CampaignPlan` compiler and one provider operation pipeline. The compiler produces a versioned, immutable plan for campaign, ad set, creative and ad objects. The provider pipeline performs local validation, Meta validation, paused creation in dependency order, read-back, normalized comparison and reconciliation before activation.

**Rationale**: the current general path discards its destination URL, omits the WhatsApp promoted object and creates only country plus Facebook positions. The video-test path builds a different hierarchy and copies only a subset of the source audience. Fixing each path separately would preserve contradictory behavior.

**Alternatives considered**:

- Patch the current controller and video test independently: rejected because targeting, destination, objective, validation and retries would continue to drift.
- Let Meta object IDs represent success: rejected because an accepted parent can have rejected, missing or materially different children.

## 2. Graph versioning and capability discovery

**Decision**: pin the Meta adapter to `v26.0` by default, make the version configurable from an allowlisted supported range, record it on every plan and provider operation, and fail startup/readiness when the configured version is unsupported. Treat documentation and SDK enums as candidates only. Verify every objective, optimization goal, destination, placement and automation feature against the live account with provider validation and read-back.

Capability discovery is a recorded probe set, not an imagined single matrix endpoint: `/me/permissions`; available ad accounts and selected account status/currency/timezone/business fields; available Pages/tasks; business WABAs and `/{waba-id}/phone_numbers`; `GET|POST /{waba-id}/dataset`; exact compiled object requests with `execution_options=["validate_only"]` only where the pinned version accepts it; then paused creation/read-back for anything validation-only cannot prove. Each row stores fields requested, response hash, provider trace, expiry and derivation. A combination becomes eligible only after those exact-shape checks.

**Rationale**: Meta's current click-to-WhatsApp developer guide examples use Graph API v26.0. Account rollout, permissions and product eligibility can still differ even when a generated SDK contains an enum.

**Alternatives considered**:

- Keep the current v25.0 default: rejected because the feature is being rebuilt against the current guide and must not preserve a stale contract.
- Follow the newest version automatically: rejected because silent version changes can mutate spend behavior.

**Primary references**: [Click-to-WhatsApp Marketing API guide](https://developers.facebook.com/docs/marketing-api/ad-creative/messaging-ads/click-to-whatsapp/), [Marketing API overview](https://developers.facebook.com/docs/marketing-api/), [official Business SDK](https://github.com/facebook/facebook-python-business-sdk).

## 3. Connection and production capability catalog

**Decision**: expand the connection from ad account, Page and Dataset to a mutually validated resource set:

- Ad account, currency, timezone, funding/account status and granted advertising capabilities.
- Page identity.
- WhatsApp Business Account ID, WhatsApp phone number ID and display number used by the advertisement.
- Dataset associated with the WhatsApp Business Account.
- Supported WhatsApp Business Platform mode (`CloudApi` or `CloudApiCoexistence`) and an observed official referral fixture; a Baileys protobuf field is not production proof.
- Supported objectives, optimization goals, bid strategies, placements and validation support.
- Permissions and access level, including `ads_read`, `ads_management`, Page content permissions, `whatsapp_business_management` and `whatsapp_business_manage_events` when Business Messaging events are enabled.

Readiness persists the last checked capability snapshot, exact missing permissions, provider trace/correlation ID and retry or App Review guidance. Advanced-access eligibility and current call-quality requirements are operational readiness checks, not generic connection errors.

**Rationale**: Page messaging access does not prove advertising, WABA, Dataset or business-event access. A phone number string alone cannot prove the selected destination and measurement source are mutually eligible.

**Alternatives considered**: reuse the existing messaging token and `ConnectedPage` record; rejected because it conflates customer messaging authority with financial advertising authority.

## 4. WhatsApp destination and optimization matrix

**Decision**: model destination, campaign objective and optimization goal as separate typed values selected from a runtime capability matrix.

Default progression:

1. Start with the current click-to-WhatsApp engagement/messaging campaign shape and `CONVERSATIONS` when the account validates it and downstream signal is not mature.
2. Use a WhatsApp leads/performance-goal combination only when the account validates that exact combination.
3. Use WhatsApp messaging purchase optimization only when the account validates it and qualified/purchase business-messaging events meet the configured volume, freshness, match and correction gates.
4. Fall back to the last validated messaging-compatible goal. Never fall back to website traffic, landing-page views or link clicks.

The ad-set promoted object always binds the Page and WhatsApp phone identity, the destination type is WhatsApp, and every creative CTA binds the same authorized WhatsApp destination. `OUTCOME_ENGAGEMENT` is the initial campaign-objective candidate from the current official guide, but it is not hard-coded as universally eligible.

**Rationale**: the current allowlist excludes conversations and permits link clicks and landing-page views, which optimizes the wrong outcome for a WhatsApp-only funnel.

**Alternatives considered**:

- Hard-code purchase optimization: rejected because live eligibility and signal maturity vary.
- Optimize link clicks until purchases exist: rejected because cheap clicks can produce no useful WhatsApp conversation.

**Primary references**: [Click-to-WhatsApp Marketing API guide](https://developers.facebook.com/docs/marketing-api/ad-creative/messaging-ads/click-to-whatsapp/), [click-to-message ads](https://www.facebook.com/business/ads/click-to-message-ads), [lead ads with messaging](https://www.facebook.com/business/ads/ad-objectives/lead-generation/lead-ads-with-messaging).

## 5. Advantage+ placement policy

**Decision**: request Advantage+ placements by not sending a fixed Facebook publisher/position list. Before activation, verify that the live objective, destination and creative combination is eligible. Read back the effective configuration and block any non-WhatsApp destination or provider-ineligible placement. Store observed placement delivery from insights rather than pretending a permanent local position allowlist is complete.

Account-level placement exclusions may be honored only when they are explicit business or brand-safety controls inside the envelope. The AI does not narrow placements merely to create an audience experiment.

**Rationale**: the user selected every Meta placement currently eligible for WhatsApp. Meta reports that automated placements can lower cost per result. The old `publisher_platforms=[facebook]` invariant directly contradicts that choice.

**Alternatives considered**:

- Facebook plus Instagram hard allowlist: rejected by the accepted clarification.
- Accept every placement without destination validation: rejected because placement eligibility does not prove the resulting ad still opens the authorized WhatsApp conversation.

**Primary reference**: [Advantage+ placements](https://www.facebook.com/business/ads/meta-advantage-plus/placements).

## 6. Advantage+ audience with hard business controls

**Decision**: store an `AudienceStrategy` that distinguishes:

- **Hard controls**: included/excluded locations, minimum age, language when required, special-category rules, custom-audience exclusions, prohibited geographies and authorized customer-data sources.
- **Suggestions**: age range above the minimum, gender, interests, custom audiences and lookalikes used to guide delivery when the live account supports them.
- **Observed evidence**: estimated size/reach when available, actual reach, overlap, delivery concentration, qualified outcomes and cost.

Default to broad Advantage+ audience inside hard controls. Do not send detailed-targeting exclusions, which Meta removed for new and active ad sets from 2025-03-31. Customer lists, retargeting and lookalikes require both an envelope authorization and the applicable consent/legal basis.

**Rationale**: the current country-only object is not a targeting strategy, while narrow interest stacks fragment learning and can raise cost. Hard business limits must never be converted into suggestions.

**Alternatives considered**:

- Infer interests and force them as restrictions: rejected because it reduces delivery freedom without outcome evidence.
- Broad with no controls: rejected because location, age, legal and exclusion requirements are business invariants.

**Primary references**: [Advantage+ audience](https://www.facebook.com/business/ads/meta-advantage-plus/audience), [current audience targeting changes](https://www.facebook.com/help/messenger-app/717368264947302/).

## 7. Special advertising categories and policy classification

**Decision**: replace `special_ad_categories=[]` with a pre-launch policy classification. The sourced offer profile records the proposed category, jurisdiction, policy evidence and unresolved risks. Housing, employment, credit and any other provider-regulated category use the corresponding live-account constraints. Unresolved category or unsupported claims block validation.

**Rationale**: the current code asserts no special category for every campaign. Existing page content can include employment or guarantee-like claims, making that behavior unsafe and a likely source of rejection or bad targeting.

**Alternatives considered**: let Meta reject the campaign later; rejected because targeting and creative may already be wrong and the UI would provide no actionable reason.

## 8. Budget ownership, bidding and consolidation

**Decision**: default to one consolidated campaign per project, authorized offer-to-WhatsApp pairing, currency and compatible business goal. Use Advantage+ campaign budget when validated, with the smallest number of ad sets needed for genuinely different hard controls or one-variable experiments. Persist one external budget owner per allocation.

Use highest-volume/lowest-cost bidding while signal is sparse. Enable a cost cap only after enough mature attributed outcomes establish a credible sustainable target and the account validates the strategy. Never derive target CPA from daily budget. Budget changes are planned as reallocations: reserve, release, then mutate one budget owner, with cooldown and learning-state gates.

**Rationale**: the current static 70/15/10/5 split and `max(25, dailyBudget)` target CPA are unrelated to business economics. Separate ad sets per creative also fragment learning.

**Alternatives considered**:

- One campaign/ad set per creative: rejected because it splits signal and duplicates budgets.
- Cost cap from day one: rejected because a tight unproven cap can prevent delivery and lengthen learning.

**Primary references**: [Advantage+ campaign budget](https://www.facebook.com/business/ads/meta-advantage-plus/budget), [ad-set consolidation](https://www.facebook.com/business/ads/ad-set-structure), [Meta bid strategy guide](https://www.facebook.com/business/m/one-sheeters/facebook-bid-strategy-guide).

## 9. Creative portfolio and evidence

**Decision**: use existing Page posts and project images/videos, generate only approved copy and format-preserving variants, and create unpublished click-to-WhatsApp creatives when an existing post cannot express the required CTA. Creative eligibility includes offer facts, rights, format, policy, brand, Page/identity compatibility and destination compatibility.

Recommendation is a pre-spend filter, not a fabricated winner score. Paid status comes only from a controlled experiment with mature outcome evidence. Maintain diversity by concept, hook, format and message rather than selecting six near-duplicates. Native vertical assets are preferred for eligible story/reel inventory.

**Rationale**: the current ranking is mainly freshness and the automated test selects at most two Page videos, despite many images and videos being available.

**Primary references**: [Meta ad creative guidance](https://www.facebook.com/business/ads/ad-creative), [Reels ads guidance](https://www.facebook.com/business/ads/facebook-instagram-reels-ads), [Advantage+ creative](https://www.facebook.com/business/ads/meta-advantage-plus/creative).

## 10. Provider operations, validation and reconciliation

**Decision**: add durable `ProviderOperation` and `ProviderObjectSnapshot` records beneath an execution command. Every plan uses a deterministic fingerprint and client operation key. Workflow:

1. Resolve the current connection, envelope, offer, audience, creative and capability versions.
2. Compile normalized provider payloads with no secrets.
3. Run provider validation-only where supported and persist field-level warnings/errors.
4. Create campaign, ad set, creative and ad paused in dependency order.
5. After every response, read back the object and normalize critical fields.
6. Compare planned versus effective fields and parent links with field-level invariant/equivalence rules.
7. Mark campaign/ad set/ad `VerifiedPaused` and creative `Verified` only when all critical fields are equivalent. Full JSON/hash equality is diagnostic, not an activation requirement.
8. Keep partial/unknown objects paused, reconcile before retry, and open an actionable incident. Never blind-retry a mutation and never auto-delete a campaign.

Activation is a separate command that rechecks capability, destination, review/effective status, plan hash, budget, tracking and stop state immediately before enabling delivery.

**Rationale**: Meta request acceptance and object existence are different from a complete deliverable hierarchy. This also makes partial failures recoverable without duplicate spend objects.

## 11. WhatsApp advertising referral capture

**Decision**: use the documented WhatsApp Cloud API Messages-webhook `referral.ctwa_clid` as the supported production source, including an eligible coexistence configuration when available. Preserve the existing Baileys gateway as an experimental internal-attribution adapter: inspect `ContextInfo.externalAdReply.ctwaClid`, but require a real configured-account fixture before marking it observed. The installed optional field proves only that decoding is possible, not that WhatsApp will populate it.

Both adapters emit one minimized observation shape. Undocumented `ctwaPayload` or `conversionData` is never decrypted, reverse-engineered or promoted to a click ID; only an opaque hash may be retained. The Cloud endpoint validates raw-body `X-Hub-Signature-256`, resolves `phone_number_id` through a globally unique server-side route and publishes a durable inbound event to Conversations. Conversations emits `WhatsAppAttributionObserved.v1` for the first message regardless of state and for every later message carrying referral/CTWA/opaque markers. Only a valid identifier creates a touch. Missing/opaque observations make referral coverage measurable and can force `Unsafe/WAIT`.

**Rationale**: Meta documents `ctwa_clid` on the official Messages webhook and requires it for WhatsApp Business Messaging events. The current gateway discards context, while Baileys behavior is not a documented Meta integration guarantee. Treating an optional type or opaque payload as proof would create false attribution.

**Alternatives considered**:

- Attribute by customer phone or time proximity: rejected because it fabricates ad attribution.
- Make Advertising write directly to Conversation tables: rejected by the modular-monolith constitution.
- Decode undocumented opaque conversion payloads: rejected because the format/authority is unsupported and unstable.

**Primary references/evidence**: [Meta Business Messaging CAPI](https://developers.facebook.com/docs/marketing-api/conversions-api/business-messaging/) and the official [WhatsApp webhook example](https://www.postman.com/meta/whatsapp-business-platform/request/g7sv9jo/received-message-triggered-by-click-to-whatsapp-ads); installed Baileys `WAProto.proto`/typings are implementation evidence for the experimental adapter only.

## 12. Conversions API for Business Messaging

**Decision**: only after supported Cloud API/coexistence and referral proof, use `POST /{dataset-id}/events` on the pinned Graph version. Send one top-level `data[]` array; each event contains:

- `action_source=business_messaging`
- `messaging_channel=whatsapp`
- `user_data.whatsapp_business_account_id`
- `user_data.ctwa_clid`
- stable event ID, event name/time, currency/value where applicable

The provider mapping is an explicit pinned-version allowlist: qualified sales state -> `QualifiedLead`; checkout intent -> `InitiateCheckout`; created order -> `OrderCreated`; verified payment -> `Purchase`; cancelled order -> `OrderCanceled`; actual return -> `OrderReturned`; delivered order -> `OrderDelivered`. Conversation start, booking, refund and other internal outcomes remain internal unless the current official contract exposes an exact truthful mapping; they are never relabeled merely to send them. A chat start is never promoted to a qualified lead or purchase. Website/app conversions use their applicable CAPI path instead of the messaging contract.

Readiness may add top-level `test_event_code`. Persist one delivery identity plus child attempts, payload hash, provider request/trace ID, `events_received`/accepted count, warnings and terminal/non-terminal error category. Re-evaluate consent and capability before every retry. Verify test and production events in Events Manager during readiness.

**Rationale**: the current adapter sends `action_source=system_generated`, omits WhatsApp attribution and sends no WABA/`ctwa_clid`, so Meta cannot learn from the real messaging outcome.

**Primary reference**: [Conversions API for Business Messaging](https://developers.facebook.com/docs/marketing-api/conversions-api/business-messaging/), [general Conversions API](https://www.facebook.com/business/help/AboutConversionsAPI), [official WhatsApp webhook example](https://www.postman.com/meta/whatsapp-business-platform/request/g7sv9jo/received-message-triggered-by-click-to-whatsapp-ads).

## 13. Canonical outcomes, attribution and corrections

**Decision**: separate source-event identity from canonical business identity. Deduplicate source delivery on `(ProjectId, SourceSystem, ExternalEventId)`, then merge cross-source representations with a canonical business key based on business aggregate/order/payment identity, not source name. Preserve every source event and strength.

Internal attribution searches the same protected customer journey across conversations, selects the last eligible WhatsApp referral inside the visible window by touch time and stable-ID tie-breaker, and preserves all touches. Meta-reported attribution remains separate. Missing referral means `Unattributed`, never inferred success. Truth, attribution, correction and delivery states remain independent. Source events retain normalized event/time/value/currency/consent evidence for replay. Corrections create append-only adjustments for refund, cancellation, chargeback, absence, churn and lost deal; they never overwrite history silently.

Qualified-message creation requires an explicit closed classification with confidence/evidence and an applicable sales taxonomy. A consumer must not turn every received classification event into `QualifiedLead`.

**Rationale**: the current canonical key includes source, internal outcomes omit advertisement identity and the qualified-message consumer ignores the classification value.

## 14. Outcome hierarchy and evidence maturity

**Decision**: calculate one coherent evaluation window per decision and rank:

1. Attributed net paid value or contribution after refunds/cancellations when reliable.
2. Verified paid order/purchase count.
3. Verified booking or qualified WhatsApp lead.
4. New messaging conversation as an explicitly labeled fallback.

Clicks and engagement remain diagnostics only. Each goal has minimum elapsed time, minimum spend relative to a configured business target, minimum attributed sample, attribution coverage, correction lag and learning-state gates. Sparse or delayed evidence produces `WAIT`. The target cost/value and capacity come from authorized offer economics; missing margin is shown as unknown, not invented from budget.

Use confidence intervals or Bayesian shrinkage for comparisons instead of exact-looking AI percentages. An experiment must identify control, single primary variable, hypothesis, maturity rule, attribution window and stop rule before spend.

**Rationale**: the current loser rule is protected by any raw messaging count, the winner rule requires revenue even for non-revenue goals and the dashboard displays long unsupported percentage scores.

## 15. End-to-end autonomous decision system

**Decision**: retain deterministic screening, Strategist, independent Auditor and final Safety Engine, but expand the closed action catalog:

- Create/validate/activate a campaign plan.
- Repair or quarantine provider drift.
- Start/stop a creative or audience-suggestion experiment; placement work is observational delivery/format analysis while retaining all live-eligible Advantage+ inventory. Only an explicit envelope brand-safety exclusion may narrow inventory.
- Replace creative.
- Reserve, reallocate, release, increase or decrease budget.
- Pause/resume owned delivery.
- Promote a mature winner or retire a mature loser.
- Change to a validated optimization goal after signal gates.
- Wait or escalate with exact reason codes.

The AI selects only server-provided entity IDs and bounded options. It cannot author provider IDs, destinations, prices, policy categories, budgets outside the envelope or unsupported targeting fields. Every no-change/failure contains machine-readable reason codes and evidence thresholds. Impact review compares a stored baseline and matured after-window, not merely whether insight rows exist.

**Rationale**: the current action set largely scales or pauses, repeated `NoChange` has generic prose and impact review approves whenever any later snapshot exists.

## 16. Tracking, spend and job cadence

**Decision**: continue project-scoped Hangfire dispatch plus Redis lease and durable time-bucket record, with these corrected responsibilities:

- 5 minutes: pull the freshest observable managed spend and forecast cap risk. A cached 30-minute snapshot does not satisfy the spend guard.
- 10 minutes: provider state and plan-drift reconciliation.
- 15 minutes: incremental insights and attribution/tracking health.
- Hourly: deterministic eligibility and decision cycle.
- 2 hours: due impact reviews with maturity checks.
- 6 hours: fatigue evaluation.
- Daily at 04:00 in the active IANA project timezone: next-period ledger and portfolio allocation.
- Every 1 to 3 days: propose a test only when budget and sample capacity exist, not every 5 minutes.
- Monday 05:00 in the active IANA project timezone: offer, goal, audience and strategy review.

Each run resolves local time/DST from the envelope's validated IANA timezone and claims a UTC bucket. Budget reservation debits every applicable daily and monthly/total ledger atomically using `max(observed + delayed estimate, committed + delta) <= usable cap`. Tracking health is calculated from all first-conversation observations, valid/opaque/missing identifiers, exact-match rate (eligible in-thread outcomes with exact ctwa/WABA/destination touch divided by all eligible in-thread outcomes), attributable conversation coverage, source-event freshness, Business Messaging delivery acceptance, Dataset/WABA health, dedupe conflict rate, event delay and correction rate. Thresholds are versioned per goal; insufficient denominator is unknown. `Unsafe` MUST activate `TrackingUnsafe` Emergency Stop. Jobs may recover an incident only when the measured cause is healthy.

**Rationale**: the current tracking job closes incidents without measuring these signals and the test dispatcher runs every five minutes despite a six-hour lease.

## 17. HTTP and UI contract strategy

**Decision**: expose resource-shaped APIs for strategy, plans, provider validation, audiences, experiments, outcomes, decisions and health. The Meta OAuth callback is global and derives project/user solely from single-use server-side state; all project APIs remain JWT/member scoped. Human plan endpoints are operator controls, while activated Autopilot invokes the same services internally without per-action approval. The public Cloud webhook has challenge, signature, global route, dedupe and durable-ack contracts. Webhook source rotation/revocation, Business Messaging test/readiness, complete Advertising audit and a monotonic UI change cursor are explicit. Disconnect/revoke defaults to protective pause/reconcile before credential/routing disposal; LeaveRunning or force revoke requires explicit continuing/unmonitored-spend evidence. Every list supports stable cursor pagination. Every operational response includes `asOfUtc`, timezone, reporting window, attribution window, currency, source-of-truth labels and freshness. Errors use typed stage/object/field/provider details and a correlation ID.

The product-register UI is a dark, high-density command center, not a card wall:

- Persistent control strip: Autopilot, real-spend state, WhatsApp destination, cap remainder, tracking health and Emergency Stop.
- Readiness rail with exact blocking steps.
- URL-addressable Strategy, Overview, Campaigns, Audiences, Creatives, Experiments, WhatsApp Outcomes, AI Decisions and Settings views.
- Plan-versus-effective configuration inspector for each provider object.
- Outcome funnel and allocation views use one coherent time window.
- Dense tables remain for operations but use progressive row detail, readable precision, skeletons, empty-state guidance and per-stage failures.
- Keyboard shortcuts, visible focus, Arabic RTL, 375/768/1024/1440 responsive structures and reduced motion.

Use the existing restrained navy/cyan product system. Magenta is reserved for urgent human attention; destructive emergency control uses the semantic error role. No gradients, glass, hero metrics, repeated identical cards or modal-first setup.

**Rationale**: the current monolithic page mixes time windows, clears the page during refresh, exposes raw tables and cannot explain creation, audience or experiment state.

## 18. Testing, rollout and operational proof

**Decision**: automated tests never use live spend. Expand the fake Meta handler into a scenario provider that records normalized requests and can simulate validation errors, partial hierarchy creation, timeouts, read-back drift, account capability differences, rejected review and delayed insights.

Required test layers:

- Unit: plan compiler, targeting controls/suggestions bounded by the envelope, destination invariants, objective fallback, special-category rules, multi-period budget ledger, maturity, cross-conversation attribution, dedupe, consent/correction and decision safety.
- Provider contract: exact request/read-back normalization for image, video, existing post, clone and replacement paths.
- Gateway/webhook: official Cloud API referral fixtures plus Baileys present/opaque/missing wrappers; opaque payloads are never decoded and all raw identifiers stay out of logs.
- Backend integration: present/opaque/missing observation to attribution context to canonical outcome to Business Messaging delivery/suppression.
- API: tenant/role/idempotency/concurrency, coherent reporting windows and typed provider errors.
- Async AI: request/completion events let the AI module resolve project Gemini credentials internally; no key appears in Advertising state/events.
- Audit: PostgreSQL record/outbox to Elasticsearch index with retry, stale-index status and authoritative-record preservation.
- Frontend: route-state, project switch without stale data, refresh without blanking, readiness, drift inspector, experiment states, Emergency Stop and accessibility.
- Regression: conversations, CRM, group booking/payment/attendance, media, settings and WhatsApp gateway.

Rollout sequence:

1. Migrate new data, register outbox handlers/projections and deploy read-only capability/observation capture with Autopilot disabled.
2. Configure supported Cloud API/coexistence and pass a real-account `referral.ctwa_clid` go/no-go fixture; optional Baileys type presence never passes this gate.
3. Verify a Business Messaging Dataset test event and response in Events Manager.
4. Enable plan compilation, validation and paused provider creation against the fake provider.
5. Validate one real paused hierarchy and field-level read-back with no activation.
6. Run normal-disable, Emergency Stop and unknown-result drills.
7. Run a guarded real-spend canary inside the existing envelope.
8. Enable mature autonomous budget and experiment actions only after attribution and stop drills pass.

**Rationale**: the user granted bounded full autonomy, but full autonomy depends on truthful signal and recoverable provider operations. Canary rollout is not a permanent approval queue or shadow product.

## Resolved planning unknowns

- The specification has no unresolved clarification marker.
- Destination is always WhatsApp.
- Placement policy is dynamic Advantage+ eligibility selected by the user.
- Existing media plus generated copy/format variants remain the creative scope.
- Existing campaigns remain read-only until explicitly imported.
- Owner/Admin authorizes offer-to-WhatsApp pairs and customer-data audience sources; the AI selects and operates inside that envelope.
- Production Business Messaging optimization requires official Cloud API/coexistence referral proof; Baileys-only observation is explicitly experimental/internal.
