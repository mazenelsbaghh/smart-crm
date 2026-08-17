# Feature Specification: Autonomous Facebook Ads Manager

**Feature Branch**: `033-facebook-ads-manager`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Build a project-isolated autonomous Facebook Ads Manager inside the existing Smart Sales shell. Start with Facebook placements only. Use the project's current knowledge base, CRM, conversations, bookings, payment and attendance outcomes. Create and run multiple ads from eligible Facebook posts, images, videos and project assets, recommend the best creatives, distribute a user-approved daily cap across tests, winners and retargeting, optimize for the deepest reliable business conversion, and operate safely through independent review, deterministic safeguards, conversion deduplication, scheduled monitoring and an emergency stop."

## Clarifications

### Session 2026-08-17

- Confirmed that the first release supports Facebook placements only and must not activate Instagram, Messenger, Audience Network, Threads, Google, YouTube or TikTok placements.
- Confirmed that the feature appears as a separate shell destination named `مدير الإعلانات`; the existing campaign area remains for WhatsApp outreach and is relabeled `حملات واتساب` to prevent confusion.
- Confirmed that the system may create and operate multiple ads, use eligible page posts, images and videos, recommend the strongest candidates, distribute the approved daily cap, continue winners, pause proven losers and replace fatigued creatives.
- Confirmed that Autopilot performs real-spend actions after connection and tracking readiness checks, without a non-spend shadow period, but begins with a guarded real-spend canary allocation.
- Confirmed that the user grants one bounded financial and operating authorization; actions inside it may execute autonomously, while actions outside it require a new authorization.
- Q: ما نطاق مصادر التحويل التي يجب أن يدعمها الإصدار الأول فعليًا؟ → A: يستخدم الإصدار الأول CRM والحجوزات والدفع والحضور الموجودة، ويقبل Webhooks عامة وآمنة للاشتراك والشراء والتجديد والـRefund من الأنظمة الخارجية، من دون بناء أنظمة Native كاملة للاشتراكات أو المتاجر داخل هذه الميزة.
- Q: عندما لا توجد مادة إعلانية مناسبة، إلى أي مدى يجب أن ينشئ الإصدار الأول Creative جديدًا تلقائيًا؟ → A: يستخدم الإصدار الأول بوستات Facebook وملفات المشروع الموجودة، ويولد النصوص والـCTA والقص والمقاسات والـThumbnail والنسخ التنسيقية فقط؛ ولا يولد صورًا أو فيديوهات جديدة من الصفر.
- Q: ما سياسة إرسال بيانات مطابقة العميل مع Server-side Conversions؟ → A: ترسل بيانات المطابقة المسموح بها فقط عند وجود Consent State أو أساس قانوني موثق، وإلا تستخدم معرفات النقر والحدث والبيانات غير التعريفية المسموح بها دون إرسال الهاتف أو البريد.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect and authorize Facebook advertising (Priority: P1)

As a project owner or admin, I want to connect the project's Facebook advertising resources and define a bounded daily and total spend authorization, so that Autopilot can manage real ads without gaining access to unrelated projects, platforms, accounts or unlimited spend.

**Why this priority**: No campaign can be created or funded safely until identity, account ownership, placement scope, tracking readiness and financial authority are explicit.

**Independent Test**: Connect an eligible advertising account, select its Facebook Page and measurement source, set a daily cap and allowed offer, pass readiness checks, and verify that Autopilot becomes available only for that project and authorization envelope.

**Acceptance Scenarios**:

1. **Given** an authorized admin and an unconnected project, **When** the admin completes the Facebook advertising connection and selects an eligible account, Page and measurement source, **Then** the project displays the connected resources and their health without exposing secret credentials.
2. **Given** a connected project with no daily cap, no eligible offer or failing conversion tracking, **When** the admin attempts to enable Autopilot, **Then** activation is blocked and the missing readiness items are shown.
3. **Given** an authorization that allows Facebook placements only, **When** any plan or execution requests another Meta placement or another advertising platform, **Then** the request is rejected before spend occurs.
4. **Given** two projects connected to different advertising resources, **When** either project is opened or processed, **Then** it can use only its own knowledge, conversions, creatives, decisions, account and budget.

---

### User Story 2 - Build a launch plan from project knowledge and assets (Priority: P1)

