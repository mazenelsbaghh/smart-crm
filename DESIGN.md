# Smart Customer Core Interface

Updated: 16 October 2026. The confirmed direction is light, Arabic RTL and responsive.

## Foundation

`frontend/src/styles/variables.css` defines semantic colors in OKLCH, spacing, control height, radii and motion. Legacy HSL variables remain for feature modules. Components use semantic tokens; new palette values belong in this file.

- Canvas: `--canvas`, with `--surface` for content and `--sidebar-surface` for navigation.
- Text: `--text-strong`, `--text-base`, `--text-soft`.
- Actions and selection: `--accent`, `--accent-ink`, `--accent-soft`, `--accent-soft-strong`.
- Feedback: `--success`, `--warning`, `--danger` and their soft backgrounds. Blue `--info` marks context/new items; violet `--intelligence` marks AI tools. Each has a matching soft background.
- Navigation: ink surface with dedicated `--navigation-*` text, hover and selection tokens; these are scoped to the desktop sidebar.
- Icons: Lucide icons accompany page headings, task/media tabs, status labels and actions; retain text for meaning. `PageHeader`, `Tabs` and `FilterTabs` accept icons. Filter options can carry semantic tones.
- Borders: `--border-subtle`, `--border-strong`.
- Controls: `--control-height` is 44px; standard radius is `--radius-md`.

`app/layout.tsx` loads IBM Plex Sans Arabic locally through `next/font/local`. Font files and their OFL license live under `app/fonts`. Body typography uses `--font-family`; identifiers can use `--font-mono` and explicit LTR isolation.

## Theme

The CSS root defaults to light. `ThemeProvider` in `context/theme-context.tsx` applies the shared preference to every route, including direct inbox loads. Its storage key is `smart-customer-theme-v2`; older neon-theme preferences do not determine the new default. The header retains an optional dark-mode switch.

## Application Shell

`components/layout/AppShell.tsx` is used by dashboard pages and the inbox. It contains the labeled sidebar, project switcher, page search and mobile navigation.

`config/navigation.ts` is the source for destinations, groups, shortcuts and role-filtered entries. Nested channel and daily-report destinations declare a parent. The sidebar selects the longest matching visible path; inbox channel links are also available inside the inbox.

Desktop navigation has a fixed overview link and three native disclosure groups: daily work, growth and administration. The group containing the active destination opens on route changes. Below 1024px the sidebar becomes a drawer. Below 768px four bottom destinations appear: overview, conversations, customers and more. The header includes the current section, a project switcher and a search command that also works on phones. The open conversation hides global mobile chrome to prioritize messages and composition.

## Shared Components

- `components/ui/Button.tsx`: primary, secondary, danger and ghost actions, with disabled and loading behavior.
- `components/ui/PageHeader.tsx`: title, optional context, description and actions.
- `components/ui/Badge.tsx`: labeled neutral, success, warning, danger and information badges.
- `components/ui/EmptyState.tsx`: a readable next step for empty workspaces.
- `components/ui/FormSection.tsx`: native disclosure sections for settings.
- `components/ui/Tabs.tsx`: shared wrapping tabs, an optional labeled mobile select through `compactOnMobile`, RTL arrow navigation and a change callback that can refuse transitions with unsaved input. Used by settings, content and advertising.
- `components/ui/FilterTabs.tsx`: wrapping status or category filters, optional counts and explanatory text. `compactOnMobile` shows a labeled select that retains the selected filter and every option. Used by CRM, groups, approvals and follow-ups.
- `components/ui/SearchField.tsx`: shared accessible search fields for inbox, CRM and follow-ups.
- `components/ui/StatusMessage.tsx`: consistent semantic feedback used by management editors and settings.
- `components/layout/WorkspaceSearch.tsx`: page navigation search with keyboard selection, role-filtered results and Escape dismissal.
- `components/ui/ui.module.css`: shared control, field, badge and header styles. Feature styles compose these classes. Its responsive table pattern presents management records as labeled summaries on phones.
- `components/shared/Dialog.tsx`: native modal and drawer behavior, Escape dismissal, background isolation and focus restoration. Used by navigation, inbox details and management editors.
- Existing `ConfirmDialog` and `CustomerDetail` retain their focus and isolation behavior.

Use the existing Toast, Tooltip and ConfirmDialog for their established purpose. Every actionable icon needs an accessible name. Avoid adding a new one-off control vocabulary inside a feature.

## Page Patterns

