# Feature Specification: Arabic Light Admin Platform With Persistent WhatsApp State

**Feature Branch**: `011-arabic-light-admin-persistence`  
**Created**: 2026-05-25  
**Status**: Draft  
**Input**: User requested converting the platform to a light CRMX-style Arabic admin interface, preserving WhatsApp connection and chats, and adding a place to enter an API key so the bot can start replying.

## User Scenarios & Testing

### User Story 1 - Arabic Light Admin Shell (Priority: P1)

As an Arabic-speaking operator, I want the platform UI to use Arabic labels, RTL layout, and a clean light admin style so I can work without mixed language or dark-mode friction.

**Independent Test**: Open the dashboard, inbox, CRM, and settings pages and verify the shell, navigation labels, headings, action labels, and major empty states are Arabic and RTL.

**Acceptance Scenarios**:

1. **Given** the user opens any authenticated page, **When** the page renders, **Then** the document uses `lang="ar"` and `dir="rtl"`.
2. **Given** the user sees the admin shell, **When** they scan navigation, **Then** all primary navigation labels are Arabic.
3. **Given** the user opens settings or inbox, **When** content loads, **Then** the page uses a light white/soft-gray dashboard style matching the provided CRMX reference.

### User Story 2 - Persistent WhatsApp Session and Chats (Priority: P1)

As an operator, I want WhatsApp connection and conversation history to remain available across refreshes, frontend rebuilds, and container restarts so I do not lose the QR session or customer chats.

**Independent Test**: Connect WhatsApp, restart the frontend/backend/gateway containers, and verify session status and conversations are still available without manual data deletion.

**Acceptance Scenarios**:

1. **Given** a project has an existing WhatsApp session, **When** the gateway restarts, **Then** it restores the saved Baileys credentials from the mounted sessions volume.
2. **Given** messages have been received, **When** the frontend refreshes or the container restarts, **Then** conversations remain visible from backend persistence.
3. **Given** the gateway cannot reconnect, **When** restore attempts fail, **Then** it stops retrying after a bounded number of attempts and does not delete stored credentials automatically.

### User Story 3 - API Key Setting for Automated Replies (Priority: P1)

As a project owner, I want a visible settings field for the AI provider API key so the bot can use it to send automated replies.

**Independent Test**: Open project settings, enter an API key, save preferences, reload the page, and verify the setting is retained or masked without exposing the secret in logs.

**Acceptance Scenarios**:

1. **Given** the user opens settings, **When** they view bot settings, **Then** there is an Arabic API key input with helper text.
2. **Given** the user saves preferences, **When** the API responds successfully, **Then** the page confirms the save in Arabic.
3. **Given** an API key is stored, **When** settings are loaded, **Then** the UI does not leak the full key unnecessarily.

### User Story 4 - Real WhatsApp Phone Display (Priority: P2)

As an operator, I want customer WhatsApp identifiers to show the real phone number instead of the WhatsApp LID identifier so customer data is understandable.

**Independent Test**: Receive a WhatsApp message where Baileys includes both `@lid` and `@s.whatsapp.net` IDs and verify the inbox and customer detail display the phone number.

## Requirements

### Functional Requirements

- **FR-001**: The authenticated frontend shell MUST use Arabic labels and RTL flow for navigation, headers, inbox, customer detail, CRM, and settings surfaces.
- **FR-002**: The default visual theme MUST be light mode with white surfaces, soft borders, compact admin spacing, and restrained accent colors.
- **FR-003**: The platform MUST keep the existing WhatsApp session directory mounted and MUST NOT clear session data during normal restart, refresh, or reconnect flows.
- **FR-004**: The gateway MUST attempt safe bounded auto-restore of existing project sessions after restart.
- **FR-005**: Conversation history MUST continue to come from backend persistence and MUST NOT be cleared by frontend state resets.
- **FR-006**: Project settings MUST expose an API key input for AI replies and persist it through the existing project settings path when available.
- **FR-007**: The API key UI MUST avoid printing or logging the raw key.
- **FR-008**: Customer phone display MUST prefer the real WhatsApp phone number when available and only fall back to LID if no phone number exists.
- **FR-009**: The feature MUST keep existing Docker local deployment working through `http://localhost`.
- **FR-010**: The authenticated shell MUST align with the "CrmX Admin" layout:
  - Sidebar: Centered user profile avatar, name, and green pulsing status dot showing "نشط" (Active), with sidebar menu links grouped into clear categories (APPS & PAGES, MANAGEMENT, SYSTEM) and a copyright footer.
  - Header: Solid deep blue primary theme with clean white text, white action icons, white glassmorphic search bar, and white text project selector.
- **FR-011**: ALL frontend pages/packages MUST be fully translated to Arabic (Dashboard, Inbox, Settings, Deals Pipeline, Follow-ups, Campaigns, Workflows, Knowledge Base, Approvals, Reports).
- **FR-012**: When an incoming customer message is received and AI Auto-Reply is active, the system MUST broadcast an "AITyping" SignalR event to clients, and the frontend MUST display a custom animated typing indicator bubble in the active chat log until a reply from "AI" or "Agent" is received.
- **FR-013**: Project settings MUST persist the Gemini API key supplied by the user.

### Non-Functional Requirements

- **NFR-001**: Frontend changes SHOULD preserve the current package/module boundaries under `frontend/src/packages`, `frontend/src/components`, and `frontend/src/shared` when present.
- **NFR-002**: The UI MUST avoid nested card-heavy marketing composition and remain usable as an operational CRM/admin tool.
- **NFR-003**: Backend and gateway changes MUST be backward-compatible with existing stored project data.

## Edge Cases

- Saved WhatsApp credentials exist but are invalid: restore attempts are bounded and status becomes disconnected with a readable error.
- API key is empty: automated replies remain disabled or use existing server configuration without frontend crashes.
- Backend returns old LID customers: the frontend displays the normalized phone if backend has already repaired it; otherwise it shows a readable fallback.
- Browser extensions inject attributes into `<body>`: hydration warning is non-product and out of scope unless app code introduces mismatched markup.

## Success Criteria

- **SC-001**: A user can navigate the app in Arabic RTL on light mode without mixed primary navigation language.
- **SC-002**: WhatsApp session remains connected or restorable after Docker restart without deleting session data.
- **SC-003**: Existing conversations still appear after refresh and restart.
- **SC-004**: Settings includes an Arabic API key field and save path for AI replies.
- **SC-005**: TypeScript frontend check and backend build complete successfully.
