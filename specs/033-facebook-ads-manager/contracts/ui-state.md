# UI State Contract: مدير الإعلانات

The interface is an Arabic RTL, high-density product command center. It uses the existing navy/cyan design system with restrained semantic color. Familiar product patterns, standard tabs/tables and inline progressive detail take priority over decorative card grids or modal flows.

## Route and navigation state

Route: `/management/ad-manager?tab={tab}`

Allowed tabs:

- `strategy`
- `overview`
- `campaigns`
- `audiences`
- `creatives`
- `experiments`
- `outcomes`
- `decisions`
- `settings`

Invalid/missing tab redirects or replaces to the role-appropriate default without losing other safe query filters. Browser back/forward restores the selected tab and filters.

## Persistent control strip

Visible on every tab:

- Autopilot state and real-spend state.
- AI-selected offer and authorized WhatsApp destination (masked display number/label).
- daily cap, observed spend, committed amount, reserve and usable remainder in one reporting period;
- tracking state and `asOf` freshness;
- current optimization outcome and fallback label;
- normal disable and Emergency Stop.

Emergency Stop remains reachable by keyboard/touch without scrolling. Normal disable clearly offers `إيقاف الإعلانات المدارة` (default) or `تركها تعمل بدون تعديلات AI`; the latter requires an explicit continuing-spend acknowledgement for that request and confirms continuing spend inline.

## Shared resource state

Each resource query has independent state:

```ts
type ResourceState<T> = {
  data: T | null;
  status: 'idle' | 'loading' | 'ready' | 'refreshing' | 'partial' | 'error';
  asOfUtc?: string;
  error?: AdManagerError;
};
```

- Initial loading uses skeleton structure.
- Refresh keeps prior data visible with freshness indicator.
- One failed resource does not blank other views.
- Project switch aborts in-flight requests, clears old project data synchronously and ignores late responses.
- Mutations optimistically show only local operation submission, never provider success; actual state follows operation polling/reconciliation.
- Non-terminal operations poll at most every five seconds; the project change cursor polls at most every fifteen seconds and resumes from its last cursor after reconnect. SignalR may accelerate updates but never replaces the polling fallback.

## View contracts

### Strategy

- Authorized offer-to-destination opportunities.
- Selected offer, economics/capacity/policy evidence and source citations.
- Current business outcome hierarchy and fallback reason.
- Latest immutable plan, readiness blockers and operation progress.
- Primary action follows the current safe state: refresh facts, validate, provision paused or activate.

### Overview

- One visible date range/timezone/currency/attribution window.
- Spend, qualified WhatsApp outcomes, bookings, paid orders, net value and return from the same window.
- Separate unattributed outcomes and Meta-reported results.
- Attribution coverage/freshness before winner/loser claims.
- Small useful funnel and allocation relationship, no hero-metric template.

### Campaigns

- Campaign -> ad set -> creative/ad hierarchy.
- Configured/effective/review/reconciliation status.
- Expandable planned-versus-effective diff with field severity.
- Provider failure stage, code, trace reference, retryability and next safe action.
- Imported legacy objects labeled `غير متحقق` until reconciled.

### Audiences

- Hard controls shown separately from AI suggestions.
- Authorized customer sources and legal-basis status.
- Estimated reach only when provider-supplied, labeled with freshness.
- Actual reach, overlap/delivery evidence and rationale for a change or `WAIT`.
- No raw provider JSON.

### Creatives

- Source, offer, concept/hook, format compatibility, rights/policy and paid state.
- Recommendation bands, not long false-precision percentages.
- Testing/winner/fatigue labels derive from experiment evidence.
- Preview uses accessible media fallback and never auto-plays sound.

### Experiments

- Hypothesis, one primary variable, control, variants, budget, maturity rule, attribution cutoff and stop rule.
- States: planned, validating, active, learning, mature, winner, loser, inconclusive, stopped, invalid.
- No winner/loser styling before maturity.

### WhatsApp Outcomes

- Journey rows connect referral -> conversation -> qualification -> booking/order -> paid/corrected state.
- Truth source labels: first-party verified, Meta-reported, CRM, AI classification, unattributed/unknown.
- Raw `ctwa_clid`, phone and match identifiers never display.
- Filters for type, attribution state, truth source and reporting window.

### AI Decisions

- Action, target, evidence window, maturity, Strategist/Auditor/Judge/Safety verdicts, command/reconciliation and impact.
- `WAIT` and failure show exact reason codes/thresholds.
- Decision detail links to related plan, experiment, provider operation and outcome evidence.

### Settings

- Connection/WABA/phone/Dataset capability and permission readiness.
- Envelope offers/destinations, audience-source grants, hard controls, budgets, time and normal-disable policy.
- Webhook source and Business Messaging test-event status.
- Secrets reveal once and cannot be retrieved again.

## Error and empty states

- First run is an inline readiness rail, not an empty dashboard.
- No eligible offer/creative/audience explains the missing source fact/action.
- Provider failure preserves paused objects and offers only safe retry/repair/reconnect/escalate actions.
- Missing attribution shows `غير منسوب` and how coverage affects AI decisions; it is never rendered as zero success.
- Offline/timeout keeps last known data with stale timestamp and disables unsafe mutations.

## Precision and copy

- Money: account currency, at most two display decimals while retaining ledger precision.
- Rates/coverage: at most one decimal; confidence shown as a range/band when applicable.
- Dates: project timezone with a visible timezone label; exact UTC available in accessible detail.
- No generic `تم تحليل الأداء واتخاذ القرار`. Every state states what happened, why and what happens next.

## Keyboard and accessibility

- Native tab semantics and arrow-key navigation.
- Visible `:focus-visible` state with WCAG 2.1 AA contrast.
- Any future command-surface shortcut is added only when the shell actually provides that surface; this feature does not invent a dead `Cmd/Ctrl+K` interaction.
- Emergency Stop has an accessible name and confirmation that describes spend impact.
- Status changes use polite `aria-live`; Emergency Stop failure is assertive.
- Tables use semantic headers and accessible expandable-row controls.
- Reduced motion removes non-essential transition duration; no layout-property animation.

## Responsive behavior

- `>=1440`: dense control strip, nine tabs and split hierarchy/detail pane.
- `1024-1439`: horizontal tab overflow and collapsible detail pane.
- `768-1023`: two-row control strip; tables switch non-critical columns into row detail.
- `375-767`: primary status/action stack; views remain list/table hybrids with no horizontal page overflow; Emergency Stop stays visible.

Typography uses the existing Inter/system product scale, not fluid display headings. Surfaces are flat navy levels with 1px borders; cyan marks focus/current action only, magenta is reserved for urgent attention and semantic red for destructive emergency state.