As a project owner or admin, I want the system to understand the selected product, service, course, subscription, booking or event from the project's existing knowledge base and recommend a launch plan, so that I do not have to manually design campaign structure, targeting, copy and conversion strategy.

**Why this priority**: Autonomous management is valuable only when the offer, claims, audience, economics and conversion path come from trusted project facts rather than invented content.

**Independent Test**: Select an offer whose price, audience, locations, claims and restrictions exist in approved project knowledge, and verify that the proposed plan uses those facts, identifies their sources and refuses unsupported prices, discounts or claims.

**Acceptance Scenarios**:

1. **Given** an approved knowledge base describing one eligible offer, **When** the admin starts a promotion, **Then** the system presents a sourced offer summary, funnel, recommended conversion event, audience, budget allocation and campaign plan.
2. **Given** multiple eligible offers, **When** the admin starts a promotion, **Then** the admin must select the offer to advertise before real spend is enabled.
3. **Given** missing or contradictory price, location, landing destination or prohibited-claim information, **When** the system evaluates readiness, **Then** it blocks launch and identifies the unresolved facts instead of inventing them.
4. **Given** a knowledge document changes after planning, **When** the active advertising profile becomes stale, **Then** new creative or campaign creation pauses until the affected facts are refreshed and revalidated.

---

### User Story 3 - Create and test multiple Facebook ads (Priority: P1)

As a project owner or admin, I want Autopilot to find eligible Facebook Page posts and project media, recommend the best images and videos, and create a budget-appropriate set of ads, so that several creative approaches can be tested without fragmenting a small budget.

**Why this priority**: Creative selection and controlled experimentation are required to discover winners; a single advertisement cannot provide a reliable autonomous buying loop.

**Independent Test**: Provide eligible image posts, video posts and project assets, set a small or medium daily cap, and verify that the system ranks candidates with reasons, creates only as many tests as the cap can support, and runs them on Facebook placements only.

**Acceptance Scenarios**:

1. **Given** eligible Page posts and project assets, **When** a launch plan is created, **Then** each candidate is scored for offer relevance, policy and brand safety, format suitability, freshness and available historical evidence, and the strongest candidates are explained to the admin.
2. **Given** both image and video candidates, **When** the promotion launches, **Then** Autopilot may operate standard Facebook image, carousel and video advertisements, including eligible Facebook feed, story, video and reel placements, without enabling corresponding Instagram placements.
3. **Given** a daily cap too small to support every candidate, **When** tests are allocated, **Then** the system reduces the number of simultaneous advertisements rather than spreading the cap too thinly.
4. **Given** an ineligible, expired, rights-restricted, unsupported or policy-rejected post or asset, **When** candidates are evaluated, **Then** it is excluded with a visible reason and receives no spend.
5. **Given** an existing Page post is selected, **When** the advertisement is created, **Then** the source post identity is preserved and reported as an existing-post creative.
6. **Given** no suitable existing Page post or project media is available, **When** a launch is requested, **Then** the system blocks the affected creative launch and explains what source media is missing instead of generating a new image or video from scratch.
7. **Given** an eligible existing image or video, **When** the system prepares it for selected Facebook placements, **Then** it may generate copy, calls to action, crops, dimensions, thumbnails and format-preserving variants without changing the offer or materially fabricating the media.

---

### User Story 4 - Allocate the daily cap and scale proven winners (Priority: P1)

As a project owner or admin, I want Autopilot to distribute one approved project daily cap across prospecting, creative tests, audience tests, retargeting and proven winners, so that the best business outcomes receive more spend without exceeding my authority.

**Why this priority**: This is the core real-money behavior and must remain predictable, bounded, measurable and reversible.

**Independent Test**: Set one daily cap, run multiple managed advertisements with different business outcomes, and verify that the sum of protected allocation and recorded spend stays within the authorized boundary while budget gradually moves toward sufficiently proven winners.

**Acceptance Scenarios**:

1. **Given** a project daily cap and multiple eligible advertisements, **When** Autopilot launches, **Then** it reserves a safety buffer and allocates the remaining usable amount across the smallest viable test and delivery structure.
2. **Given** one advertisement has stronger paid, subscribed, attended or qualified outcomes with sufficient evidence, **When** the next eligible decision window occurs, **Then** its allocation may increase gradually within the maximum authorized increase.
3. **Given** an advertisement has weak results but insufficient data or an incomplete attribution window, **When** an AI proposal requests a pause, **Then** the decision resolves to `WAIT` and spend is not removed solely from short-term noise.
4. **Given** an advertisement is proven unprofitable or policy-rejected, **When** safety and evidence checks pass, **Then** it is paused without deletion and its released allocation may be reassigned.
5. **Given** the protected daily or total limit is reached or forecast to be breached, **When** monitoring detects the condition, **Then** managed delivery is stopped or constrained before additional autonomous increases can occur.
6. **Given** the retargeting audience cannot use its reserved allocation efficiently, **When** the allocator reviews the day, **Then** it may return unused allocation to eligible tests or winners while preserving the hard cap.

---

### User Story 5 - Optimize for reliable business conversions (Priority: P1)

As a project owner or admin, I want advertising decisions to use verified payments, subscriptions, enrollments, bookings, attendance, CRM outcomes and qualified conversations, so that cheap clicks, registrations or unqualified messages do not appear successful when they fail to produce business value.

**Why this priority**: Closed-loop business measurement is the feature's main differentiation and the basis for safe optimization.

**Independent Test**: Attribute a visitor through lead, payment and attendance states, including a duplicate delivery and a later refund or absence, and verify that each canonical outcome is recorded once, linked to the correct project and advertisement, and reflected in later decisions.

**Acceptance Scenarios**:

1. **Given** a confirmed server-side payment, subscription, enrollment, booking or attendance event, **When** it is received, **Then** it is recorded once with its value, currency, source and advertising attribution and is delivered for Facebook measurement.
2. **Given** the same customer event arrives from browser and server sources, **When** both copies share the same canonical identity, **Then** reporting and optimization count one conversion.
3. **Given** a refund, cancellation, chargeback, absence or lost deal, **When** the negative outcome is confirmed, **Then** the original business value is adjusted and future decisions use the corrected result.
4. **Given** deeper outcomes are reliable but too sparse or delayed for stable optimization, **When** the conversion eligibility check runs, **Then** the system continues using an eligible upper-funnel event or expected value rather than switching prematurely.
5. **Given** a campaign produces cheap signups or messages but poor payment, attendance or retention outcomes, **When** sufficient downstream data exists, **Then** it is ranked below a campaign producing stronger long-term value.
6. **Given** a service conversation, **When** it is classified as spam, support, unqualified, qualified, booking intent, purchase intent or confirmed payment, **Then** only the applicable qualified sales state contributes to qualified-message cost.

---

### User Story 6 - Review autonomous decisions and intervene safely (Priority: P2)

As an owner, admin, supervisor or reviewer, I want to understand every proposed and executed decision, its evidence, independent review, safety result and measured impact, so that autonomous spend is explainable and reversible.

**Why this priority**: Financial autonomy requires transparent evidence, clear role boundaries and durable audit history.

**Independent Test**: Trigger an increase, an insufficient-data pause proposal and a large out-of-envelope change, then verify the first executes once within authority, the second waits, and the third requires authorized intervention.

**Acceptance Scenarios**:

1. **Given** a proposed financial action, **When** it is evaluated, **Then** the decision record shows the strategy, evidence window, statistical eligibility, auditor result, judge result when required, safety result, execution status and planned evaluation time.
2. **Given** a command is retried after a timeout or worker restart, **When** the same command identity is encountered, **Then** the external financial mutation occurs at most once.
3. **Given** an expected campaign or budget state changed manually before execution, **When** the command runs, **Then** it is marked stale, not applied blindly, and the latest Facebook state is synchronized.
4. **Given** an action exceeds the authorized envelope, **When** review completes, **Then** no execution occurs until an authorized user approves a new boundary.
5. **Given** a reviewer opens decision history, **When** an outcome window has completed, **Then** the decision is labeled positive, negative, inconclusive or reverted using the actual observed outcome.

---

### User Story 7 - Monitor health and stop safely (Priority: P2)

As a project owner or admin, I want continuous spend, tracking, connection and delivery health monitoring with an emergency stop, so that abnormal spend, expired authorization, broken attribution or cross-project inconsistency cannot continue unattended.

**Why this priority**: Safe failure behavior protects the advertiser when dependencies or tracking fail.

