# Frontend Master Plan

**Last Updated**: 2026-05-25

This document tracks all frontend requirements, design structures, pages, and historical updates.

## Chronological Log

### 2026-05-25: Phase 6 Frontend Dashboard, Realtime & Production Hardening (Planned)
- **Goal**: Create a Next.js application side-by-side with backend containers, implement secure authentication client routing, build a real-time 3-panel chat inbox using SignalR, and deliver a clean CRM pipeline Kanban board.
- **Pages**:
  - `frontend/app/page.tsx` — Login Form
  - `frontend/app/register/page.tsx` — User Registration Form
  - `frontend/app/dashboard/page.tsx` — Performance Metrics & quick links
  - `frontend/app/inbox/page.tsx` — Chat Inbox (real-time updates, AI replies, attachment upload)
  - `frontend/app/crm/page.tsx` — Customers management
  - `frontend/app/crm/pipeline/page.tsx` — Deals Kanban board pipeline
- **Services**:
  - `frontend/src/services/auth.ts` — Authentication handlers & refresh tokens
  - `frontend/src/services/api.ts` — Centralized API calls configuration
  - `frontend/src/services/signalr.ts` — WebSocket / SignalR client connection logic
- **Components**:
  - `frontend/src/components/CustomerDetail.tsx` — Customer sidebar profile inspector
- **Styling**:
  - `frontend/src/styles/variables.css` — Theme configurations, neon colors, sizing scales
