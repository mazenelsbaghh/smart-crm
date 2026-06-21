# Feature Specification: UX/UI Unified Inbox Redesign & GSAP Animations

**Feature Branch**: `026-inbox-redesign-gsap-animations`

**Created**: 2026-06-21

**Status**: Draft

**Input**: User description: "عايزك توحد كومبونت لكل حاجه محتاجه و تعملها برضو. وعايز نظبط الوان كل حاجه و نظبط كل ده و نشوف كل العيوب و نحلها لكل حاجه عايزها احترافيه. عايزوا شبه الصوره بالونا و الصوره و نفس التقسيمع و كل ده عايزوا احترافي و ضفلي انيميشن بده https://gsap.com/docs/v3/"

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Premium Unified Layout (Priority: P1)
As a CRM Agent, I want a single, cohesive, modern dark-themed inbox interface that brings together WhatsApp, Messenger, and Comments under identical visual styling, layout partitions, and color accents.
- **Left Sidebar**: Thin panel (80-90px) displaying icons for Home, Inbox, WhatsApp, Messenger, Instagram, Comments, Tasks, Campaigns, Analytics, Settings.
- **Worklist/Conversations Panel**: Dark matte panel displaying metrics cards and conversation list cards.
- **Workspace**: Light off-white (`#F8F8F6`) panel with chat message thread, profile header, quick action buttons, and timeline.
- **Right Sidebar (Context Panel)**: Dark matte panel with deal data, lead scores, AI insights, and automations.

**Why this priority**: This is the core visual requirement. Agent efficiency relies on a clean, professional, unified layout.

**Independent Test**:
Can be fully tested by opening the WhatsApp inbox, Messenger inbox, or Comments inbox, and verifying they all render the unified 4-pane layout (Sidebar -> Worklist -> Active Chat/Timeline -> Customer Details) with identical dark background, lime-green accents, and cards.

**Acceptance Scenarios**:
1. **Given** the agent is on the WhatsApp Inbox (`/inbox`), **When** the page loads, **Then** they see the unified layout including top metric counters, Worklist, Active customer header with quick contact buttons, tab menus, chat history with timeline, and the dual-card right sidebar.
2. **Given** the agent is on the Messenger Inbox (`/inbox/messenger`), **When** the page loads, **Then** the exact same structure is rendered with Messenger-specific messages.
3. **Given** the agent is on the Comments Inbox (`/inbox/comments`), **When** the page loads, **Then** the exact same structure is rendered with Comments-specific messages.

---

### User Story 2 - Accent-Colors and Screenshot Fidelity (Priority: P1)
As an Agent, I want the theme colors and active states to match the provided screenshot:
- Core background: Deep matte charcoal/black (`#0F1115`).
- Card containers: Smooth dark translucent gray panels (`#171A21`) with borders (`rgba(255,255,255,.06)`).
- Active card in Worklist: Accentuated with a solid neon lime-green background (`#D8F15D`) and dark text (`#1A1A1A`).
- Circular buttons (phone, chat, mail, active sidebar menu): Neon lime-green borders or backgrounds.
- Right-side context panel: Dark cards with Lead Score, Tasks, AI Insights, and Automations.
- Chat message styling: Incoming -> `#1D2430`, Outgoing -> `#D8F15D`.

**Why this priority**: The user wants the interface to look exactly like the reference image to feel premium.

**Independent Test**:
Inspect the page styling and verify that the color classes utilize the exact color codes and layout distribution as shown in the screenshot.

**Acceptance Scenarios**:
1. **Given** a list of conversations in the Worklist, **When** the agent clicks a conversation, **Then** that conversation's card turns neon lime-green with dark text, while other cards remain dark gray.
2. **Given** the active customer detail pane, **When** rendered, **Then** the quick-action round buttons (call, message, mail, calendar, folder) are highlighted in lime-green.
3. **Given** the right detail sidebar, **When** rendered, **Then** the cards display real customer data (budget, stage, score, probability, insights) from the backend.

---

### User Story 3 - Real Customer Data & Backend Integration (Priority: P1)
As an Agent, I want to view and update real CRM metadata (Lead Score, Purchase Probability, Deal Value, AI Insights, and Automations) on the right context panel, persisting updates directly to the database.

