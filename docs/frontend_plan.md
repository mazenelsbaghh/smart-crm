# Frontend Master Plan

**Last Updated**: 2026-05-25

This document tracks all frontend requirements, design structures, pages, and implementation details.

---

## Chronological Log

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
pytest tests/phase_6/test_production.py
```
