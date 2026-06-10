# Frontend Master Plan

**Last Updated**: 2026-06-10

This document tracks all frontend requirements, design structures, pages, and implementation details.

---

## Chronological Log

### 2026-06-10: Firebase Cloud Messaging Push Notifications & Xcode Bundling (Completed)
- **Goal**: Implement native iOS push notifications using Firebase Cloud Messaging (FCM), configure Xcode to bundle `GoogleService-Info.plist`, and add settings testing UI.
- **Updates**:
  - Registered `GoogleService-Info.plist` inside `mobile_app/ios/Runner.xcodeproj/project.pbxproj` with unique resource IDs (`F3C0123456789ABCDEF00001` and `F3C0123456789ABCDEF00002`) to ensure plist is compiled into iOS app package.
  - Added `firebase_core` and `firebase_messaging` dependencies to `pubspec.yaml`.
  - Created `PushNotificationService` inside `lib/core/services/` to handle iOS push permissions request, fetch/refresh token, and post to backend project FCM registry.
  - Added notification callback mapping: foreground notifications display our premium `NotificationBanner`, and background/terminated clicks navigate users directly to the bookings screen.
  - Added a "Test Push Notifications" card with a trigger button in `SettingsScreen` to test push notification delivery on demand.

### 2026-06-10: Dashboard Connection Status, Timezone Correction & Bookings Summary Cards (Completed)
- **Goal**: Fix WhatsApp connection status on the Dashboard, correct timezone offset for inbox messages, and add reservation summary cards to the Bookings screen.
- **Updates**:
  - Added dynamic WhatsApp status query to `DashboardRepository` checking `/api/whatsapp/session/status?projectId={projectId}`.
  - Updated `DashboardBloc` and `DashboardState` to fetch and preserve the current session connection state.
  - Modified `DashboardScreen` to reactively render connection status dot (green for connected, red for disconnected) directly from the bloc state.
  - Implemented client-side timezone correction by applying `.toLocal()` on all inbox messages, conversation lists, and group booking times.
  - Introduced 3 summary metrics cards at the top of `BookingsCalendarScreen` showing occupied bookings count, percentage occupancy, and active-to-total groups ratio.

### 2026-06-10: Mobile App Hardening & Parity Enhancements (Completed)
- **Goal**: Harden the Flutter Web/Mobile App to achieve 100% settings parity with the website, add light theme default, resolve settings toggle cache staleness, sort calendar events, and transition to a list of current groups.
- **Updates**:
  - Configured Light Mode as the default theme mode by updating `AppColors` to a light slate palette and configuring MaterialApp brightness to `Brightness.light`.
  - Added a post-frame check on `LoginScreen` to bypass authentication and route immediately to `/dashboard` if user session is active.
  - Sorted bookings/group appointments chronologically by time.
  - Replaced TableCalendar on Bookings screen with a list of Current Groups, displaying occupancy percentage progress indicators, status badges, and action controls (Delete, Toggle, and a bottom sheet detailing Registered Subscribers).
  - Integrated `CrmRepository` in `DashboardBloc` to compute and display real-time CRM KPI stats (Total Customers, Active Deals, Closed Won Revenue, Average Lead Score) on the Home Screen.
  - Redesigned `SettingsScreen` to support all 10 settings fields, matching the Next.js web client.
  - Updated `AuthBloc` status check to query the latest project settings from the API, eliminating cache staleness.

### 2026-06-10: Complete Flutter Mobile Application Port (Active Plan)
- **Goal**: Port the complete Next.js CRM frontend to a Flutter mobile application, ensuring absolute feature parity, robust state management with BLoC, secure session handling, and real-time messaging using SignalR.
- **Updates**:
  - Scaffold a new Flutter project in `mobile_app/`.
  - Design visual screens conforming to `impeccable` dark/light design system guidelines (tinted neutrals, 60fps animations, paired Outfit/Inter fonts).
  - Implement BLoC controllers for state synchronization, and `dio` API client with automatic token refresh.
  - Port all features: Login/Register, Project Selector, Dashboard, WhatsApp Inbox, CRM directory, Deals pipeline board, and Bookings calendar.
  - Implement full unit and widget test suites.

### 2026-06-06: Pagination for Conversations list (Completed)
- **Goal**: Implement dynamic scrolling to load more conversations (latest 20 first, scroll down to load more).
- **Updates**:
  - Add pagination state variables (`hasMoreConvs`, `loadingMoreConvs`) in `Inbox.tsx`.
  - Debounce the `searchQuery` state to `debouncedSearchQuery` to avoid rapid API requests.
  - Modify `fetchConversations` to retrieve the first page (up to 20 conversations) using the active status filter and debounced search query.
  - Implement `loadMoreConversations` that fetches conversations before the oldest timestamp of the currently loaded list.
  - Hook scroll events on the `.convList` container to trigger loading more conversations.


