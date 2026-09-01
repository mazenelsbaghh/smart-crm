# Feature Specification: Autonomous WhatsApp AI Media Buyer

**Feature Branch**: `033-facebook-ads-manager`

**Created**: 2026-08-17

**Status**: Draft

**Input**: User description: "Repair the existing broken ad creation and targeting flows and turn the manager into a genuinely autonomous AI media buyer that manages Meta advertising end to end. Every advertisement must open WhatsApp. The system must choose the offer, structure, audience, placements, creative, budget, experiments and optimization strategy, validate that each external object was created correctly, learn from qualified conversations and verified business outcomes, and pursue the lowest sustainable cost without presenting clicks or raw messages as business success."

## Clarifications

### Session 2026-08-17

- The original session limited delivery to Facebook placements only; this placement limit is superseded by the 2026-08-18 click-to-WhatsApp requirement below, while its non-Meta exclusion remains.
- Confirmed that the feature appears as a separate shell destination named `مدير الإعلانات`; the existing campaign area remains for WhatsApp outreach and is relabeled `حملات واتساب` to prevent confusion.
- Confirmed that the system may create and operate multiple ads, use eligible page posts, images and videos, recommend the strongest candidates, distribute the approved daily cap, continue winners, pause proven losers and replace fatigued creatives.
- Confirmed that Autopilot performs real-spend actions after connection and tracking readiness checks, without a non-spend shadow period, but begins with a guarded real-spend canary allocation.
- Confirmed that the user grants one bounded financial and operating authorization; actions inside it may execute autonomously, while actions outside it require a new authorization.
- Q: ما نطاق مصادر التحويل التي يجب أن يدعمها الإصدار الأول فعليًا؟ → A: يستخدم الإصدار الأول CRM والحجوزات والدفع والحضور الموجودة، ويقبل Webhooks عامة وآمنة للاشتراك والشراء والتجديد والـRefund من الأنظمة الخارجية، من دون بناء أنظمة Native كاملة للاشتراكات أو المتاجر داخل هذه الميزة.
- Q: عندما لا توجد مادة إعلانية مناسبة، إلى أي مدى يجب أن ينشئ الإصدار الأول Creative جديدًا تلقائيًا؟ → A: يستخدم الإصدار الأول بوستات Facebook وملفات المشروع الموجودة، ويولد النصوص والـCTA والقص والمقاسات والـThumbnail والنسخ التنسيقية فقط؛ ولا يولد صورًا أو فيديوهات جديدة من الصفر.
- Q: ما سياسة إرسال بيانات مطابقة العميل مع Server-side Conversions؟ → A: ترسل بيانات المطابقة المسموح بها فقط عند وجود Consent State أو أساس قانوني موثق، وإلا تستخدم معرفات النقر والحدث والبيانات غير التعريفية المسموح بها دون إرسال الهاتف أو البريد.

### Session 2026-08-18

- Confirmed that the current advertisement-creation and targeting behavior is considered broken and must be replaced rather than treated as a finished foundation.
- Confirmed that every system-created advertisement must use WhatsApp as its only customer destination, regardless of the eligible Meta placement on which it appears.
- Confirmed that the user delegates the complete media-buying lifecycle inside the bounded authorization: research, planning, creation, validation, launch, targeting, experimentation, budget movement, optimization, pausing, replacement, measurement and reporting.
- Confirmed that reducing cost means reducing the cost of the deepest reliable business outcome, not merely reducing link-click, engagement or raw-message cost.
- Q: ما نطاق مواضع Meta التي يستطيع الـAI استخدامها مع بقاء وجهة الإعلان واتساب؟ → A: يستخدم Advantage+ وكل موضع تؤكد Meta وقت التنفيذ أنه مؤهل لفتح واتساب، ولا يعتمد على قائمة مواضع ثابتة قديمة.
- The working default is to rank verified paid value or contribution first, then verified booking or qualified WhatsApp lead, then a new messaging conversation only when stronger outcomes are not yet reliable.
- Confirmed from the end-to-end delegation that the Owner/Admin authorizes a bounded pool of eligible offer-to-WhatsApp-destination pairings and the AI selects autonomously only inside that pool.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Connect and authorize Meta advertising (Priority: P1)

As a project owner or admin, I want to connect the project's Meta advertising and WhatsApp resources and define a bounded daily and total spend authorization, so that Autopilot can manage real ads without gaining access to unrelated projects, platforms, accounts or unlimited spend.

**Why this priority**: No campaign can be created or funded safely until identity, account ownership, placement scope, tracking readiness and financial authority are explicit.

**Independent Test**: Connect an eligible advertising account, select its Page, WhatsApp business destination and measurement source, set a daily cap and allowed offer, pass readiness checks, and verify that Autopilot becomes available only for that project and authorization envelope.