**Independent Test**: Simulate tracking loss, expired Facebook authorization, abnormal spend and an emergency-stop request, and verify that financial changes freeze, managed advertisements pause when required, alerts appear and no campaign is deleted.

**Acceptance Scenarios**:

1. **Given** conversion tracking is unhealthy, **When** a financial decision cycle begins, **Then** financial modifications are frozen and a visible tracking incident is created.
2. **Given** Facebook authorization expires or required permissions disappear, **When** execution is attempted, **Then** new commands are blocked and the admin is instructed to reconnect.
3. **Given** abnormal spend or an imminent hard-cap breach, **When** monitoring detects it, **Then** Emergency Stop activates for system-managed advertisements and records the reason.
4. **Given** an authorized admin activates Emergency Stop, **When** managed advertisements are running, **Then** Autopilot stops, managed delivery pauses without deletion, pending commands are cancelled or blocked, and explicit admin action is required to resume.
5. **Given** a user manually disables Autopilot without an emergency, **When** the change is saved, **Then** no new autonomous decisions execute and the user is shown the safe state in which existing managed advertisements will remain.

---

### User Story 8 - Operate from one clear shell workspace (Priority: P3)

As an operations user, I want a dedicated `مدير الإعلانات` destination beside the existing communication tools, so that Facebook advertising is distinct from WhatsApp campaigns but shares the active project and familiar navigation.

**Why this priority**: Clear information architecture prevents users from confusing paid advertising with outbound WhatsApp campaigns.

**Independent Test**: Navigate on desktop and mobile, switch active projects, and verify that the dedicated workspace loads the correct project's overview, campaigns, creatives, conversions, decisions and settings while the legacy area is labeled `حملات واتساب`.

**Acceptance Scenarios**:

1. **Given** an authenticated user on desktop or mobile, **When** the shell opens, **Then** it displays `مدير الإعلانات` as a dedicated destination and `حملات واتساب` as the existing outreach destination.
2. **Given** an unconfigured project, **When** the user opens `مدير الإعلانات`, **Then** a readiness checklist explains connection, offer, tracking, budget and activation steps instead of showing an empty dashboard.
3. **Given** a configured project, **When** the workspace opens, **Then** it provides Overview, Campaigns, Creatives, Conversions, AI Decisions and Settings views with current health and emergency controls.
4. **Given** the active project changes, **When** the workspace reloads, **Then** all visible advertising data, commands and recommendations switch to the newly active project without retaining the previous project's data.

## Edge Cases