### 2026-06-05: Support Project Renaming in Settings (Completed)
- **Goal**: Allow the user to view and update the current project's name from the Project Settings page.
- **Updates**:
  - Added `name` field to `ProjectSettingsResponse` type.
  - Introduced `projectName` state in `Settings.tsx`.
  - Added an "اسم المشروع" (Project Name) input field in the settings form.
  - Sent the updated `projectName` in the PUT request to `/api/projects/${activeProject.id}/settings`.
  - Called `refreshProjects()` upon successful settings save to update the header/sidebar project selector display.

### 2026-06-05: Customer Blacklist visual toggle and indicators (Completed)
- **State and API update**: Updated the `Customer` type in `crm.ts` to include the `isBlacklisted` property.
- **Togglable Exclude Switch**: Added a checkbox toggle in [CustomerDetail.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/components/shared/CustomerDetail.tsx) labeled "حظر الرد الآلي بالذكاء الاصطناعي (Blacklist)" to persist blacklist status via the update customer API.
- **Visual Table Badge**: Integrated the visual red/muted Arabic badge `حظر رد آلي` in the customer grid [CustomerList.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/crm/CustomerList.tsx) to indicate blocked AI status.

### 2026-06-05: Table Pagination & Arabic Localization Controls (Completed)
- **CSS-Module Shared Styles**: Added scoped pagination styling classes (`.pagination`, `.paginationInfo`, `.paginationSelect`, `.paginationControls`, `.paginationBtn`, `.paginationBtnActive`) to [crm.module.css](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/crm/crm.module.css) and [management.module.css](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/management.module.css).
- **Arabic Pagination Footer**: Integrated user-friendly Arabic pagination footer controls across 5 main tables:
  - CRM Customer Directory ([CustomerList.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/crm/CustomerList.tsx))
  - Outbound Campaigns ([Campaigns.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/Campaigns.tsx))
  - AI Suggestion Approvals Queue ([Approvals.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/Approvals.tsx))
  - Scheduled Customer Follow-ups ([FollowUps.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/FollowUps.tsx))
  - AI Gemini Knowledge Base documents ([KnowledgeBase.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/KnowledgeBase.tsx))
- **Auto Page Resets**: Configured hooks that automatically reset the page counter back to page 1 whenever search terms, category tabs, dropdown status filters, or active projects change.
- **RTL Arrow Alignment**: Handled chevron button alignment for Arabic RTL layouts, mapping previous to right (`ChevronRight`) and next to left (`ChevronLeft`).

### 2026-05-25: CrmX Admin Redesign, Full Arabic Translations & Real-time AI Typing (Completed)
- **Visual Redesign**: Redesign `Sidebar.tsx`, `Header.tsx`, and `layout.module.css` to implement the white sidebar (with centered user profile, avatar, name, green active dot) and the solid deep blue header (with white items, project selector, and search bar). Set up active items right-border highlight for RTL.
- **Arabic Translation Pass**: Full UI copy, titles, labels, input place holders, modals, and tables translation to Arabic for CRM Pipeline, Campaigns, Follow-ups, Workflows, Knowledge Base, Approvals, and Reports.
- **Real-time AI Typing Indicator**: Integrate `AITyping` event handler in `SignalRService.ts` and set up typing state in `Inbox.tsx` chat thread, displaying pulsing dots with custom text bubble.

### 2026-05-25: Frontend Clean Code & CSS Modules Refactoring (Completed)
- **Goal**: Reorganize route page layouts into modular packages, move all inline React CSS declarations to scoped CSS Modules (`.module.css`), and keep routing entry files as thin wrappers.
- **Reorganized Directories**:
  - `frontend/src/packages/auth/` — Authentication screens and styles (`Login.tsx`, `Register.tsx`, `auth.module.css`)
  - `frontend/src/packages/dashboard/` — Analytical widgets and stats cards (`Dashboard.tsx`, `dashboard.module.css`)
  - `frontend/src/packages/inbox/` — Three-panel real-time chat with SignalR and AI Suggestions (`Inbox.tsx`, `inbox.module.css`)
  - `frontend/src/packages/crm/` — Contacts list and Deals pipeline Kanban board (`CustomerList.tsx`, `PipelineBoard.tsx`, `crm.module.css`)
  - `frontend/src/packages/error/` — Runtime error diagnostics boundaries (`ErrorBoundary.tsx`, `error-boundary.module.css`)
  - `frontend/src/components/layout/` — Desktop/mobile Sidebar, Header, and layout styling (`Sidebar.tsx`, `Header.tsx`, `layout.module.css`)
  - `frontend/src/components/shared/` — Shared CRM customer details modal drawer (`CustomerDetail.tsx`, `customer-detail.module.css`)
