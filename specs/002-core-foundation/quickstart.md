# Phase 1 Quickstart: Core Foundation

## 1. Prerequisites
- Docker & Docker Compose
- Node.js (v20+)
- .NET 8 SDK (for local development, though runs inside Docker)
- Python 3.14 (with venv for running integration tests)
- A WhatsApp number to link (for manual verification, or test harness mocks)

---

## 2. Running the Infrastructure & Applications

### Step 2.1: Initialize Environment Variables
Ensure `.env` exists:
```bash
make env
```
Make sure you update the placeholder secret keys (like `POSTGRES_PASSWORD`, `MINIO_ROOT_PASSWORD`, etc.) in `.env`.

### Step 2.2: Build and Run Services
Run the following to start all running services, including databases, queue, Nginx, the C# ASP.NET Core backend, and the Node.js WhatsApp Gateway:
```bash
make up
```

---

## 3. Database Migrations & Seeding
Once the backend container is running, run migrations and seed initial tenant data (default project and users):
```bash
make db-migrate
make db-seed
```

---

## 4. WhatsApp Session Authentication (QR Code)
To connect a WhatsApp Gateway session for a project:
1. Trigger a start session request:
   ```bash
   curl -X POST http://localhost/api/whatsapp/session/start -d "projectId=RESOLVED_PROJECT_UUID"
   ```
2. Retrieve the scannable QR code:
   ```bash
   curl -X GET http://localhost/api/whatsapp/session/qr?projectId=RESOLVED_PROJECT_UUID
   ```
3. Scan the QR code using your WhatsApp app (Linked Devices -> Link a Device).

---

## 5. Verification Checkpoints

### 5.1 Automated Health Checks
Run the Makefile target to ensure all microservices and databases are fully operational:
```bash
make health
```

### 5.2 Running Integration Tests
Execute the cumulative pytest harness including both Phase 0 infrastructure health tests and Phase 1 functional API endpoints tests:
```bash
make test-setup
make test-phase-1
```