- A Page is connected for messages and comments but the user has not granted advertising permissions or selected an advertising account.
- An advertising account is disabled, under review, out of funds, has billing failure, uses an unexpected currency or no longer permits the connected user.
- A Page post was edited, deleted, expired, shared from another source, contains restricted media rights or becomes ineligible after selection.
- A video is unsuitable for the intended Facebook placement because of dimensions, duration, rights, missing source file or processing failure.
- A connected Page or measurement source belongs to a different project or advertising account than the selected authorization.
- The project daily cap is below the minimum viable amount for more than one test; the system must reduce simultaneous tests and explain the limitation.
- Spend reporting is delayed; the allocator must preserve a safety reserve and must not treat locally allocated budget as actual spend.
- Several workers, retries or decision cycles attempt the same budget mutation concurrently.
- The user edits a managed campaign directly in Facebook between decision and execution.
- A payment event arrives before its visitor or lead attribution record, or arrives after the reporting window.
- A conversion has valid business value but no recorded consent or legal basis for sending customer matching identifiers; the event remains in the project ledger and uses only permissible non-identifying attribution data for external delivery.
- The same conversion arrives from browser, server, CRM and payment-provider sources with incomplete identifiers.
- The customer changes devices or enters through a conversation destination without a standard website click identifier.
- A refund, cancellation, absence or churn event arrives after a campaign was initially judged successful.
- A project is deleted, archived or loses its knowledge base while managed advertisements are active.
- All candidate creatives are rejected or unsupported; no advertisement may launch from fabricated or unverified content.
- Retargeting audience volume is too small to spend its planned allocation.
- The independent reviewer or strategist is unavailable; financial action waits rather than bypassing required review.
- The system restarts during an external request whose result is unknown; reconciliation occurs before retry.
- Facebook accepts a request but later reports a rejected, pending or different effective state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a dedicated project-level workspace named `مدير الإعلانات` in desktop and mobile shell navigation.
- **FR-002**: The existing outreach campaign destination MUST be labeled `حملات واتساب` and remain functionally separate from paid advertising.
- **FR-003**: The first release MUST support Facebook placements only and MUST reject Instagram, Messenger, Audience Network, Threads, Google, YouTube and TikTok delivery.
- **FR-004**: Only Owner and Admin roles MUST be able to connect advertising resources, define financial authority, activate Autopilot, resume after Emergency Stop or approve an out-of-envelope change.
- **FR-005**: The system MUST associate every advertising connection, offer, creative, insight, conversion, decision, command, incident and budget record with exactly one Project.
- **FR-006**: The system MUST verify that selected advertising account, Page and measurement resources are mutually eligible before allowing activation.
- **FR-007**: Secret advertising credentials MUST never be displayed after connection or included in user-visible logs and exported decision evidence.
- **FR-008**: The system MUST display connection status, granted capability status, expiration or reconnection status, last successful synchronization and actionable failure details.
- **FR-009**: The system MUST require a user-approved autonomy envelope containing the selected project, offer, Facebook resources, daily cap, total or monthly cap, currency, allowed locations, time boundary and maximum autonomous increase.
- **FR-010**: The system MUST block any action outside the active autonomy envelope until a permitted user grants a new boundary.
- **FR-011**: The system MUST obtain advertising facts from the project's existing approved or published knowledge and MUST NOT create an alternative editable knowledge base.
- **FR-012**: The system MUST produce a sourced advertising profile that records the origin, version, confidence and refresh time of offer, price, audience, location, claim, restriction, destination and brand guidance facts.
- **FR-013**: The system MUST block launch when required commercial facts are missing, contradictory, stale or below the accepted confidence level.
- **FR-014**: The system MUST never invent or silently alter prices, discounts, guarantees, schedules, availability, claims or prohibited wording.
- **FR-015**: The user MUST select the advertised offer when more than one eligible offer exists.
- **FR-016**: The system MUST infer and present a suitable business funnel for SaaS, courses, products, services, bookings and events using available project facts and outcomes.
- **FR-017**: The system MUST support eligible existing Facebook Page image, carousel and video posts as advertisement sources.
- **FR-018**: The system MUST support eligible project image and video assets as advertisement sources and retain their project ownership and source identity.
- **FR-019**: The system MUST explain creative recommendations using offer relevance, brand and policy compatibility, format suitability, freshness, organic evidence when available and prior paid evidence when available.
- **FR-020**: The system MUST exclude ineligible, unsupported, expired, unavailable, rights-restricted, stale-offer or policy-rejected creatives from spend.
- **FR-021**: The system MUST choose a simultaneous creative-test count that the authorized usable daily cap can support and MUST reduce test count when evidence would otherwise be fragmented.
- **FR-022**: The system MUST support Facebook-only image, carousel and video delivery, including eligible Facebook feed, story, video and reel placements.
- **FR-023**: The system MUST track each creative's source, version, offer facts, placement eligibility, delivery state, spend, impressions, frequency, engagement, business outcomes and fatigue status.
- **FR-023A**: The first release MUST generate advertising copy, headlines, descriptions, calls to action, crops, dimensions, thumbnails and format-preserving variants from eligible existing Page posts and project media.
- **FR-023B**: The first release MUST NOT generate finished new source images or videos from scratch; when no eligible source media exists, it MUST block that creative path and request suitable project media.
- **FR-024**: The system MUST maintain one authoritative daily and total budget ledger for all advertisements it manages within a project authorization.
- **FR-025**: The usable daily allocation MUST preserve a configurable safety reserve below the hard daily cap to account for reporting and delivery delay.
- **FR-026**: The allocator MUST distribute usable budget among the smallest viable combination of prospecting, creative tests, audience tests, retargeting and proven winners instead of enforcing fixed percentages regardless of evidence.
- **FR-027**: The system MUST prevent concurrent campaigns or workers from reserving the same available budget twice.
- **FR-028**: Budget increases MUST be gradual, respect the maximum authorized increase and cooling period, and be blocked while tracking, connection or account health is unsafe.
- **FR-029**: The system MUST NOT pause or reduce an advertisement solely because of insufficient, delayed or statistically inconclusive short-term data.
- **FR-030**: Proven losing or rejected advertisements MAY be paused but MUST NOT be permanently deleted by Autopilot.
- **FR-031**: The system MUST rank performance primarily by the deepest reliable business outcomes and value, including verified payment, subscription, renewal, enrollment, attendance, booking, won customer and qualified lead outcomes.
- **FR-032**: The system MUST treat clicks, visits, conversations, leads and signups as interim outcomes when stronger reliable outcomes are unavailable, not as unconditional final success.
- **FR-033**: The system MUST evaluate event volume, tracking quality, match quality, delay, correction rate, learning state and business value before changing the active optimization event.
- **FR-034**: The system MUST record a canonical conversion once while preserving its source events, value, currency, customer or visitor identity, occurrence time, attribution evidence and delivery status.
- **FR-035**: The system MUST deduplicate equivalent browser, server, payment, CRM, booking and attendance copies of the same business event.
- **FR-036**: The system MUST support negative adjustments for refunds, cancellations, chargebacks, churn, absence and lost deals and use them in later evaluation.
- **FR-037**: The system MUST connect project CRM qualified states, won deals, conversations, group-booking payment and attendance outcomes to the advertising conversion ledger without treating unverified classification as confirmed payment.
- **FR-037A**: The first release MUST accept authenticated, project-scoped external business events for signup, trial, subscription, renewal, enrollment, purchase, refund and cancellation through a documented generic conversion-ingestion contract.
- **FR-037B**: External conversion ingestion MUST validate event identity, source, occurrence time, project, event type, value and currency where applicable, and MUST reject unauthenticated, cross-project, unsupported or malformed events.
- **FR-038**: The system MUST visibly distinguish confirmed first-party business truth from platform-reported results, CRM inference and AI conversation classification.
- **FR-038A**: The system MUST record the consent state or documented legal basis governing whether customer matching identifiers may be included in an external conversion delivery.
- **FR-038B**: The system MUST NOT send phone, email or other customer matching identifiers when no applicable consent or legal basis is recorded; it MAY still deliver the business event using permissible event, click and non-identifying data.
- **FR-038C**: Revocation or restriction of consent MUST apply to future deliveries and retries and MUST be visible in the conversion delivery evidence.
- **FR-039**: A strategist MUST propose only a closed set of supported actions with evidence, expected effect, risk, evaluation window and rollback plan.
- **FR-040**: A deterministic evaluator MUST reject or defer actions when data volume, attribution timing, learning state, spend or statistical evidence is insufficient.
- **FR-041**: An independent auditor MUST return `APPROVE`, `REJECT`, `WAIT` or `ESCALATE` and MUST NOT be bypassed for financial decisions.
- **FR-042**: A judge MUST resolve configured high-risk or disputed cases, including large increases, optimization-event changes, new strategy launches and proposals to pause a campaign still producing verified value.
- **FR-043**: A deterministic safety layer MUST validate tenant scope, placement scope, offer facts, budget limits, health, command identity and expected external state immediately before execution.
- **FR-044**: Every external mutation MUST have a durable unique command identity and MUST execute at most once despite retries, concurrency, timeouts or restarts.
- **FR-045**: The system MUST reconcile actual Facebook state after mutation and before retrying an operation whose result is unknown.
- **FR-046**: Decision history MUST show proposal, evidence, evaluation, audit, judge when used, safety outcome, execution, reconciliation, actual impact and rollback status.
- **FR-047**: Decision impact MUST be evaluated after an event-appropriate delay and labeled positive, negative, inconclusive or reverted.
- **FR-048**: The system MUST monitor actual spend at least every five minutes while managed advertising is active.
- **FR-049**: The system MUST synchronize campaign and delivery state at least every ten minutes and performance insights at least every fifteen minutes while managed advertising is active.
- **FR-050**: The system MUST evaluate tracking health at least every fifteen minutes and freeze financial modifications while tracking is unsafe.
- **FR-051**: The system MUST run an hourly eligibility and decision cycle, while allowing `WAIT` with no mutation whenever learning, cooling, evidence, attribution or health gates are not satisfied.
- **FR-052**: The system MUST inspect creative fatigue periodically using sufficient impressions, frequency, engagement decline, conversion decline and cost increase rather than age alone.
- **FR-053**: The system MUST perform daily budget review and weekly strategy review within the project's configured timezone.
- **FR-054**: Real payment, subscription, booking, attendance and correction events MUST be accepted immediately rather than waiting for a scheduled decision cycle.
- **FR-055**: Failed conversion deliveries and external commands MUST use bounded retry and reconciliation without duplicating business events or financial mutations.
- **FR-056**: The system MUST activate Emergency Stop when abnormal spend, hard-cap risk, cross-project mismatch, unsafe tracking, repeated financial commands or lost required authorization is detected.
- **FR-057**: An authorized user MUST be able to trigger Emergency Stop immediately from the advertising workspace.
- **FR-058**: Emergency Stop MUST disable Autopilot, block pending and new commands, pause system-managed delivery when safe to do so, retain all campaign history and require explicit authorized resumption.
- **FR-059**: The system MUST NOT alter unrelated manually managed campaigns unless they have been explicitly imported and assigned to Autopilot ownership.
- **FR-060**: The workspace MUST provide Overview, Campaigns, Creatives, Conversions, AI Decisions and Settings views plus a first-run readiness checklist.
- **FR-061**: Overview MUST display current Autopilot, connection and tracking health, daily cap, actual spend, usable remainder, safety reserve, active/test/paused counts, verified conversions, revenue, return and best and worst sufficiently-evaluated advertisements.
- **FR-062**: Creative views MUST distinguish recommended, testing, winning, fatigued, rejected and paused states and explain why a creative is in that state.
- **FR-063**: All primary workspace controls and status communication MUST be keyboard accessible, readable in right-to-left layout and usable on desktop and mobile.
- **FR-064**: The system MUST keep durable audit evidence for authorization changes, financial actions, emergency actions, connection changes, conversion corrections and cross-project access denials.

