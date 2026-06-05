# Operations & Deployment Master Plan

**Last Updated**: 2026-06-05

## Chronological Log

### 2026-06-05: Manual Deployment and Server Recovery (In Progress)
- **Goal**: Manually deploy updates to the Hostinger server `147.93.86.206` via local rsync and rebuild docker services.
- **Updates**:
  - Run rsync to sync all code changes to server root (excluding caches/node_modules/dotenv).
  - Rebuild and restart the Docker stack on the server using SSH.
  - Check container health.



### 2026-06-02: Resolve Frontend CORS & Network Errors & Enable WhatsApp Seen Status (Completed)
- **Goal**: Address frontend Axios "Network Error" issues, SignalR drops, and enable WhatsApp "seen" status (read receipts) for incoming messages.
- **Updates**:
  - Configured ASP.NET Core backend `Program.cs` CORS policy to use dynamic origin matching (`SetIsOriginAllowed(origin => true)`) to natively support HTTP/HTTPS schemas, local IP redirects, and custom staging/production domains.
  - Added missing `Access-Control-Allow-Credentials: true` to the Nginx OPTIONS preflight handler inside `nginx/production.conf` and `nginx/default.conf` so browser CORS verification passes on authenticated requests.
  - Aligned development Nginx configuration `nginx/default.conf` with `production.conf` by removing redundant duplicate CORS headers for non-preflight requests, preventing console CORS block errors.
  - Implemented automatic read receipts ("seen" status) in the WhatsApp gateway's `baileys-manager.js` by invoking `sock.readMessages([msg.key])` on every incoming WhatsApp message. This immediately marks messages as read/seen (triggering double blue checkmarks for the customer).

### 2026-05-25: Phase 6 Frontend Dashboard, Realtime & Production Hardening (Completed)
- **Goal**: Configure Nginx reverse proxy with SSL termination, CORS controls, and rate-limiting zones; write automated backup/restore scripts for PostgreSQL, Redis, and MinIO storage; containerize frontend application with Docker.
- **Updates**:
  - Created hardened production Nginx reverse proxy configuration at `nginx/production.conf` supporting SSL/TLS redirects, custom origin-controlled CORS policies, active SignalR hubs WS tunneling, rate-limiting zone rules (100 requests/minute, burst=20) and standard HTTP 429 status response.
  - Wrote automated backup script `deploy/backup.sh` to package PostgreSQL dump, Redis memory snapshots, and MinIO storage files into a single timestamped gzip archive.
  - Wrote automated restore script `deploy/restore.sh` to clean and reinitialize Postgres DB, copy Redis/MinIO assets, and restart the backend service container with startup delay (`sleep 8`) to gracefully rebuild the connections pool and avoid 502 Bad Gateway errors.
  - Created root-level and deploy-level `docker-compose.production.yml` configuration files to orchestrate the backend, database, caching, object storage, Next.js frontend app, and Nginx containers.
  - Fixed a critical Docker Compose environment override issue where the list syntax in `docker-compose.production.yml` wiped out environment variables defined in the base `docker-compose.yml` for the backend service. Converted the backend `environment` fields to map format in both compose configurations.
  - Created multi-stage `frontend/Dockerfile` to compile and package Next.js production builds.
  - Updated repository `Makefile` by adding targets: `make deploy` (production docker compose deployment), `make backup` (full state backup execution), `make restore FILE=...` (full state recovery), and `make test-phase-6` (Phase 6 individual test verification).

### 2026-05-25: Phase 5 Shared Assets, Media Engine & Audit Trail (Completed)
- **Goal**: Integrate MinIO S3-compatible file storage with the backend container, configure connection environment variables in `.env` and `docker-compose.yml`, set up Serilog logging output configurations, and add Make targets for managing media assets, audit queries, and system health checks.
- **Updates**:
  - Expose MinIO console and API environment overrides in `.env.example`.
  - Update `Makefile` to include targets: `make asset-stats` (get asset storage summary), `make audit-report` (extract audit trail reports), `make system-health` (fetch endpoint check statuses), and `make test-phase-5` (run cumulative Phase 5 verification test suite).

### 2026-05-25: Phase 4 Elasticsearch & Campaign Infrastructure (Completed)
- **Goal**: Integrate Elasticsearch 8.x search indexing service with the backend container, configure connection strings, and add Make targets for managing search and campaign health.
- **Updates**:
  - Verify Elasticsearch 8.x container configuration in `docker-compose.yml` and check port limits (exposing 9200 for local queries).
  - Update `Makefile` to include targets: `make campaign-status` (check running/completed campaigns), `make analytics-dashboard` (trigger daily snapshots manually), `make search-reindex` (clear and rebuild Elasticsearch indices), and `make test-phase-4` (run cumulative Phase 4 verification test suite).

### 2026-05-24: Phase 2 Service Orchestration & Scheduler
- **Goal**: Support Hangfire Dashboard, SignalR WebSocket transport, and environment-level settings for Phase 2.
- **Updates**:
  - Integrate Hangfire storage using PostgreSQL connection string in `docker-compose.yml` and `appsettings.json`.
  - Expose `/hangfire` route through Nginx proxy configuration if routing rules require it.
  - Expose SignalR endpoints (`/hubs/notifications`) through Nginx proxy, ensuring proper WebSocket upgrade headers (`Upgrade`, `Connection`).
  - Add `make scheduler-status` command to verify Hangfire background jobs and RabbitMQ queues.
