# Quickstart: Shared Assets, Media Engine & Audit Trail

This document outlines the steps required to verify the Shared Asset Management System, Media Transformation Engine, Audit Logging, and System Health monitoring in development.

## Setup Requirements

Before testing Phase 5, verify that all external infrastructure services are healthy:
- **MinIO** is running on port `9000` (Console on `9001`).
- **Elasticsearch** is running on port `9200`.
- **RabbitMQ** is running on port `5672` (management UI on `15672`).
- **PostgreSQL** database is migrated and seeded.

```bash
make up
make db-migrate
make db-seed
```

## Running Verification Tests

Run the complete suite of Phase 5 tests:

```bash
make test-phase-5
```

This command executes:
- Asset uploading to MinIO, deduplication via SHA-256 hashes, and signed URL generation.
- Media transformations including thumbnail creation and resizing for WhatsApp compatibility.
- Audit trail writing on CRM change events, indexing to Elasticsearch, and querying.
- System health checks and telemetry metrics verification.

## API Execution Flow (Manual Testing)

### 1. Upload an Asset
```bash
curl -X POST http://localhost:5000/api/assets/upload \
  -H "Authorization: Bearer YOUR_TOKEN" \
  -F "file=@/path/to/image.jpg" \
  -F "projectId=YOUR_PROJECT_ID"
```

### 2. Download an Asset (Signed URL)
```bash
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://localhost:5000/api/assets/YOUR_ASSET_ID/download
```

### 3. Get Thumbnail
```bash
curl -H "Authorization: Bearer YOUR_TOKEN" \
  http://localhost:5000/api/assets/YOUR_ASSET_ID/thumbnail --output thumb.jpg
```

### 4. Query Audit logs
```bash
curl -H "Authorization: Bearer YOUR_TOKEN" \
  "http://localhost:5000/api/projects/YOUR_PROJECT_ID/audit?action=CustomerUpdate"
```

### 5. Check System Health
```bash
curl http://localhost:5000/api/system/health
```