**Acceptance Scenarios**:

1. **Given** an authorized admin and an unconnected project, **When** the admin completes the Facebook advertising connection and selects an eligible account, Page and measurement source, **Then** the project displays the connected resources and their health without exposing secret credentials.
2. **Given** a connected project with no daily cap, no eligible offer or failing conversion tracking, **When** the admin attempts to enable Autopilot, **Then** activation is blocked and the missing readiness items are shown.
3. **Given** an authorization for click-to-WhatsApp advertising, **When** any plan or execution requests a non-WhatsApp customer destination, an ineligible Meta placement or another advertising platform, **Then** the request is rejected before spend occurs.
4. **Given** two projects connected to different advertising resources, **When** either project is opened or processed, **Then** it can use only its own knowledge, conversions, creatives, decisions, account and budget.
5. **Given** a connected account lacks a required advertising, WhatsApp, measurement or optimization capability, **When** readiness is evaluated, **Then** activation is blocked with the exact missing capability and a safe supported fallback when one exists.

---

### User Story 2 - Build a launch plan from project knowledge and assets (Priority: P1)

As a project owner or admin, I want the system to understand the selected product, service, course, subscription, booking or event from the project's existing knowledge base and recommend a launch plan, so that I do not have to manually design campaign structure, targeting, copy and conversion strategy.

**Why this priority**: Autonomous management is valuable only when the offer, claims, audience, economics and conversion path come from trusted project facts rather than invented content.

**Independent Test**: Select an offer whose price, audience, locations, claims and restrictions exist in approved project knowledge, and verify that the proposed plan uses those facts, identifies their sources and refuses unsupported prices, discounts or claims.

**Acceptance Scenarios**:

1. **Given** an approved knowledge base describing one eligible offer, **When** the admin starts a promotion, **Then** the system presents a sourced offer summary, WhatsApp funnel, recommended conversion event, audience strategy, placement strategy, experiment design, budget allocation and campaign plan.
2. **Given** multiple authorized offer-to-WhatsApp pairings, **When** a promotion opportunity is evaluated, **Then** Autopilot selects the pairing with the strongest verified economics, capacity and evidence inside the envelope and explains the selection.
3. **Given** missing or contradictory price, location, landing destination or prohibited-claim information, **When** the system evaluates readiness, **Then** it blocks launch and identifies the unresolved facts instead of inventing them.
4. **Given** a knowledge document changes after planning, **When** the active advertising profile becomes stale, **Then** new creative or campaign creation pauses until the affected facts are refreshed and revalidated.
5. **Given** an offer falls into a regulated or special advertising category, **When** the plan is produced, **Then** the applicable category and allowed audience constraints are explicit and launch is blocked if classification or policy eligibility is unresolved.
6. **Given** the offer has verified service areas, age restrictions, language, customer exclusions and existing-customer lists, **When** targeting is planned, **Then** hard business restrictions remain fixed while broader delivery suggestions may be explored only inside those restrictions.

---

### User Story 3 - Create and validate click-to-WhatsApp advertisements (Priority: P1)

As a project owner or admin, I want Autopilot to build the complete campaign, audience, placement and creative hierarchy with WhatsApp as an enforced destination, then validate the resulting external state before spend, so that creation and targeting cannot silently succeed in a broken or partial state.

**Why this priority**: The existing creation and targeting paths are not trustworthy. No optimization loop is valid if the wrong objective, destination, audience, placement, budget or creative is created.

**Independent Test**: Provide an eligible offer, WhatsApp business destination, image and video candidates and a bounded daily cap. Verify that the system builds a complete paused structure, validates every configured field against the effective external state, preserves the full audience in every test, and refuses activation when any object is missing, rejected or different from the approved plan.

**Acceptance Scenarios**:

