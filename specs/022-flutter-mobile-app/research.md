# Research & Technical Decisions: Flutter Mobile Application

## 1. Technical Scope & Architecture

The mobile application is a complete port of the Next.js React frontend to Flutter (Dart). It acts as a client connecting to the existing containerized C# (.NET 8) backend.

### Language & SDK
- **Choice**: Dart 3.11+ / Flutter 3.41.0
- **Rationale**: Matches the user's local installed environment, utilizing modern Dart language patterns (records, pattern matching, class modifiers) and robust rendering performance.

### State Management
- **Choice**: BLoC (Business Logic Component) via `flutter_bloc` & `equatable`
- **Rationale**: BLoC offers strict separation of UI and business logic, which is critical for handling real-time SignalR message streams, async API queries, and complex CRM states. It makes debugging state transitions extremely predictable.

### Networking & API Integration
- **Choice**: `dio` client + `dio_cookie_manager` (if needed) or custom Interceptors.
- **Rationale**: `dio` is chosen over the standard `http` package because it natively supports:
  - Global request/response interceptors (ideal for appending JWT authorization and the tenant `X-Project-Id` header).
  - Clean error handling.
  - Transparent JWT token auto-refresh flows (holding failed queue while refreshing).

### Real-time Communication
- **Choice**: `signalr_netcore` or `signalr_core` Dart package.
- **Rationale**: Connects natively to the ASP.NET Core SignalR hub endpoints. Maps the project group registration (`JoinProjectGroup`) and registers callbacks for message receipt, agent presence, and typing indicators.

### Local Storage & Session Persistence
- **Choice**: `flutter_secure_storage` + `shared_preferences`
- **Rationale**:
  - `flutter_secure_storage` is used for securely writing JWT tokens (access and refresh tokens) to the device's Keychain (iOS) / Keystore (Android).
  - `shared_preferences` is used for non-sensitive data, such as theme settings, developer URLs, and the active selected project context.

### Navigation & Routing
- **Choice**: `go_router`
- **Rationale**: Best-in-class declarative routing package for Flutter. Supports shell/nested navigation (e.g., maintaining a persistent bottom navbar across dashboard tabs like inbox, CRM, bookings, and settings) and handles deep-linking natively.

### UI & Styling Strategy
- **Choice**: Custom theme applying `impeccable` rules:
  - OKLCH color mappings converted to Flutter `Color` or HSL representations. Neutral colors will be tinted with brand hues.
  - Zero raw `#000` or `#fff` colors.
  - Font pairing using `google_fonts` (e.g. Outfit for headings, Inter for body).
  - Smooth animation easing using `Curves.easeOutQuart` or `Curves.easeInOutCubic`.
  - Proper adaptive widgets (`LayoutBuilder`, `MediaQuery`) to eliminate layout shifts and pixel overflows.

---

## 2. API Endpoint & SignalR Mappings

Based on the research of `frontend/src/services/`, the following endpoints must be implemented:

### Authentication Services:
- `POST /api/auth/login` -> Request body: `{email, password}`. Returns: `{accessToken, refreshToken, user}`
- `POST /api/auth/register` -> Request body: `{email, password, fullName}`
- `POST /api/auth/refresh` -> Request body: `{refreshToken}`. Returns: `{accessToken, refreshToken}`
- `POST /api/auth/logout` -> Revokes tokens

### Project Management:
- `GET /api/projects` -> Retrieves project list
- `GET /api/projects/{projectId}/customers` -> Customer list (CRM)
- `GET /api/projects/{projectId}/pipelines/stages` -> Pipeline board structure
- `GET /api/projects/{projectId}/deals` -> Deals listing
- `POST /api/projects/{projectId}/deals` -> Creates new deals
- `PUT /api/deals/{dealId}/stage` -> Updates deal stage
- `PUT /api/deals/{dealId}/status` -> Updates deal status (Won/Lost/Open)

### Customer Operations:
- `GET /api/customers/{customerId}` -> Fetch customer profile
- `PUT /api/customers/{customerId}` -> Update customer notes, tags, leadScore, budget, and blacklist status

### SignalR Hub:
- Hub URL: `{BASE_URL}/hubs/notifications?projectId={projectId}`
- Outgoing invocations:
  - `JoinProjectGroup(projectId)`
  - `UpdatePresence(status)`
- Inbound listeners:
  - `ReceiveMessage(message)`
  - `ConversationStatusChanged(conversationId, status)`
  - `AISuggestionGenerated(suggestion)`
  - `AITyping(typingState)`
  - `AITypingError(errorState)`
  - `NotificationReceived(title, body, type)`
  - `AgentPresenceUpdated(agentId, status)`
  - `CustomerUpdated(customer)`

---

## 3. Alternatives Considered

| Dimension | Option A | Option B (Selected) | Rationale for B |
|---|---|---|---|
| State Management | Provider | BLoC | BLoC's event-driven architecture maps cleaner to SignalR's event stream. |
| Networking | `http` client | `dio` client | `dio` makes intercepting JWT token expirations and queued retries much cleaner. |
| Chart Library | `syncfusion_flutter_charts` | `fl_chart` | `fl_chart` is lightweight, highly customizable, open-source, and has zero commercial licensing requirements. |
