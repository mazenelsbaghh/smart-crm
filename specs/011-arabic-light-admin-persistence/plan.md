# Implementation Plan: Arabic Light Admin Platform With Persistent WhatsApp State

**Branch/Feature**: `011-arabic-light-admin-persistence`  
**Spec**: `specs/011-arabic-light-admin-persistence/spec.md`  
**Date**: 2026-05-25

## Summary

Convert the authenticated Smart Customer platform from a dark English admin shell into a light Arabic RTL operational CRM experience, while keeping WhatsApp sessions and conversation data persistent across refreshes and Docker restarts. Add project-level AI API key configuration in settings so automated replies can be enabled per project.

## Technical Context

- **Frontend**: Next.js App Router, React, TypeScript, CSS modules, global HSL design tokens.
- **Backend**: ASP.NET Core modular monolith, EF Core, PostgreSQL, RabbitMQ worker architecture.
- **Gateway**: Node.js WhatsApp gateway using Baileys, session files mounted at `./whatsapp-gateway/sessions:/app/sessions`.
- **Deployment**: Docker Compose with nginx serving frontend and API through `http://localhost`.

## Constitution Alignment

- **Modular Monolith**: Backend settings changes stay inside Projects API and AI worker reads existing ProjectSettings.
- **Tenant Isolation**: All settings and WhatsApp sessions remain scoped by `ProjectId`.
- **Gemini Engine**: API key field maps to existing `ProjectSettings.GeminiApiKey`.
- **Human-Like Messaging**: Existing AI delay and auto-reply settings remain visible and configurable.
- **Security**: API key is saved through authenticated project settings and must not be printed in logs.

## Design Direction

### ui-ux-pro-max Findings

Design system search recommended a data-dense dashboard pattern and Arabic typography (`Noto Sans Arabic` / `Noto Naskh Arabic`) for RTL readability. The color recommendation was dark, so this plan adapts the pattern, typography, and interaction checklist while replacing the palette with a light admin palette per the user request.

### impeccable Application

The UI must feel like a practical CRM/support workspace: restrained surfaces, compact spacing, clear scan hierarchy, visible focus states, no nested decorative cards, and no marketing landing composition. The provided CRMX reference is treated as a structural style cue: light shell, left navigation, clean toolbar, white panels, subtle borders, and high-density operational controls.

## Architecture Plan

### Frontend

- Update root document metadata to Arabic and set `lang="ar"` and `dir="rtl"`.
- Replace global dark tokens in `frontend/src/styles/variables.css` with light-mode HSL tokens and Arabic font stack.
- Redesign layouts to match "CrmX Admin" theme precisely:
  - Sidebar: Destructure `user` from `useAuth()` to render a centered profile card containing a circular avatar, the user's name, and a green pulsing dot with "نشط" (Active) text. Group the navigation menu into three semantic lists: APPS & PAGES (لوحات وقوائم), MANAGEMENT (أدوات الإدارة), and SYSTEM (النظام والإعدادات), and place a copyright footer at the bottom.
  - Header: Styled with a solid deep blue background (`linear-gradient(90deg, hsl(221, 83%, 53%), hsl(221, 83%, 45%))`), white text/icons, white glassmorphic search input, and white project selector dropdown.
  - Custom active menu highlights: Blue background and blue text with a distinct right-border indicator (RTL).
- Translate all remaining pages/packages fully to Arabic:
  - `PipelineBoard.tsx` (مسار الصفقات / لوحة العمليات)
  - `FollowUps.tsx` (جدول المتابعات)
  - `Campaigns.tsx` (الحملات التسويقية)
  - `Workflows.tsx` (أتمتة العمليات)
  - `KnowledgeBase.tsx` (قاعدة المعرفة والتدريب)
  - `Approvals.tsx` (إدارة الموافقات)
  - `Reports.tsx` (التقارير والإحصائيات)
- Implement AI typing indicator in `Inbox.tsx`:
  - Listen for the `AITyping` event in `SignalRService` (passes `conversationId` and `isTyping`).
  - Clear the typing status in `registerOnMessage` when an AI or Agent message arrives.
  - Display a beautiful pulsing typing bubble at the bottom of the chat log when `isAiTyping` is active.

### Backend

- Add SignalR `AITyping` broadcast event in `WebhookController.cs`:
  - Upon receiving an incoming message via `/api/webhooks/whatsapp/message`, query `ProjectSettings`.
  - If `settings != null && settings.AiAutoReplyEnabled`, send a SignalR event `AITyping` to the group `project_{projectId}` with the payload `{ conversationId = conversation.Id, isTyping = true }`.
- Save the Gemini API key supplied by the user through the standard Project Settings endpoint.

### WhatsApp Gateway

- Keep `./whatsapp-gateway/sessions:/app/sessions` volume.
- Enable safe restore with bounded reconnect attempts by setting `AUTO_RESTORE_SESSIONS=true` in Docker Compose.
- Do not clear session directories automatically during reconnect failure.

## Data Model

No new database columns are required.

Existing fields:

- `ProjectSettings.AiAutoReplyEnabled`
- `ProjectSettings.Timezone`
- `ProjectSettings.GeminiApiKey`

## Risk Plan

- **API key exposure**: UI should mask by default and no console logging of raw key.
- **Invalid session restore**: Gateway bounded reconnect prevents infinite loops.
- **Large UI conversion**: Prioritize authenticated shell and core working surfaces first: navigation, settings, inbox/customer labels where practical.
- **Build warnings**: Existing backend warnings may predate this feature. New warnings introduced by this feature must be fixed.

## Verification Plan

- `npx tsc --noEmit` in `frontend`
- `dotnet build backend/backend.csproj --no-restore`
- `node --check whatsapp-gateway/src/baileys-manager.js && node --check whatsapp-gateway/src/index.js`
- Docker compose config validation
- Browser smoke check for `http://localhost/settings` and `http://localhost/inbox` when services are running
