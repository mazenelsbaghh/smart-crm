# Technical Research: Phase 2 AI & Scheduling Infrastructure

This document details technology choices, designs, and decisions for the implementation of Phase 2 features.

## 1. Hangfire PostgreSQL Storage & Dashboard Auth

### Decision
Use `Hangfire.PostgreSql` storage provider to run background jobs, and implement a custom `IDashboardAuthorizationFilter` to protect `/hangfire`.

### Rationale
- Stores jobs directly in PostgreSQL, reducing infrastructure overhead (no need for a separate database).
- Uses the same connection string as the app database, making setup simple.
- Dashboard authentication must verify JWT tokens or Admin roles to prevent unauthorized access.

### Dashboard Auth Filter Implementation Pattern
Since Hangfire Dashboard is rendered as HTML, we will implement a custom filter that validates the token from cookies or query string, falling back to basic authentication if not running in development.

---

## 2. SignalR JWT WebSocket Authentication

### Decision
Use standard ASP.NET Core SignalR and configure JWT Bearer authentication to intercept the query string `access_token`.

### Rationale
- Browsers do not support custom HTTP headers on WebSocket handshakes.
- SignalR client libraries pass the JWT in the query string parameter.
- The backend configuration must map `context.Token` to `access_token` during `OnMessageReceived`.

---

## 3. Human-Like Messaging (Reply Chunking & Typing Delays)

### Decision
- **Chunking**: Split reply strings by newline characters `\n` or sentences (`.`, `!`, `?` followed by space) if the reply exceeds 150 characters.
- **Delay Calculation**: Implement typing speed simulator calculated as `Math.Min(characterCount * 50, 4000)` milliseconds (approx. 50ms per character, capped at 4 seconds).
- **Throttling**: Cache last sent timestamp per session in Redis to ensure a minimum gap of 1 second between chunks.

---

## 4. Agent Presence & Routing

### Decision
Use Redis hashes `project:{projectId}:agents` to track presence, active loads, and routing order.
- **Heartbeat**: Agents ping `POST /api/presence` every 30 seconds. Redis records expire after 60 seconds.
- **Workload Routing**: Route conversations to the agent with the lowest number of assigned active conversations.

---

## 5. CRM Auto-Update Extraction Prompt

### Decision
Gemini 3.5 Flash prompt enforces structured JSON output:
```json
{
  "city": "string | null",
  "budget": "decimal | null",
  "interests": "string[]",
  "timeline": "string | null",
  "confidence": 0.95
}
```
If confidence is < 0.8, the proposal status is set to `PendingApproval` instead of automatically applying the values.
