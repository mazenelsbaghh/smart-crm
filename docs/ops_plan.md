# Operations & Deployment Master Plan

**Last Updated**: 2026-06-23

## Chronological Log

### 2026-06-23: Deploy UI Changes (Remove Deals & Profits) to Hostinger Production Server (Completed)
- **Goal**: Deploy the UI cleanup updates (which remove Deals, Profits, and Sales Pipeline) to the remote production environment.
- **Updates**:
  - Synced workspace source files to the remote server `147.93.86.206` using `rsync` via the deployment script.
  - Successfully rebuilt and restarted all production Docker Compose containers (backend, frontend, gateway, nginx, databases, queues).
  - Verified remote startup health with Next.js Turbopack build and backend compilation success logs.

### 2026-06-22: Resolve Hostinger Docker Compose Container Name Conflicts (Active Plan)
- **Goal**: Fix container name conflict with `smartcustomercore-elasticsearch` during remote deployment.
- **Updates**:
  - Run remote SSH command to explicitly stop and remove conflicting Elasticsearch container.
  - Restart the production Docker Compose stack cleanly.

### 2026-06-10: Firebase Cloud Messaging Server Configuration & Container Mounts (Completed)
- **Goal**: Configure the local/production Docker stack to initialize Firebase Admin by mounting the service account key file.
- **Updates**:
  - Modified `docker-compose.yml` to define `Firebase__ServiceAccountPath=/app/firebase-key.json` and mount the local credentials file `./firebase-key.json` to `/app/firebase-key.json` inside the backend container.
  - Documented production key requirement `/root/smart-crm/firebase-key.json` in the FCM implementation plan.

### 2026-06-10: GitHub Actions iOS Build Workflow (Completed)
- **Goal**: Create a GitHub Actions workflow to build the Flutter iOS application without requiring local Xcode installation or developer certificates.
- **Updates**:
  - Created `.github/workflows/build-ios.yml` supporting manual triggers (`workflow_dispatch`).
  - Configured setup steps for Flutter and building with `--no-codesign` on macOS runner.
  - Configured uploading the resulting `.ipa` file as a workflow artifact.
  - Updated `ApiClient` default `baseUrl` in the mobile app to point to the production server `https://n8n-mazen.online` to fix connection timeouts on physical iOS devices.

### 2026-06-05: SSH Login Skill (Completed)
- **Goal**: Create an agent skill (`ssh-server`) containing instructions and scripts for connecting to the remote server `147.93.86.206` using credentials in `.env.deploy`.
- **Updates**:
  - Created `.agents/skills/ssh-server/` directory and wrote `SKILL.md` documenting connection details, credentials, and common command patterns.
  - Implemented `scripts/ssh-interactive.sh` for interactive remote terminal sessions.
  - Implemented `scripts/ssh-cmd.sh` for non-interactive remote command executions.
  - Verified connection by removing a conflicting elasticsearch container on the production host and successfully completing the production deployment.



### 2026-06-05: Manual Deployment Script (Completed)
- **Goal**: Create a manual deployment script in `ops/deploy.sh` that automates file synchronization and Docker container rebuilds.
- **Updates**:
  - Wrote `ops/deploy.sh` containing the optimized `rsync` commands and container execution sequence.
  - Made the script executable (`chmod +x ops/deploy.sh`).

### 2026-06-05: Domain Configuration and SSL Setup (Completed)
- **Goal**: Configure domain `n8n-mazen.online` and obtain Let's Encrypt SSL certificates.
- **Updates**:
  - Updated `nginx/production.conf` and `nginx/default.conf` server_name and CORS origins to include `n8n-mazen.online` and `www.n8n-mazen.online`.
  - Installed `certbot` on the server host.
  - Temporarily stopped Nginx container, ran `certbot certonly --standalone` to obtain a valid SSL certificate for `n8n-mazen.online` and `www.n8n-mazen.online`.
  - Copied Let's Encrypt certificate/key files directly into `./nginx/certs/` to be mounted by the Nginx container.
  - Started Nginx container and verified HTTPS access works perfectly (returning 200 OK).

### 2026-06-05: Manual Deployment and Server Recovery (Completed)
- **Goal**: Manually deploy updates to the Hostinger server `147.93.86.206` via local rsync and rebuild docker services.
- **Updates**:
  - Installed missing dependency `make` on the Hostinger server to support Makefiles.
  - Installed Docker CE and Docker Compose plugin on the Hostinger server.
  - Deployed the application files using `rsync` from the local workspace (with optimized exclusions for `.next` caches, node modules, and git files).
  - Successfully built and started the production Docker containers on the server.
  - Verified remote health status endpoint returns HTTP 200 OK.
  - Integrated local and remote branches and pushed the updates to GitHub via `make push`.



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
