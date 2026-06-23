# UI/UX Plan

**Last Updated**: 2026-06-23

## 2026-06-23: Fix Light/Dark Mode Contrast in Chat & CRM Panels (Completed)
- **Goal**: Address low-contrast readability issues in Light Mode and hidden dark texts in Dark Mode across the Inbox Workspace.
- **Updates**:
  - Replaced over 50 instances of hardcoded background and text colors in [inbox.module.css](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/inbox/inbox.module.css) with dynamic tokens.
  - Resolved Light Mode invisibility of customer names, subtitles, timestamps, and low-contrast details card contents.
  - Fixed Dark Mode invisibility of placeholder headers, active tab buttons, assignee names, deal cards, notes textareas, and timeline labels.

## 2026-06-22: Complete Dashboard & Side Screens Theme Redesign (Completed)
- **Goal**: Redesign all remaining dashboard pages to match the dark neon premium crm theme of the inbox and resolve any layout bugs.
- **Updates**:
  - Transition global backgrounds to deep matte charcoal `#0F1115`.
  - Harmonize card components to translucent dark gray `#171A21` with subtle borders `rgba(255,255,255,0.06)`.
  - Shift accent borders/hover highlights from cyan/pink to neon lime-green `#D8F15D`.
  - Retain the thin 80px sidebar as the unified global navigation.
  - [x] **Conversation List Card Contrast Override**: Resolved visual regression where inner rows of `.conversationItem` (e.g. `.cardHeaderRow`, `.cardActionRow`, `.cardFooterRow`) were styled with dark `#171A21` background blocks by wildcard selector in `globals.css`. Wrote transparent overrides in `inbox.module.css` to restore smooth active/inactive card states.

## 2026-06-06: Conversations list dynamic load more (Completed)
- **Goal**: Implement smooth scrolling pagination for conversation cards.
- **Updates**:
  - Add a subtle loader at the bottom of the conversation list when loading the next page of conversations.
  - Implement a clean scroll listener on the conversation list with standard debounced/throttled loading triggers.
  - Maintain focus/active state smoothly without jarring visual shifts when new items are prepended or appended.

## 2026-05-25: CrmX Admin Redesign & AI Typing Indicator (Completed)
- **CrmX Admin Sidebar**: Flat, solid white sidebar layout (`#ffffff`). Centers user's profile image avatar, name, and a green pulsing indicator with the label "نشط" (Active). Divides navigation items into semantic categories (لوحات وقوائم, أدوات الإدارة, النظام والإعدادات). Active items styled with a light blue background and a right-border highlight indicator (RTL).
- **CrmX Admin Header**: Solid professional deep blue background with white items. Includes a white glassmorphic search input and white project selector dropdown.
- **AI Replying Indicator**: A custom pulsing chat bubble at the bottom of the active chat window, displaying "الذكاء الاصطناعي يجهز الرد..." in Arabic with micro-animated typing dots.
- **Full Arabic Operational Interface**: Complete translation of the Deals Pipeline, Follow-ups, Campaigns, Workflows, Knowledge Base, Approvals, and Reports packages.

## Design Setup Completed

We have successfully initialized the design system context (`PRODUCT.md` and `DESIGN.md`) for **Smart Customer Core** based on the user's explicit aesthetic requirements:
- **Attractive and premium vibe** (جذاب بمود جامد)
- **Fast, youthful, and high-energy** (سريع وشبابي)
- **Everything is easy and simple** (سهل جداً)
- **Keyboard shortcuts for all actions** (شورت كات لكل حاجة)

### Completed Changes

- `[x]` **PRODUCT.md**: Contains the strategic design context, including register (product), target users, voice, and design principles (Frictionless Speed, Youthful Energy, Immediate Context).
- `[x]` **DESIGN.md**: Defines the visual guidelines, typography, dark/light contrast rules, and keyboard shortcut indicators.
- `[x]` **.impeccable/design.json**: Provides the extended design token configurations (metadata, color ramps, shadows, motion, breakpoints, etc.) for UI tools.

## Verification Log
1. Validated the context files by running the context loader script:
   `node .agents/skills/impeccable/scripts/load-context.mjs`
   - Output confirmed: `hasProduct: true`, `hasDesign: true`.
2. Verified that both files are compliant with the `impeccable` design system guidelines (Stitch compliance).

### 2026-05-24: Phase 2 UI/UX Guidelines (Completed)
- **Human-Like Chat Bubbles Sequence**: When the AI replies in multiple chunks (e.g. 2-3 messages), they must render sequentially with a visual typing indicator (`...` animated bubble) displaying between chunks. The delay must correspond to realistic typing speeds (e.g., 50ms per character).
- **Real-Time Notification Cards**: SignalR alerts must display as premium, non-blocking toast notifications in the top-right corner. VIP and SLA breach alerts should have distinctive glowing neon borders (e.g., violet/pink for VIP, neon amber/red for SLA breaches) to maintain the "attractive and premium vibe".

### 2026-05-25: Phase 4 Campaigns & Analytics UI/UX Guidelines (Completed)
- **A/B Testing Conversion Metrics UI**: Comparison of Variant A and Variant B must be color-coded using distinct, high-contrast, premium color schemes (e.g., Purple-Iris for Variant A and Electric-Indigo for Variant B). Show comparison ratios using animated horizontal bar gauges that expand on load.
- **Dynamic Funnel and Conversion Charts**: Sales funnel charts showing transitions from "New" to "Won" must display dropoff ratios as percentage labels inside the connectors, styled with micro-animations on hover (expanding slightly and showing drop counts).
- **Interactive CRM Kanban Board**: The Deals pipeline Kanban board columns must support smooth drag-and-drop animations using cards with neon shadow highlights. Card dragging must trigger drop-zone indicator highlights. Column headers must display opportunity sum values dynamically recalculating on drop.
- **Search Highlight UI**: Search results must highlight matching query tokens inside messages, customer names, and notes using a warm yellow glowing background (`rgba(255, 235, 59, 0.2)`).

### 2026-05-25: Phase 6 Frontend Dashboard & Real-Time Inbox UI/UX Guidelines (Completed)
- **Fluid Layout**: Modern 3-panel split container (Conversations list, Active Chat log, Customer details drawer) using CSS Grids and flexible layouts that collapse responsively on smaller mobile screens.
- **Real-Time Presence Indicators**: Pulsing active contact status rings (emerald pulse for active connection, slate for offline/disconnected) connected directly to SignalR events.
- **Micro-Animations**: Fluid CSS transitions, neon glow highlights on focused inputs, and spring-like button interactions for message sending, tab switching, and card updates.
- **Glassmorphic Panels**: Premium frosted-glass design containers (`backdrop-filter: blur(16px)`) with semi-transparent borders and neon-lit background gradient glows for a high-fidelity visual experience.
- **Error Fallback Design**: Custom-designed boundary template page at `frontend/src/app/error.tsx` matching the application styling with animated warning icons, stack details debug drawers, and action recovery triggers.
