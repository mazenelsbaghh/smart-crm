# Developer Quickstart: Frontend Dashboard, Realtime & Production Hardening

This guide covers setup instructions, environment configurations, and validation commands for developers and administrators.

## 1. Frontend Development Setup

The frontend app runs in the `frontend/` directory.

### Installation

```bash
cd frontend
npm install
```

### Configuration

Create a `.env.local` inside the `frontend/` directory:

```env
NEXT_PUBLIC_API_URL=http://localhost:5000
NEXT_PUBLIC_WS_URL=http://localhost:5000/hubs
```

### Run Dev Server

To launch the Next.js local development environment:

```bash
npm run dev
```

The frontend dashboard will be available at `http://localhost:3000`.

---

## 2. Production Build Check

To verify compilation and optimization before deploying:

```bash
npm run build
```

This builds a production-optimized package under the `.next/` directory.

---

## 3. Production Hardening Verification

### Docker Stack Deploy

The hardened production setup uses Nginx. Run the stack in production mode:

```bash
docker-compose -f docker-compose.yml -f docker-compose.production.yml up -d --build
```

### Rate Limiting and CORS Verification

Test endpoint rate limits (requires `pytest` environment):

```bash
pytest tests/phase_6/test_zz_production.py
```

Or manually verify via `curl` by spamming requests:

```bash
for i in {1..25}; do curl -I -s http://localhost/api/health | grep HTTP; done
```

You should see `HTTP/1.1 200 OK` followed by `HTTP/1.1 429 Too Many Requests` after exceeding limits.

---

## 4. Backup & Restore Scripts

### Backup

Run the automated backup script to create a secure snapshot:

```bash
./deploy/backup.sh
```

This generates a timestamped tar archive under the `/backups` directory (e.g., `backup-20260525-010000.tar.gz`) containing:
- PostgreSQL database schema and data.
- Redis dataset.
- MinIO S3 object media files.

### Restore

To restore the system state from a backup archive:

```bash
./deploy/restore.sh /backups/backup-20260525-010000.tar.gz
```