1. **Given** eligible Page posts and project assets, **When** a launch plan is created, **Then** each candidate is scored for offer relevance, policy and brand safety, format suitability, freshness and available historical evidence, and the strongest candidates are explained to the admin.
2. **Given** both image and video candidates, **When** the promotion launches, **Then** Autopilot may operate image, carousel and video advertisements across every Meta placement that the live account confirms is currently eligible to open the selected WhatsApp business conversation.
3. **Given** a daily cap too small to support every candidate, **When** tests are allocated, **Then** the system reduces the number of simultaneous advertisements rather than spreading the cap too thinly.
4. **Given** an ineligible, expired, rights-restricted, unsupported or policy-rejected post or asset, **When** candidates are evaluated, **Then** it is excluded with a visible reason and receives no spend.
5. **Given** an existing Page post is selected, **When** the advertisement is created, **Then** the source post identity is preserved and reported as an existing-post creative.
6. **Given** no suitable existing Page post or project media is available, **When** a launch is requested, **Then** the system blocks the affected creative launch and explains what source media is missing instead of generating a new image or video from scratch.
7. **Given** an eligible existing image or video, **When** the system prepares it for selected placements, **Then** it may generate copy, calls to action, crops, dimensions, thumbnails and format-preserving variants without changing the offer or materially fabricating the media.
8. **Given** a plan is ready, **When** external objects are created, **Then** campaign, audience, optimization, placement, schedule, budget, Page identity, WhatsApp identity, creative and call-to-action are created paused and validated before activation.
9. **Given** the provider accepts a creation request but reports an unsupported, rejected, pending or different effective configuration, **When** reconciliation runs, **Then** activation remains blocked and the exact object, field and provider reason are visible.
10. **Given** Autopilot clones a winner or starts a creative test, **When** the new advertisement structure is built, **Then** all approved audience restrictions, exclusions, destination and attribution settings are copied or deliberately changed by a recorded experiment; none are lost through implicit defaults.
11. **Given** a small budget or sparse signal, **When** an experiment is designed, **Then** Autopilot consolidates delivery and tests one controlled variable at a time instead of creating many near-duplicate audiences and campaigns.

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

### User Story 5 - Close the WhatsApp outcome loop (Priority: P1)

As a project owner or admin, I want every attributable WhatsApp conversation to remain connected to its originating advertisement and later qualified, booked, paid, cancelled or refunded outcome, so that the AI buys profitable customers rather than cheap but useless chats.

**Why this priority**: Closed-loop business measurement is the feature's main differentiation and the basis for safe optimization.

**Independent Test**: Start a WhatsApp conversation from an advertisement, preserve its advertising referral, progress it through qualification, booking and payment, deliver a duplicate event and later refund, and verify that each canonical outcome is counted once, attributed to the correct advertisement and used in later decisions.

**Acceptance Scenarios**:

1. **Given** a confirmed server-side payment, subscription, enrollment, booking or attendance event, **When** it is received, **Then** it is recorded once with its value, currency, source and advertising attribution and is delivered to the selected measurement source.
2. **Given** the same customer event arrives from browser and server sources, **When** both copies share the same canonical identity, **Then** reporting and optimization count one conversion.
3. **Given** a refund, cancellation, chargeback, absence or lost deal, **When** the negative outcome is confirmed, **Then** the original business value is adjusted and future decisions use the corrected result.
4. **Given** deeper outcomes are reliable but too sparse or delayed for stable optimization, **When** the conversion eligibility check runs, **Then** the system continues using an eligible upper-funnel event or expected value rather than switching prematurely.
5. **Given** a campaign produces cheap signups or messages but poor payment, attendance or retention outcomes, **When** sufficient downstream data exists, **Then** it is ranked below a campaign producing stronger long-term value.
6. **Given** a service conversation, **When** it is classified as spam, support, unqualified, qualified, booking intent, purchase intent or confirmed payment, **Then** only the applicable qualified sales state contributes to qualified-message cost.
7. **Given** the first inbound WhatsApp message contains an advertising referral, **When** the conversation is opened, **Then** that referral is durably attached to the project, contact, conversation and originating advertisement for downstream attribution.
8. **Given** a customer merely starts a chat, **When** no qualified or paid outcome has occurred, **Then** the system records a conversation start and never labels it as a purchase, booking or qualified lead.
9. **Given** a qualified, ordered or paid outcome occurs inside the attributed WhatsApp journey, **When** it becomes reliable, **Then** the event is sent to the selected measurement source with its permitted WhatsApp attribution evidence and later delivery status.
10. **Given** a real outcome has no sufficient advertising attribution evidence, **When** it is recorded, **Then** it remains visible as an unattributed business outcome and is not assigned to an advertisement by guesswork.
11. **Given** attributed outcome volume or match quality drops below its healthy threshold, **When** the AI considers a financial change, **Then** it waits or uses the declared fallback outcome and clearly labels the weaker optimization basis.

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

As an operations user, I want a dedicated `مدير الإعلانات` control center beside the existing communication tools, so that paid click-to-WhatsApp advertising is distinct from outbound WhatsApp campaigns but shares the active project and familiar navigation.

**Why this priority**: Clear information architecture prevents users from confusing paid advertising with outbound WhatsApp campaigns.

**Independent Test**: Navigate on desktop and mobile, switch active projects, and verify that the dedicated workspace loads the correct project's overview, campaigns, creatives, conversions, decisions and settings while the legacy area is labeled `حملات واتساب`.

**Acceptance Scenarios**:

1. **Given** an authenticated user on desktop or mobile, **When** the shell opens, **Then** it displays `مدير الإعلانات` as a dedicated destination and `حملات واتساب` as the existing outreach destination.
2. **Given** an unconfigured project, **When** the user opens `مدير الإعلانات`, **Then** a readiness checklist explains connection, offer, tracking, budget and activation steps instead of showing an empty dashboard.
3. **Given** a configured project, **When** the workspace opens, **Then** it provides Strategy, Overview, Campaigns, Audiences, Creatives, Experiments, WhatsApp Outcomes, AI Decisions and Settings views with current health and emergency controls.
4. **Given** the active project changes, **When** the workspace reloads, **Then** all visible advertising data, commands and recommendations switch to the newly active project without retaining the previous project's data.
5. **Given** creation, validation, delivery or measurement fails, **When** the user opens the affected item, **Then** the interface shows the exact stage, object, attempted configuration, provider explanation, retry eligibility and next safe action instead of a generic failure sentence.
6. **Given** the dashboard compares spend and return, **When** totals are displayed, **Then** spend, conversions and revenue use the same visible date range, attribution window, currency and source-of-truth definition.

## Edge Cases

- A Page is connected for messages and comments but the user has not granted advertising permissions or selected an advertising account.
- A selected WhatsApp business number is not linked to the Page or advertising account, cannot receive click-to-WhatsApp traffic, or lacks the required measurement resource.
- An advertising account is disabled, under review, out of funds, has billing failure, uses an unexpected currency or no longer permits the connected user.
- The account supports conversations but rejects a deeper optimization goal, or capability availability changes between planning and creation.
- The advertiser is subject to a special advertising category or policy restriction that conflicts with the requested age, location or audience plan.
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
- A WhatsApp conversation begins without an advertising referral, with an expired referral, or through a different advertisement after an earlier touch.
- A refund, cancellation, absence or churn event arrives after a campaign was initially judged successful.
- A project is deleted, archived or loses its knowledge base while managed advertisements are active.
- All candidate creatives are rejected or unsupported; no advertisement may launch from fabricated or unverified content.
- Retargeting audience volume is too small to spend its planned allocation.
- The independent reviewer or strategist is unavailable; financial action waits rather than bypassing required review.
- The system restarts during an external request whose result is unknown; reconciliation occurs before retry.
- Meta accepts a request but later reports a rejected, pending or different effective state.
- Provider validation succeeds for one object but a child object is missing, linked to the wrong parent or configured with an unexpected destination.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a dedicated project-level workspace named `مدير الإعلانات` in desktop and mobile shell navigation.
- **FR-002**: The existing outreach campaign destination MUST be labeled `حملات واتساب` and remain functionally separate from paid advertising.
- **FR-003**: Every system-created advertisement MUST open the project's selected WhatsApp business conversation; Autopilot MUST allow Advantage+ to use every Meta placement that the live account validates as eligible for that destination and MUST reject ineligible placements and all non-Meta delivery.
- **FR-004**: Only Owner and Admin roles MUST be able to connect advertising resources, define financial authority, activate Autopilot, resume after Emergency Stop or approve an out-of-envelope change.
- **FR-005**: The system MUST associate every advertising connection, offer, creative, insight, conversion, decision, command, incident and budget record with exactly one Project.
- **FR-006**: The system MUST verify that the selected advertising account, Page, WhatsApp business identity, phone number and measurement resources are mutually eligible before allowing activation.
- **FR-007**: Secret advertising credentials MUST never be displayed after connection or included in user-visible logs and exported decision evidence.
- **FR-008**: The system MUST display connection status, granted capability status, supported objectives and optimization outcomes, expiration or reconnection status, last successful synchronization and actionable failure details.
- **FR-009**: The system MUST require a user-approved autonomy envelope containing the selected project, allowed offer-to-WhatsApp-destination pairings, advertising resources, allowed customer-data audience sources, daily cap, total or monthly cap, currency, hard audience controls, time boundary and maximum autonomous increase.
- **FR-010**: The system MUST block any action outside the active autonomy envelope until a permitted user grants a new boundary.
- **FR-011**: The system MUST obtain advertising facts from the project's existing approved or published knowledge and MUST NOT create an alternative editable knowledge base.
- **FR-012**: The system MUST produce a sourced advertising profile that records the origin, version, confidence and refresh time of offer, price, audience, location, claim, restriction, destination and brand guidance facts.
- **FR-013**: The system MUST block launch when required commercial facts are missing, contradictory, stale or below the accepted confidence level.
- **FR-014**: The system MUST never invent or silently alter prices, discounts, guarantees, schedules, availability, claims or prohibited wording.
- **FR-015**: When more than one offer is eligible, Owner/Admin MUST authorize the allowed offer-to-WhatsApp-destination pairings and Autopilot MUST select autonomously only among those pairings using verified economics, capacity, policy eligibility and evidence.
- **FR-016**: The system MUST infer and present a suitable WhatsApp-centered business funnel for SaaS, courses, products, services, bookings and events using available project facts and outcomes.
- **FR-017**: The system MUST support eligible existing Facebook Page image, carousel and video posts as advertisement sources.
- **FR-018**: The system MUST support eligible project image and video assets as advertisement sources and retain their project ownership and source identity.
- **FR-019**: The system MUST explain creative recommendations using offer relevance, brand and policy compatibility, format suitability, freshness, organic evidence when available and prior paid evidence when available.
- **FR-020**: The system MUST exclude ineligible, unsupported, expired, unavailable, rights-restricted, stale-offer or policy-rejected creatives from spend.
- **FR-021**: The system MUST choose a simultaneous creative-test count that the authorized usable daily cap can support and MUST reduce test count when evidence would otherwise be fragmented.
- **FR-022**: The system MUST support eligible click-to-WhatsApp image, carousel and video delivery across every live Meta placement that passes destination and capability validation.
- **FR-023**: The system MUST track each creative's source, version, offer facts, placement eligibility, delivery state, spend, impressions, frequency, engagement, business outcomes and fatigue status.
- **FR-023A**: The first release MUST generate advertising copy, headlines, descriptions, calls to action, crops, dimensions, thumbnails and format-preserving variants from eligible existing Page posts and project media.
- **FR-023B**: The first release MUST NOT generate finished new source images or videos from scratch; when no eligible source media exists, it MUST block that creative path and request suitable project media.
- **FR-023C**: The selected WhatsApp destination MUST be an invariant of the approved plan and every created campaign, audience group and advertisement; a missing or different destination MUST block activation.
- **FR-023D**: The system MUST choose a messaging-compatible business objective and optimization outcome from capabilities verified for the live advertising account, and MUST use the declared safe fallback rather than submitting an unsupported combination.
- **FR-023E**: The system MUST separate hard audience controls from delivery suggestions: authorized location, minimum age, language, legal restrictions and explicit customer exclusions are fixed, while broader interests, lookalikes and customer signals MAY guide delivery only inside those controls.
- **FR-023M**: Retargeting, customer-list and lookalike sources MUST be individually authorized inside the active envelope and MUST have the required consent or documented legal basis before use.
- **FR-023F**: The system MUST classify applicable special advertising categories and policy constraints before targeting is approved and MUST block creation when classification is unresolved or requested targeting is prohibited.
- **FR-023G**: The system MUST create every campaign hierarchy paused, validate the planned request before creation when supported, retrieve the effective created state and reconcile every critical field before any activation command.
- **FR-023H**: Critical validation MUST cover parent relationships, business objective, optimization outcome, bid strategy, budget source and amount, schedule, full audience controls and exclusions, placement inventory, Page identity, WhatsApp identity, creative identity and call to action.
- **FR-023I**: A provider identifier alone MUST NOT count as successful creation; success requires the complete hierarchy to exist in the intended paused state with no rejected or materially different critical field.
- **FR-023J**: Clones, replacements and experiments MUST preserve the approved destination, full audience strategy, attribution settings and policy classification unless a recorded experiment deliberately changes one eligible variable.
- **FR-023K**: The default acquisition structure MUST consolidate statistically similar delivery instead of fragmenting learning across avoidable duplicate campaigns or audience groups.
- **FR-023L**: The system MUST maintain an experiment plan containing the hypothesis, single primary variable, control, variants, eligible budget, maturity rule, attribution window, success outcome and stop rule before test spend begins.
- **FR-024**: The system MUST maintain one authoritative daily and total budget ledger for all advertisements it manages within a project authorization.
- **FR-025**: The usable daily allocation MUST preserve a configurable safety reserve below the hard daily cap to account for reporting and delivery delay.
- **FR-026**: The allocator MUST distribute usable budget among the smallest viable combination of prospecting, creative tests, audience tests, retargeting and proven winners instead of enforcing fixed percentages regardless of evidence.
- **FR-027**: The system MUST prevent concurrent campaigns or workers from reserving the same available budget twice.
- **FR-028**: Budget increases MUST be gradual, respect the maximum authorized increase and cooling period, and be blocked while tracking, connection or account health is unsafe.
- **FR-029**: The system MUST NOT pause or reduce an advertisement solely because of insufficient, delayed or statistically inconclusive short-term data.
- **FR-030**: Proven losing or rejected advertisements MAY be paused but MUST NOT be permanently deleted by Autopilot.
- **FR-031**: The system MUST rank performance first by attributable paid value or contribution when reliable, then by verified booking or qualified WhatsApp lead, then by a new messaging conversation only while stronger outcomes are unavailable or too sparse.
- **FR-032**: The system MUST treat clicks, visits, conversations, leads and signups as interim outcomes when stronger reliable outcomes are unavailable, not as unconditional final success.
- **FR-033**: The system MUST evaluate event volume, tracking quality, match quality, delay, correction rate, learning state and business value before changing the active optimization event.
- **FR-034**: The system MUST record a canonical conversion once while preserving its source events, value, currency, customer or visitor identity, occurrence time, attribution evidence and delivery status.
- **FR-035**: The system MUST deduplicate equivalent browser, server, payment, CRM, booking and attendance copies of the same business event.
- **FR-036**: The system MUST support negative adjustments for refunds, cancellations, chargebacks, churn, absence and lost deals and use them in later evaluation.
- **FR-037**: The system MUST connect project CRM qualified states, won deals, conversations, group-booking payment and attendance outcomes to the advertising conversion ledger without treating unverified classification as confirmed payment.
- **FR-037A**: The first release MUST accept authenticated, project-scoped external business events for signup, trial, subscription, renewal, enrollment, purchase, refund and cancellation through a documented generic conversion-ingestion contract.
- **FR-037B**: External conversion ingestion MUST validate event identity, source, occurrence time, project, event type, value and currency where applicable, and MUST reject unauthenticated, cross-project, unsupported or malformed events.
- **FR-037C**: The system MUST preserve the advertising referral received with the first inbound WhatsApp interaction and link it to the correct project, contact, conversation, managed advertisement and later canonical outcomes.
- **FR-037D**: Supported WhatsApp journey events MUST distinguish conversation start, qualified lead, checkout or order intent, order creation, paid purchase, cancellation, refund, delivery and other verified downstream states without upgrading a weaker event into a stronger one.
- **FR-037E**: Only business outcomes that actually occur inside the WhatsApp journey may use WhatsApp messaging attribution; website or app outcomes MUST use their applicable evidence path instead of being misrepresented as in-thread events.
- **FR-037F**: The system MUST calculate and display attribution coverage, match quality, event delay, missing-referral rate, delivery acceptance and correction rate, and MUST NOT label tracking healthy solely because recent snapshots exist.
- **FR-037G**: When several eligible advertisement referrals precede one WhatsApp business outcome, internal reporting MUST attribute it to the last eligible WhatsApp referral inside the visible attribution window, preserve all earlier touches and display Meta-reported attribution separately.
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
- **FR-045**: The system MUST reconcile actual external advertising state after mutation and before retrying an operation whose result is unknown.
- **FR-046**: Decision history MUST show proposal, evidence, evaluation, audit, judge when used, safety outcome, execution, reconciliation, actual impact and rollback status.
- **FR-047**: Decision impact MUST be evaluated after an event-appropriate delay and labeled positive, negative, inconclusive or reverted.
- **FR-047A**: The autonomous action set MUST cover supported end-to-end work, including create, validate, activate, pause, resume, replace creative, start or stop an experiment, adjust an audience suggestion, reallocate or release budget, scale a proven winner, change an eligible optimization outcome and repair or escalate a drifted external state.
- **FR-047B**: Every `WAIT`, no-change, rejection or failure decision MUST state the exact evidence threshold, missing signal, cooldown, attribution concern, policy reason or provider error; generic statements that a decision was made are not sufficient.
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
- **FR-058A**: Normal Autopilot disablement MUST pause all system-managed delivery by default; an authorized user MAY explicitly choose to leave current advertisements running without autonomous changes, and that continuing spend state MUST remain prominent.
- **FR-059**: The system MUST NOT alter unrelated manually managed campaigns unless they have been explicitly imported and assigned to Autopilot ownership.
- **FR-060**: The workspace MUST provide Strategy, Overview, Campaigns, Audiences, Creatives, Experiments, WhatsApp Outcomes, AI Decisions and Settings views plus a first-run readiness checklist.
- **FR-061**: Overview MUST display current Autopilot, connection and tracking health, daily cap, actual spend, usable remainder, safety reserve, active/test/paused counts, attributable qualified conversations, bookings, paid orders, corrected revenue or contribution, return and best and worst sufficiently evaluated advertisements.
- **FR-062**: Creative views MUST distinguish recommended, testing, winning, fatigued, rejected and paused states and explain why a creative is in that state.
- **FR-063**: All primary workspace controls and status communication MUST be keyboard accessible, readable in right-to-left layout and usable on desktop and mobile.
- **FR-064**: The system MUST keep durable audit evidence for authorization changes, financial actions, emergency actions, connection changes, conversion corrections and cross-project access denials.
- **FR-065**: Every metric comparison MUST use a visible and internally consistent date range, timezone, currency, attribution window and truth source; current spend MUST NOT be divided by all-time outcomes or revenue.
- **FR-066**: Campaign and creation views MUST show the planned configuration beside the effective external configuration and highlight any drift by field.
- **FR-067**: Audience views MUST explain hard controls, exclusions, suggestions, estimated reach when available, learning state and the reason each audience change was proposed or withheld.
- **FR-068**: Experiment views MUST distinguish planned, validating, active, learning, mature, winner, loser, inconclusive, stopped and invalid states and MUST show control, variable, sample evidence and decision rule.
- **FR-069**: Operational errors MUST identify the failing stage and object, preserve a safe paused state, expose a human-readable provider reason and correlation reference, and state whether retry, repair, reconnection or manual escalation is appropriate.