- **App Router Entry Wrappers**:
  - Converted `src/app/page.tsx`, `src/app/register/page.tsx`, `src/app/(dashboard)/dashboard/page.tsx`, `src/app/(dashboard)/inbox/page.tsx`, `src/app/(dashboard)/crm/page.tsx`, `src/app/(dashboard)/crm/pipeline/page.tsx`, and `src/app/error.tsx` into thin route wrappers importing components from packages.
- **Hardening and Verification**:
  - Verified 100% successful compile of Next.js production builds using Turbopack with zero warnings/errors.
  - Verified 50/50 test passes across all backend/frontend integration test modules.

### 2026-05-25: Phase 6 Frontend Dashboard, Realtime & Production Hardening (Completed)
- **Goal**: Create a Next.js application side-by-side with backend containers, implement secure authentication client routing, build a real-time 3-panel chat inbox using SignalR, and deliver a clean CRM pipeline Kanban board.
- **Pages**:
  - `frontend/src/app/page.tsx` — Login Form
  - `frontend/src/app/register/page.tsx` — User Registration Form
  - `frontend/src/app/(dashboard)/dashboard/page.tsx` — Performance Metrics & quick links
  - `frontend/src/app/(dashboard)/inbox/page.tsx` — Chat Inbox (real-time updates, AI replies, attachment upload)
  - `frontend/src/app/(dashboard)/crm/page.tsx` — Customers management
  - `frontend/src/app/(dashboard)/crm/pipeline/page.tsx` — Deals Kanban board pipeline
  - `frontend/src/app/error.tsx` — Custom glassmorphic runtime error boundary page
- **Services**:
  - `frontend/src/services/auth.ts` — Authentication handlers & refresh tokens
  - `frontend/src/services/api.ts` — Centralized API calls configuration
  - `frontend/src/services/signalr.ts` — WebSocket / SignalR client connection logic
  - `frontend/src/services/crm.ts` — Client functions for customer profiles, deal pipelines, and analytics
- **Components**:
  - `frontend/src/components/CustomerDetail.tsx` — Customer sidebar profile inspector drawer
- **Styling**:
  - `frontend/src/styles/variables.css` — Theme configurations, neon colors, sizing scales
  - `frontend/src/app/globals.css` — Global custom CSS helpers for glassmorphism, inputs, buttons, and animations

---

## Frontend Endpoints & Routing

The application utilizes Next.js App Router. Pages are protected by a client-side route guard in the layout that checks for the active authentication context.

| Client Route | Description |
| :--- | :--- |
| `/` | Login page with secure authentication form |
| `/register` | Registration form for new agents |
| `/dashboard` | Executive KPI overview displaying real-time message count, pending approvals, campaign stats, and quick links |
| `/inbox` | Three-panel real-time customer engagement UI with chat window, contact drawer, and Gemini-powered smart reply suggestions |
| `/crm` | Customer contact directory, support filtering, status editing, and tag management |
| `/crm/pipeline` | Visual Kanban board of sales/service pipeline stages with interactive drag-and-drop or column shift updates |

---

## Integration Services

### 1. Central API Communication (`api.ts`)
- Configures global Axios instance.
- Automatically inserts JWT `Authorization` header and the active tenant `X-Project-Id` context header.
- Implements response interceptors for automatic token refresh via `/api/auth/refresh` when token expiry (401) is encountered.

### 2. SignalR Realtime Bridge (`signalr.ts`)
- Manages connection status and reconnection loops.
- Joins the tenant group via backend hub method `JoinProjectGroup` mapping to project-specific message streams.
- Listens to system-wide events:
  - `ReceiveMessage`: Broadcasts incoming messages into the active chat log.
  - `ConversationStatusChanged`: Automatically shifts/reloads active panels.
  - `AISuggestionGenerated`: Updates smart-reply prompts in the agent box.
  - `AgentPresenceUpdated`: Reflects visual status indicators.

---

## Production Installation & Deployment

Follow these procedures to build and run the application in a hardened, containerized production environment.

### 1. Certificate Preparation
Nginx production configuration requires SSL certificates for TLS termination. Before launching, place your certificates or create self-signed stubs in the `nginx/certs` directory:

```bash
mkdir -p nginx/certs
openssl req -x509 -nodes -days 365 -newkey rsa:2048 \
  -keyout nginx/certs/privkey.pem \
  -out nginx/certs/fullchain.pem \
  -subj "/CN=localhost"
```

### 2. Build & Launch production containers
Deploy the services stack (backend, database, redis, rabbitmq, minio, elasticsearch, next.js frontend, and nginx) using the combined compose file commands:

```bash
docker compose -f docker-compose.yml -f docker-compose.production.yml up -d --build
```

### 3. Verify Hardening Configurations
Ensure CORS, TLS, and Rate limiting policies are active:

```bash
# Run the automated pytest suite
pytest tests/phase_6/test_zz_production.py
```