### Key Entities *(include if feature involves data)*

- **Advertising Connection**: A project's selected Facebook advertising account, Page and measurement resources, their capability and health status, and protected authorization state.
- **Autonomy Envelope**: The admin-approved financial and operating limits within which Autopilot may act without another human approval.
- **Advertising Profile**: A derived, versioned and sourced interpretation of approved project knowledge for one or more eligible offers; it is not a replacement knowledge base.
- **Advertising Offer**: The selected product, service, course, subscription, booking or event and its verified commercial facts, destination and restrictions.
- **Managed Campaign**: The project-owned representation of a campaign managed by Autopilot, including its external identity, objective, business goal, ownership and lifecycle state.
- **Managed Ad Set**: Audience, placement, schedule, optimization event and budget allocation beneath a managed campaign.
- **Managed Advertisement**: One advertisement and its source creative, copy, call to action, external identity and delivery state.
- **Advertising Creative**: An existing Page post, project image, project video or clarified generated-media output with eligibility, variants, evidence and performance history.
- **Insights Snapshot**: Time-bounded delivery and performance observations for campaign, ad set and advertisement levels.
- **Attribution Touch**: Evidence connecting a visitor, customer or conversation to an advertisement interaction.
- **Canonical Conversion**: One deduplicated business outcome with source-of-truth strength, value, attribution, consent or legal-basis state, platform-delivery and correction history.
- **Budget Ledger and Allocation**: The hard cap, safety reserve, committed allocation, actual spend, released budget and remaining authority for one project and period.
- **AI Decision**: The supported proposal, evidence, evaluation, independent review, safety outcome, execution and later impact assessment.
- **Execution Command**: One idempotent, state-checked external mutation derived from an approved decision.
- **Tracking Incident**: A period in which conversion, connection, billing, spend or project-isolation health is unsafe and financial action is frozen or stopped.
- **Emergency Stop Record**: The reason, initiator, affected managed delivery, blocked commands and explicit resumption history.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An authorized admin can complete connection, offer selection, tracking verification, budget authorization and guarded Autopilot activation in 10 minutes or less when all external resources are eligible.
- **SC-002**: 100% of advertisements created by the first release are restricted to eligible Facebook placements, with zero spend on Instagram, Messenger, Audience Network, Threads, Google, YouTube or TikTok.
- **SC-003**: Across a test with at least two projects, zero advertising facts, credentials, campaigns, creatives, conversions, decisions or budget records from one project are visible or usable by the other.
- **SC-004**: 100% of repeated conversion deliveries with the same canonical identity produce one counted business outcome while retaining their delivery and correction evidence.
- **SC-005**: 100% of repeated financial commands with the same command identity cause at most one external financial mutation.
- **SC-006**: During active managed delivery, abnormal-spend and hard-cap conditions are detected within 5 minutes of becoming observable, and no autonomous increase is permitted while the condition remains active.
- **SC-007**: No autonomous decision exceeds the approved daily cap, total or monthly cap, maximum increase, allowed offer, locations, time boundary or Facebook-only placement scope.
- **SC-008**: When tracking health is unsafe, 100% of new financial modifications are frozen until health is restored or an authorized emergency action is taken.
- **SC-009**: For a daily cap that cannot support all available creatives, the launch uses a reduced viable test set and displays the limitation rather than starting every candidate.
- **SC-010**: Every executed financial decision is discoverable with its evidence, independent review, deterministic eligibility and safety outcomes, exact execution state and later impact assessment.
- **SC-011**: Verified payment, subscription, enrollment, booking, attendance and revenue outcomes appear in the advertising workspace within 60 seconds of successful first-party receipt, excluding external platform reporting delay.
- **SC-012**: An authorized Emergency Stop request blocks new autonomous commands within 10 seconds and preserves all campaign, conversion and decision history.
- **SC-013**: A user can identify the recommended creative, why it was recommended, its source, its current allocation and its strongest reliable business outcome without opening Facebook's advertising interface.
- **SC-014**: Desktop and mobile users can reach `مدير الإعلانات`, switch project context, inspect health and activate Emergency Stop using keyboard-accessible or touch-accessible controls without confusing the workspace with `حملات واتساب`.