### Key Entities *(include if feature involves data)*

- **Advertising Connection**: A project's selected Meta advertising account, Page, WhatsApp business identity, phone number and measurement resources, their capability and health status, and protected authorization state.
- **Autonomy Envelope**: The admin-approved financial and operating limits within which Autopilot may act without another human approval.
- **Advertising Profile**: A derived, versioned and sourced interpretation of approved project knowledge for one or more eligible offers; it is not a replacement knowledge base.
- **Advertising Offer**: The selected product, service, course, subscription, booking or event and its verified commercial facts, destination and restrictions.
- **Managed Campaign**: The project-owned representation of a campaign managed by Autopilot, including its external identity, objective, business goal, ownership and lifecycle state.
- **Managed Ad Set**: Audience, placement, schedule, optimization event and budget allocation beneath a managed campaign.
- **Managed Advertisement**: One advertisement and its source creative, copy, call to action, external identity and delivery state.
- **Audience Strategy**: The versioned set of hard audience controls, explicit exclusions, optional delivery suggestions, eligibility evidence, size observations and experiment history used by one managed promotion.
- **Advertising Experiment**: A controlled hypothesis with its control, variants, changed variable, budget, maturity and stop rules, evidence window and final conclusion.
- **Advertising Creative**: An existing Page post, project image, project video or clarified generated-media output with eligibility, variants, evidence and performance history.
- **Insights Snapshot**: Time-bounded delivery and performance observations for campaign, ad set and advertisement levels.
- **Attribution Touch**: Evidence connecting a visitor, customer or conversation to an advertisement interaction.
- **WhatsApp Attribution Context**: The project-scoped advertising referral attached to the first inbound WhatsApp interaction and its relationship to the contact, conversation, advertisement and downstream outcomes.
- **Canonical Conversion**: One deduplicated business outcome with source-of-truth strength, value, attribution, consent or legal-basis state, platform-delivery and correction history.
- **Budget Ledger and Allocation**: The hard cap, safety reserve, committed allocation, actual spend, released budget and remaining authority for one project and period.
- **AI Decision**: The supported proposal, evidence, evaluation, independent review, safety outcome, execution and later impact assessment.
- **Execution Command**: One idempotent, state-checked external mutation derived from an approved decision.
- **Tracking Incident**: A period in which conversion, connection, billing, spend or project-isolation health is unsafe and financial action is frozen or stopped.
- **Emergency Stop Record**: The reason, initiator, affected managed delivery, blocked commands and explicit resumption history.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An authorized admin can complete connection, offer selection, tracking verification, budget authorization and guarded Autopilot activation in 10 minutes or less when all external resources are eligible.
- **SC-002**: 100% of system-created advertisements open the authorized WhatsApp business destination, with zero spend on an advertisement whose effective destination is a website, form, Messenger, Instagram Direct or an unknown target.
- **SC-003**: Across a test with at least two projects, zero advertising facts, credentials, campaigns, creatives, conversions, decisions or budget records from one project are visible or usable by the other.
- **SC-004**: 100% of repeated conversion deliveries with the same canonical identity produce one counted business outcome while retaining their delivery and correction evidence.
- **SC-005**: 100% of repeated financial commands with the same command identity cause at most one external financial mutation.
- **SC-006**: During active managed delivery, abnormal-spend and hard-cap conditions are detected within 5 minutes of becoming observable, and no autonomous increase is permitted while the condition remains active.
- **SC-007**: No autonomous decision exceeds the approved daily cap, total or monthly cap, maximum increase, allowed offer, locations, time boundary, WhatsApp destination or eligible Meta placement scope.
- **SC-008**: When tracking health is unsafe, 100% of new financial modifications are frozen until health is restored or an authorized emergency action is taken.
- **SC-009**: For a daily cap that cannot support all available creatives, the launch uses a reduced viable test set and displays the limitation rather than starting every candidate.
- **SC-010**: Every executed financial decision is discoverable with its evidence, independent review, deterministic eligibility and safety outcomes, exact execution state and later impact assessment.
- **SC-011**: Verified payment, subscription, enrollment, booking, attendance and revenue outcomes appear in the advertising workspace within 60 seconds of successful first-party receipt, excluding external platform reporting delay.
- **SC-012**: An authorized Emergency Stop request blocks new autonomous commands within 10 seconds and preserves all campaign, conversion and decision history.
- **SC-013**: A user can identify the recommended creative, why it was recommended, its source, its current allocation and its strongest reliable business outcome without opening Facebook's advertising interface.
- **SC-014**: Desktop and mobile users can reach `مدير الإعلانات`, switch project context, inspect health and activate Emergency Stop using keyboard-accessible or touch-accessible controls without confusing the workspace with `حملات واتساب`.
- **SC-015**: In automated creation scenarios covering image, video, existing-post, clone and replacement paths, 100% either reconcile a complete paused hierarchy with the approved objective, full audience, placement, budget and WhatsApp destination or fail closed before spend with the exact differing field.
- **SC-016**: In tests that clone or replace advertisements, 100% preserve all authorized audience controls, exclusions, WhatsApp attribution and policy classification unless the experiment record explicitly identifies the changed field.
- **SC-017**: At least 95% of attributable test conversations retain their advertising referral through qualified, booking and paid outcome updates; the remaining missing-referral rate is visible and prevents a false healthy status.
- **SC-018**: 100% of displayed cost, return and winner comparisons use one visible coherent reporting window and never combine current-period spend with lifetime outcomes.
- **SC-019**: Every autonomous pause or scale decision uses a declared mature evidence rule; no advertisement is paused solely because it has zero results before minimum spend, time and attribution-delay requirements are satisfied.
- **SC-020**: An operator can identify within two minutes why an advertisement failed creation, why an audience was chosen, which outcome the AI is optimizing, what it changed and when that change will be evaluated, without opening the external advertising interface.

