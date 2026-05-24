# Quickstart Guide: Testing & Running Phase 2

This document guides you through running and verifying the AI, CRM, Scheduler, and Notification features.

## 1. Running the Services

Start all docker containers:
```bash
make env
make up
```

Verify service health:
```bash
make health
```

---

## 2. Running Phase 2 Tests

Ensure you have initialized the virtual environment:
```bash
make test-setup
```

Run all Phase 2 tests:
```bash
.venv/bin/pytest tests/phase_2/ -v --tb=short
```

---

## 3. Verifying Dashboard & SignalR

- **Hangfire Dashboard**:
  Access the background jobs dashboard at: `http://localhost:5001/hangfire` (requires admin JWT token or credentials in production).
  
- **SignalR Notification Connection**:
  Connect using a SignalR client library (e.g. `@microsoft/signalr`) to:
  `ws://localhost:5001/hubs/notifications?access_token=YOUR_JWT_TOKEN`
