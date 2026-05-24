# Implementation Plan: Frontend Dashboard, Realtime & Production Hardening

**Branch**: `007-frontend-production` | **Date**: 2026-05-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/007-frontend-production/spec.md`

## Summary

Build a modern web interface for Smart Customer Core using React/Next.js, styled with Vanilla CSS and responsive design layouts. Integrate SignalR for realtime agent messaging and status updates. Secure the production environment with Nginx (SSL termination, rate limiting, and CORS headers), and implement robust backup and restore scripts for PostgreSQL, Redis, and MinIO storage.

## Technical Context

**Language/Version**: JavaScript/TypeScript (Node.js v20+, React 19, Next.js 15), C# (.NET 9.0) for backend.

**Primary Dependencies**:
- `@microsoft/signalr` (v8) for real-time WebSocket connection to the backend.
- `axios` (v1.7) for API requests and interceptors (token refresh).
- `lucide-react` (v0.400) for UI icons.
- `Vanilla CSS` for styling (no Tailwind CSS, in accordance with the web application development guidelines).

**Storage**: LocalStorage / SessionStorage for client JWT tokens. Backend uses PostgreSQL, Redis, Elasticsearch, and MinIO.

**Testing**: Python (`pytest` + `httpx` + `pytest-asyncio` + `websockets`) for automated integration testing of rate limiting, CORS headers, SSL endpoints, and backup/restore scripts.

**Target Platform**: Linux Server (Docker/Docker Compose, Nginx).

**Project Type**: Next.js Web Application + Nginx Reverse Proxy.

**Performance Goals**:
- Page transitions and metrics rendering in under 1 second.
- Real-time message reception in the inbox UI within 500ms of backend webhook ingest.
- Full backup and restore operations completed within 5 minutes.

**Constraints**: Strict multi-tenant project context scoping via HTTP header `X-Project-Id` or extracted from the JWT token.

## Constitution Check

- [x] **Modular Monolith Architecture**: The frontend communicates only with the backend's API layer, keeping clear boundary separation. Nginx orchestrates container networking.
- [x] **Strict Multi-Tenant Project Isolation**: Every client request is authenticated, and requests are scoped via `X-Project-Id` or verified backend JWT project claims.
- [x] **Gemini 3.5 Flash Unified AI Engine**: Realtime inbox fetches suggestions generated from the backend's Gemini service.
- [x] **Human-Like Messaging and Aggregation**: Message composition and media handling route through the backend's delay and anti-ban engines.
- [x] **Risk-Based Action Approval System**: The supervisor view contains an approvals queue to accept/reject AI proposals.

## Project Structure

### Documentation (this feature)

```text
specs/007-frontend-production/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technology decisions & rationale
├── data-model.md        # Client-side data models & SignalR events
├── quickstart.md        # Developer setup & validation commands
└── contracts/
    └── api.md           # API endpoints & proxy routes schema
```

### Source Code (repository root)

```text
frontend/
├── Dockerfile
├── package.json
├── next.config.ts
├── postcss.config.js
├── app/                  # Next.js App Router (Vanilla CSS)
│   ├── layout.tsx
│   ├── page.tsx          # Login
│   ├── register/
│   │   └── page.tsx
│   ├── dashboard/
│   │   ├── layout.tsx
│   │   └── page.tsx
│   ├── inbox/
│   │   └── page.tsx      # Realtime 3-panel chat
│   ├── crm/
│   │   ├── page.tsx      # Customers
│   │   └── pipeline/
│   │       └── page.tsx  # Kanban board
│   ├── management/
│   │   ├── follow-ups/
│   │   ├── campaigns/
│   │   ├── reports/
│   │   ├── workflows/
│   │   ├── knowledge/
│   │   └── approvals/
│   └── settings/
└── src/
    ├── components/       # Custom reusable inputs, tables, cards
    ├── services/         # api.ts, auth.ts, signalr.ts
    ├── context/          # auth-context.tsx
    └── styles/           # variables.css, global.css, components.css
```

**Structure Decision**: Web application layout. Next.js project placed under `frontend/`. Custom Nginx proxy configuration placed under `nginx/` in the root. Production scripts placed under `deploy/`.

## Complexity Tracking

*No violations of the Constitution identified.*