## Assumptions

- Existing authentication, project switching and role identities are reused, but advertising authority is additionally restricted to Owner and Admin roles unless explicitly delegated later.
- The first release operates only advertisements explicitly created by or assigned to this manager; unrelated manual campaigns remain outside its control and outside its managed daily-cap ledger.
- The approved daily cap is a hard product boundary. A safety reserve below that cap is used because platform delivery and spend reporting may be delayed.
- A guarded canary uses real spend after connection, offer, policy, measurement and budget readiness checks; there is no zero-spend shadow operation.
- The active project timezone determines daily and weekly business boundaries, with Cairo used by existing projects unless configured otherwise.
- Existing approved or published knowledge is authoritative for commercial facts. Derived advertising profiles are refreshable caches with source evidence, not an alternative content-management surface.
- Confirmed server-side payment and subscription data outrank CRM and AI inference. Attendance and booking confirmation outrank registration, while qualified conversation classification outranks raw message count.
- The project is responsible for collecting and documenting any required customer consent or legal basis; the Ads Manager enforces the recorded state when preparing external conversion deliveries.
- Platform attribution and internal attribution are reported as evidence-based attribution, not proof that advertising caused the outcome.
- Facebook may reject or delay an otherwise valid advertisement; the manager reports and reacts to effective external state rather than assuming request acceptance means delivery.
- External SaaS, commerce, payment and course systems remain the source of truth for their own subscription, order, payment, refund and enrollment lifecycles and send verified changes into the project's conversion ledger.
- The product constitution must be amended or interpreted explicitly to allow pre-authorized autonomous financial actions inside an admin-approved envelope; out-of-envelope actions remain human-approved.

## Out of Scope

- Instagram, Messenger, Audience Network, Threads, Google, YouTube and TikTok advertising delivery in the first release.
- Guaranteed cost per lead, message, sale, subscription, booking or attendee.
- Circumventing advertising policies, account restrictions, review, billing requirements or media rights.
- Permanent deletion of advertising campaigns by Autopilot.
- Autonomous control of unrelated manually managed campaigns without explicit ownership transfer.
- Creating an alternative knowledge base or sharing learning, customers, conversions or assets between projects.
- Generating finished new source images or videos from scratch; the first release transforms eligible existing media and generates advertising text only.
- Inventing prices, offers, discounts, availability, guarantees, claims or target locations not supported by approved project facts.
- Spending without a successful connection test, measurement-readiness test and active bounded authorization.
- Building a native SaaS subscription platform, ecommerce ordering system, payment processor, course platform or refund-management product as part of the Ads Manager; these systems integrate through the generic verified conversion contract.