## Assumptions

- Existing authentication, project switching and role identities are reused, but advertising authority is additionally restricted to Owner and Admin roles unless explicitly delegated later.
- The first release operates only advertisements explicitly created by or assigned to this manager; unrelated manual campaigns remain outside its control and outside its managed daily-cap ledger.
- Every paid advertisement managed by this feature opens WhatsApp. Advantage+ may use any placement that the live Meta account confirms is eligible for that WhatsApp destination; the placement set may change over time, but the customer destination never does.
- The approved daily cap is a hard product boundary. A safety reserve below that cap is used because platform delivery and spend reporting may be delayed.
- A guarded canary uses real spend after connection, offer, policy, measurement and budget readiness checks; there is no zero-spend shadow operation.
- The active project timezone determines daily and weekly business boundaries, with Cairo used by existing projects unless configured otherwise.
- Existing approved or published knowledge is authoritative for commercial facts. Derived advertising profiles are refreshable caches with source evidence, not an alternative content-management surface.
- Attributable paid value or contribution outranks verified booking and qualified WhatsApp lead; verified booking and qualified lead outrank a raw conversation start; all outrank clicks and engagement.
- The project is responsible for collecting and documenting any required customer consent or legal basis; the Ads Manager enforces the recorded state when preparing external conversion deliveries.
- Platform attribution and internal attribution are reported as evidence-based attribution, not proof that advertising caused the outcome.
- Meta may reject or delay an otherwise valid advertisement; the manager reports and reacts to effective external state rather than assuming request acceptance means delivery.
- External SaaS, commerce, payment and course systems remain the source of truth for their own subscription, order, payment, refund and enrollment lifecycles and send verified changes into the project's conversion ledger.
- The product constitution must be amended or interpreted explicitly to allow pre-authorized autonomous financial actions inside an admin-approved envelope; out-of-envelope actions remain human-approved.

## Out of Scope

- Any placement that the live Meta account does not validate as eligible for the authorized WhatsApp destination, plus Google, YouTube, TikTok and every non-Meta advertising platform.
- Website, instant-form, app-store, phone-call, Messenger or Instagram Direct customer destinations; the supported paid journey opens WhatsApp.
- Guaranteed cost per lead, message, sale, subscription, booking or attendee.
- Circumventing advertising policies, account restrictions, review, billing requirements or media rights.
- Permanent deletion of advertising campaigns by Autopilot.
- Autonomous control of unrelated manually managed campaigns without explicit ownership transfer.
- Creating an alternative knowledge base or sharing learning, customers, conversions or assets between projects.
- Generating finished new source images or videos from scratch; the first release transforms eligible existing media and generates advertising text only.
- Inventing prices, offers, discounts, availability, guarantees, claims or target locations not supported by approved project facts.
- Spending without a successful connection test, measurement-readiness test and active bounded authorization.
- Building a native SaaS subscription platform, ecommerce ordering system, payment processor, course platform or refund-management product as part of the Ads Manager; these systems integrate through the generic verified conversion contract.