- Inbox: channel tabs, search and a labeled status select precede the conversation list. The message log and composer share the workspace with that list; customer details open in a drawer at every width. The drawer retains unsaved task/tag input when closed. Status and assignment stay visible on phones; conversation actions are a disclosure. Customer editing, AI recommendations and automation are disclosures inside the drawer.
- CRM: counted stage filters, search and records lead the page. City, stage and classification filters are revealed on demand; the result count and reset remain available. Import/export tools are separate. Each row links to the WhatsApp conversation and opens customer follow-ups or the profile. CustomerDetail separates profile, follow-ups and AI memory into tabs and retains field input across tab changes. Phone records expose secondary details on demand.
- Settings: the general tab has task navigation for reply operation, behavior, connections, AI models and project details. The selected task uses the section query parameter. Form values remain mounted across task changes; saving reveals the section containing an invalid field. Channel names, states and controls stay together; inactive channel fields use disclosures that retain their values. On phones the task navigation uses a labeled select. The save bar occupies layout space and sticks to the top of the form while scrolling. The existing unsaved-change guard remains on top-level tab navigation.
- Groups: counted availability filters distinguish available, full and inactive groups without overlapping counts. Secondary row operations use a disclosure. `/management/groups` exposes group records and booking actions before configuration. The management menu opens staff, import/export tools and schedule requests, keeping task input mounted. The course picker retains the `course` query for regular or intensive groups. Instructor filters are revealed on demand; status filters use a labeled select on phones.
- Advertising: keep the safety strip and reporting context visible. Mobile section navigation uses a labeled select. Missing readiness requirements stay visible; completed requirements use a disclosure. Managed campaigns precede the import tools. Planned state, provider state, spend, attribution and evidence retain their meaning.
- Content: under the media-type tabs, the task query selects review (default), create, schedule or brand. Review shows a selectable list of pending posts with a large preview, caption and adjacent review actions. The image opens in a keyboard-accessible zoom dialog; phones use a post selector and previous/next controls. Review also retains history; create contains image trials; schedule contains plan publication controls; brand contains logo and references. Trials retain input across these tasks; changing media type still uses the discard guard. Reporting keeps its existing data context.
- Follow-ups: one compact status filter with counts replaces duplicated metric cards and status tabs.
- Approvals: each request is a labeled article with requester, risk, time, sanitized payload and adjacent approval/rejection controls. Values wrap independently on phones; actions retain their confirmation dialogs.
- Public booking, authentication and legal pages share typography and semantic colors. The Quran studio uses the light control surfaces while retaining its distinct video preview styling.

- Dashboard: a compact metric strip precedes the ranked customer list, stage filter and direct conversation action. Workspace shortcuts and data freshness stay alongside the list.
- Reports and reply review: separate tasks into tabs with a mobile select. Hidden task panels stay mounted to preserve input. Schedule-demand requests lead the page; grouped demand and open appointments have separate views.
- Other content media: saved documents and games precede their previews. Videos use a compact history disclosure; selecting a video opens its review, and selecting a scene brings its preview into view. Finished videos precede the storyboard. Creation forms use disclosures and retain their inputs when closed. Read-only restrictions remain visible outside collapsed forms. Paper style references and teacher details are secondary disclosures.
- Campaigns, knowledge and follow-ups retain search, status and pagination in the URL; workflows retain search and status, and approvals retain status and pagination. Typing replaces the current history entry; explicit filters and page changes remain available through browser back and forward. Reset preserves unrelated URL parameters.
- Addons: sections group booking, WhatsApp automation, human transfer, schedule demand and trial invitations. Section links support quick navigation. Phone drafts have separate save actions and retain the guard against activation/navigation before save; the active transfer number is shown independently from its draft.

## Responsive Verification

Use structural changes rather than shrinking type. Test 375, 768, 1024 and 1440px, with additional narrow-phone checks when needed. Check direct loads, keyboard navigation, dialogs, long text, empty and failed requests. Phone form fields use 16px text; the shell tracks the visual viewport so the composer can remain inside the usable area when the keyboard changes it.

Generated concept images in `output/ui-ux-concepts/2026-10-16` are design references. Rendered implementation screenshots and test evidence are identified separately from those concepts. The second design direction and its implementation evidence live in `output/ui-ux-redesign/2026-10-16`; `ai-direction.png` is the generated design reference.

The remaining-page pass is recorded in `output/ui-ux-rest/2026-10-16/review.html`: 22 rendered views at four widths, using synthetic network fixtures. These are implementation screenshots, not generated concepts.

The final review is in `output/ui-ux-final/2026-10-16/review.html`: 46 views at 320, 375, 768, 1024 and 1440px in the light theme, plus 1440px in the dark theme. It uses the local production build with synthetic API data. Additional evidence covers empty and failed requests, open task panels, retained drafts, filter history, narrow subscriber records and landscape comment composition. `verification.json` records the checks and their limits.