**Why this priority**: Fulfills the clarified requirement to extend the backend database and API to support these new fields for real data editing.

**Independent Test**:
Modify lead score or deal budget, reload the page, and verify the changes are persisted to the database.

**Acceptance Scenarios**:
1. **Given** a customer details page, **When** the agent updates a lead score, stage, or budget, **Then** the change is saved via C# API controller and updated in the UI.

---

### User Story 4 - GSAP Micro-Animations (Priority: P2)
As an Agent, I want smooth, modern transitions and animations when interacting with the interface to make the CRM feel responsive, alive, and professional.

**Why this priority**: Improves user engagement and fulfills the explicit request for GSAP animations.

**Independent Test**:
Load the page and verify that panels animate in, metric numbers/cards fade up, and hover states have smooth transitions powered by GSAP.

**Acceptance Scenarios**:
1. **Given** the inbox page is loading, **When** initial render completes, **Then** the top metric cards (Worklist, New leads, Updates, Assigned) fade up and slide in sequentially (staggered animation).
2. **Given** the agent hovers over circular quick action buttons or sidebar menus, **When** hovered, **Then** they expand slightly or rotate smoothly with a micro-bounce effect.
3. **Given** the agent selects a different conversation, **When** the active panel updates, **Then** the right-sidebar details card slides in from the right and the chat composer fades in.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST implement a unified `InboxLayout` component shared by `/inbox`, `/inbox/messenger`, and `/inbox/comments`.
- **FR-002**: The layout MUST use a global stylesheet with a Neon Midnight color palette containing:
  - Deep matte charcoal primary background (`#0F1115`).
  - Transparent dark gray panel cards (`#171A21`).
  - Accent colors: Neon Lime-Green (`#D8F15D`), Lavender (`#CBB8FF`), and Pink/Coral (`#f35c6e`).
- **FR-003**: The Left Sidebar MUST render circular navigation icons, applying a lime-green border/background for the active item.
- **FR-004**: The Worklist panel MUST display four metric cards at the top (Worklist, New leads, Updates, Assigned) with respective colored indicators.
- **FR-005**: The selected card in the Worklist MUST transition to a solid lime-green background with dark text.
- **FR-006**: The Active Customer Header MUST display the customer's avatar, name, metadata, and 7 circular quick-action buttons.
- **FR-007**: The Right Sidebar MUST render Lead Score, Tasks, AI Insights, and Automations using real data.
- **FR-008**: The system MUST integrate the GreenSock Animation Platform (`gsap` and `@gsap/react` npm packages) to animate the entrance of metrics, hover micro-interactions, and panel slide-ins.
- **FR-009**: The Backend C# Database models (Customer) MUST be extended with:
  - `LeadScore` (int)
  - `PurchaseProbability` (int/percentage)
  - `PipelineStage` (string/dropdown)
  - `DealValue` (decimal/budget)
  - `AIInsights` (text/JSON)
  - `AutomationRules` (text/JSON)

### Key Entities

- **Customer**: Extended C# EF Core domain entity representing a contact/lead.
- **InboxLayout**: The container wrapper orchestrating the unified 4-panel viewport.
- **WorklistCard**: Component displaying the name, subtitle, and status pills of a lead/deal.
- **RightSidebarDetails**: Component displaying the Active Deal and Active Task cards.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Inbox pages (/inbox, /inbox/messenger, /inbox/comments) load and render unified styles under 1 second.
- **SC-002**: Transitioning between active conversations occurs smoothly without layout jumps or flickering.
- **SC-003**: GSAP animation duration for entrance transitions is bounded to 0.4 seconds to maintain responsiveness.
- **SC-004**: The CSS and JSX code duplication between the three inbox channels is reduced by at least 60% by sharing the layout and sub-components.
- **SC-005**: Extended database fields are correctly fetched and persisted via EF Core.

---

## Assumptions

- GSAP package can be installed and bundled using standard Next.js build options.
- The existing business logic (SignalR real-time messages, customer details edit API, blacklisting) remains functional underneath the redesigned UI wrapper.
