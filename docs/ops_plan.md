# Operations & Deployment Master Plan

**Last Updated**: 2026-05-25

## Chronological Log

### 2026-05-25: Phase 6 Frontend Dashboard, Realtime & Production Hardening (Planned)
- **Goal**: Configure Nginx reverse proxy with SSL termination, CORS controls, and rate-limiting zones; write automated backup/restore scripts for PostgreSQL, Redis, and MinIO storage; containerize frontend application with Docker.
- **Updates**:
  - Create Nginx configuration at `nginx/production.conf`.
  - Add backup and restore script files `deploy/backup.sh` and `deploy/restore.sh`.
  - Extend `docker-compose.production.yml` with the frontend container and Nginx ingress router.
  - Update `Makefile` to include `make deploy` (production deployment build), `make backup` (full system state backup), and `make restore` (full state restore).

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
