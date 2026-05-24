# Research Notes: Frontend Dashboard, Realtime & Production Hardening

This document records the design decisions, technology choices, and architectural rationale for the frontend implementation and production hardening.

## 1. UI Framework and Architecture

### Decision
Use **Next.js 15 (App Router)** with **TypeScript** for the client application.

### Rationale
- Next.js App Router provides optimized server-side pre-rendering (SSR) and static generation for fast initial load times.
- Native React Server Components (RSC) reduce bundle sizes by moving static layout rendering to the server.
- Built-in API routes can act as a lightweight backend-for-frontend (BFF) to securely proxy API keys if needed.

### Alternatives Considered
- **Single Page App (React + Vite)**: Good, but requires configuring custom routing, lacks built-in server optimization, and doesn't offer native server rendering.
- **ASP.NET Core Razor Pages / Blazor**: Blazor was considered, but the ecosystem, community templates, and design flexibility are currently superior in the React/Next.js environment.

---

## 2. Styling System

### Decision
Use **Vanilla CSS** with CSS Variables (Custom Properties) and CSS Modules for component isolation.

### Rationale
- Conforms directly to the project's styling guidelines.
- Allows complete control over the design system, animations, transitions, and layout structure.
- CSS Modules prevent style leakage between components, resolving naming collisions.
- Leverage HSL values for color tokens to easily construct light and dark themes.

### Alternatives Considered
- **Tailwind CSS**: Rejected because the user guidelines explicitly favor Vanilla CSS for maximal styling control and flexibility unless requested.
- **CSS-in-JS (Styled Components)**: Introduces runtime overhead and is less compatible with Next.js Server Components.

---

## 3. Realtime Connection Protocol

### Decision
Use **SignalR Client (`@microsoft/signalr`)** to establish connection to the backend notification hubs.

### Rationale
- Automatically handles fallback transports (WebSockets, Server-Sent Events, Long Polling) depending on browser capabilities.
- Handles automated reconnection, handshake, and heartbeat messages out-of-the-box.
- Seamlessly integrates with the existing C# SignalR Hubs configured in Phase 2.

### Alternatives Considered
- **Raw WebSockets**: Rejected because it requires manual implementation of reconnection logic, heartbeat pinging, and transport fallbacks.

---

## 4. Production Hardening: Nginx Proxy, Rate Limiting, & CORS

### Decision
Configure **Nginx** as the front-facing reverse proxy with TLS/SSL termination, rate-limiting directives, and CORS headers.

### Rationale
- **SSL Termination**: Nginx will serve SSL certificates (using Let's Encrypt in production) and force redirect all HTTP traffic to HTTPS.
- **Rate Limiting**: Use Nginx `limit_req_zone` to restrict client request volume (e.g., 20 req/s for API endpoints, with a burst buffer).
- **CORS Headers**: Nginx will validate incoming client origins and set strict `Access-Control-Allow-Origin`, `Access-Control-Allow-Methods`, and `Access-Control-Allow-Headers` rules.
- **Structured Error Logging**: Nginx custom error pages will mask system-level issues with clean error JSON messages.

### Alternatives Considered
- **Application-Level Rate Limiting in ASP.NET Core**: While C# has middleware for this, Nginx rejects abusive traffic before it hits the application server thread pool, preserving application performance.

---

## 5. Deployment and Backup System

### Decision
Use Docker Compose with separate configurations for dev and production. Automate backup/restore using shell scripts executing `pg_dump`, `redis-cli SAVE`, and `tar` packages.

### Rationale
- **Docker Compose**: Isolates the frontend from backend dependencies during development, but lets them run together easily.
- **Backups**: Standard utilities like `pg_dump` provide consistent relational backups, and `tar` preserves MinIO binary objects. The scripts compress and tag the package with timestamps.

### Alternatives Considered
- **Kubernetes**: Deemed over-engineered for the current single-server target environment.
- **Third-Party Backup Services**: Cloud-specific snapshot solutions are expensive and introduce provider lock-in. A self-contained bash script is portable and works on any bare-metal Linux VPS.
