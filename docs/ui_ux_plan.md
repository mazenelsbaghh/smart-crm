# UI/UX Plan

**Last Updated**: 2026-05-25

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

### 2026-05-24: Phase 2 UI/UX Guidelines
- **Human-Like Chat Bubbles Sequence**: When the AI replies in multiple chunks (e.g. 2-3 messages), they must render sequentially with a visual typing indicator (`...` animated bubble) displaying between chunks. The delay must correspond to realistic typing speeds (e.g., 50ms per character).
- **Real-Time Notification Cards**: SignalR alerts must display as premium, non-blocking toast notifications in the top-right corner. VIP and SLA breach alerts should have distinctive glowing neon borders (e.g., violet/pink for VIP, neon amber/red for SLA breaches) to maintain the "attractive and premium vibe".

### 2026-05-25: Phase 4 Campaigns & Analytics UI/UX Guidelines
- **A/B Testing Conversion Metrics UI**: Comparison of Variant A and Variant B must be color-coded using distinct, high-contrast, premium color schemes (e.g., Purple-Iris for Variant A and Electric-Indigo for Variant B). Show comparison ratios using animated horizontal bar gauges that expand on load.
- **Dynamic Funnel and Conversion Charts**: Sales funnel charts showing transitions from "New" to "Won" must display dropoff ratios as percentage labels inside the connectors, styled with micro-animations on hover (expanding slightly and showing drop counts).
- **Interactive CRM Kanban Board**: The Deals pipeline Kanban board columns must support smooth drag-and-drop animations using cards with neon shadow highlights. Card dragging must trigger drop-zone indicator highlights. Column headers must display opportunity sum values dynamically recalculating on drop.
- **Search Highlight UI**: Search results must highlight matching query tokens inside messages, customer names, and notes using a warm yellow glowing background (`rgba(255, 235, 59, 0.2)`).

### 2026-05-25: Phase 6 Frontend Dashboard & Real-Time Inbox UI/UX Guidelines (Planned)
- **Fluid Layout**: Modern 3-panel split container (Conversations, Chat Area, Customer CRM Details) utilizing CSS Grids and flexbox with smooth responsive collapsing on small viewports.
- **Real-Time Presence Indicators**: User and contact presence indicators styled as pulse-animated badge rings (e.g. emerald pulse for active, slate for offline).
- **Micro-Animations**: Hover-triggered scaling on dashboard metric cards, smooth messaging transitions using spring physics, and CSS keyframe-based loading loaders.
- **Glassmorphic Panels**: Modern Frosted-glass design components (`backdrop-filter: blur()`) with semi-transparent borders for high-fidelity aesthetics.



