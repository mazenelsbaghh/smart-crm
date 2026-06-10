# Implementation Plan: Flutter Mobile Application for Smart CRM

**Branch**: `022-flutter-mobile-app` | **Date**: 2026-06-10 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen mac/apps/smart whatsapp/specs/022-flutter-mobile-app/spec.md)

**Input**: Feature specification from `/specs/022-flutter-mobile-app/spec.md`

## Summary

Build a complete, production-grade Flutter mobile application (`mobile_app/`) that integrates with the existing running ASP.NET Core backend. The mobile app will offer secure login and multi-tenant project isolation, a real-time WhatsApp chat inbox with AI suggestions, a CRM customer profile manager, sales deal pipeline tracking, a group appointments scheduling calendar, a visual dashboard, and settings panels. The UI will follow `impeccable` design principles, incorporating google fonts, OKLCH tinted neutral theme guidelines, smooth animations, and adaptive layouts.

## Technical Context

**Language/Version**: Dart 3.11.0, Flutter 3.41.0 (Stable)

**Primary Dependencies**: `flutter_bloc`, `dio`, `signalr_netcore`, `go_router`, `flutter_secure_storage`, `shared_preferences`, `google_fonts`, `table_calendar`, `fl_chart`, `intl`

**Storage**: `flutter_secure_storage` (auth token persistence), `shared_preferences` (active project ID & configurations)

**Testing**: `flutter_test` (unit and widget tests), `integration_test` (end-to-end user flows)

**Target Platform**: Android (API 21+), iOS (13.0+)

**Project Type**: Mobile Application

**Performance Goals**: App launch to dashboard under 2 seconds, UI frame rate maintained at 60fps+, SignalR latency below 200ms.

**Constraints**: Strict multi-tenant isolation by passing `X-Project-Id` in headers on all API requests. Silent background JWT refresh.

**Scale/Scope**: 7 primary modules (Auth, Dashboard, Inbox, CRM, Bookings, Settings, Shell)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Check | Status / Justification |
| :--- | :--- | :--- |
| **I. Modular Monolith** | Separate code layers by feature folders. | **PASSED**. The Flutter app will follow a feature-first folder structure (`lib/features/...`) separating auth, inbox, crm, settings, and shared. |
| **II. Project Isolation** | Scoped context per project. | **PASSED**. The active ProjectId is stored in local preferences and attached to every API request header and SignalR hub URI. |
| **III. Gemini 3.5 AI** | UI supports multimodal suggestions. | **PASSED**. Handles RAG suggested texts and status displays natively from standard backend APIs. |
| **IV. Human-Like Messaging** | Real-time typing indicators. | **PASSED**. Uses SignalR listeners to display typing indicator countdowns and stages (`generating` / `typing`). |
| **V. Risk-Based Action Approval** | RAG suggested message approval. | **PASSED**. Inbox actions provide explicit "Approve & Send" and "Reject/Edit" paths for suggested replies. |

## Project Structure

### Documentation (this feature)

```text
specs/022-flutter-mobile-app/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Data models and serialization definitions
└── quickstart.md        # Developer setup guide
```

### Source Code

The mobile app will reside in the `mobile_app/` directory at the repository root.

```text
mobile_app/
├── android/
├── ios/
├── test/
│   ├── features/
│   │   ├── auth_test.dart
│   │   ├── inbox_test.dart
│   │   └── crm_test.dart
│   └── mocks/
└── lib/
    ├── main.dart
    ├── core/
    │   ├── theme/
    │   │   ├── colors.dart
    │   │   └── typography.dart
    │   ├── services/
    │   │   ├── api_client.dart
    │   │   └── signalr_service.dart
    │   └── utils/
    └── features/
        ├── auth/
        │   ├── data/
        │   ├── bloc/
        │   └── presentation/
        ├── dashboard/
        │   ├── bloc/
        │   └── presentation/
        ├── inbox/
        │   ├── data/
        │   ├── bloc/
        │   └── presentation/
        ├── crm/
        │   ├── data/
        │   ├── bloc/
        │   └── presentation/
        ├── bookings/
        │   ├── data/
        │   ├── bloc/
        │   └── presentation/
        └── settings/
            ├── bloc/
            └── presentation/
```

**Structure Decision**: Mobile application structured as a sub-project in the workspace root under `mobile_app/`, keeping the root project clean while isolating mobile-specific build configurations and dependencies.

## Verification Plan

### Automated Tests
- Run all unit and widget tests:
  ```bash
  cd mobile_app && flutter test
  ```

### Manual Verification
- Deploy to iOS simulator / Android emulator.
- Test authentication lifecycle (register, login, token refresh, logout).
- Validate project selector switching.
- Verify real-time message stream (receive and send WhatsApp messages) and AI suggestion approval.
- Verify CRM operations (edit customer notes, tags, move pipeline stages).
- Validate Group Appointments calendar bookings and slots display.
- Validate Analytics dashboard charts display.

## Mobile App Hardening & Parity Enhancements (2026-06-10)

### 1. Default Light Theme Mode
- Change `AppColors` in `lib/core/theme/colors.dart` to a clean light slate color palette (white background, slate text, professional teal primary).
- Update the MaterialApp config in `lib/main.dart` to use `Brightness.light`.

### 2. Auto-Login Check on Launch
- Add a post-frame check in `LoginScreen`'s `initState` to immediately redirect authenticated users, preventing them from getting stuck on the login screen.

### 3. Current Groups List & Chronological Sorting
- Re-purpose `BookingsCalendarScreen` to show the list of all current groups (weekly sessions) instead of the TableCalendar view.
- Sort events chronologically by time.
- Display group type, days/time, bookings/capacity occupancy ratio progress bar, and status badge (Active vs. Full).
- Add a subscribers button showing a list of registered customers.
- Update `BookingFormDialog` to allow inputting days and picking a specific date/time.

### 4. Real-time Dashboard CRM Stats
- Inject `CrmRepository` into `DashboardBloc`.
- Fetch active customers and deals to calculate and display: Total Customers, Open Deals, Closed Won Revenue, and Average Lead Score.

### 5. Settings Screen Parity
- Add all 10 settings fields to `SettingsScreen` matching the Next.js web settings page.
- Fix cache staleness in `AuthBloc._onCheckStatus` by querying the latest project details from the network.

