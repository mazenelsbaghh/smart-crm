---
name: "phase-1"
description: "Smart Customer Core - Phase 1: Core Foundation (Auth, Projects, WhatsApp Gateway, Conversations, AI Auto-Reply & CRM)"
compatibility: "Smart Customer Core net9.0 + Node.js Baileys Gateway"
metadata:
  author: "community"
  source: "phase-1/SKILL.md"
---

# Phase 1: Core Foundation

This skill documents the features, API architecture, running procedures, and verification methods for **Phase 1: Core Foundation**.

---

## 🚀 What Was Built

### 1. Project Scaffolding & Shared Infrastructure
- **C# Web API Backend (.NET 9)** with Entity Framework Core, PostgreSQL, Redis, RabbitMQ, and StackExchange.Redis.
- **Node.js WhatsApp Gateway (Express + @whiskeysockets/baileys)** for connection management and mock messaging.
- **Multi-Tenant Isolation**: Enforced at the DbContext level using dynamic global query filters. Each request context resolves the project tenant via HTTP headers (`X-Project-Id`) or JWT claims.
- **Event Bus**: Integration events are decoupled and routed asynchronously via a custom RabbitMQ Direct Exchange (`smartcore_exchange`).

### 2. Implemented Modules & Features
- **Auth Module**: Registered and authenticated platform users via JWT access + refresh tokens. Roles claims and project contexts are automatically injected.
- **Projects Module**: Isolated project instances and project settings (including AI auto-reply switches and API keys).
- **WhatsApp Gateway**: Exposed Express routes to initialize sessions, check statuses, send messages, and mock session connections. Includes an anti-race mechanism that prevents background connection timeouts from wiping mock session credentials during testing.
- **Conversations & Webhook Ingestion**: Receives WhatsApp message payloads, automatically creates customers and threads, saves message records, and passes them to the Redis aggregator.
- **Redis Aggregator**: Buffers incoming messages and triggers a 5-second silence aggregation window before emitting a `MessageAggregatedEvent`.
- **AI Auto-Reply**: Background workers consume aggregated messages, call Google Gemini 3.5 Flash (with mock fallback for sandbox testing), generate replies, and publish `AIReplyGeneratedEvent` to send messages.
- **CRM & Follow-Ups**: Customer CRUD and metadata updates. Scheduling of overdue/future follow-ups with a background worker (`FollowUpScheduler`) checking and marking past-due follow-ups as `Missed`.

---

## 🛠️ Makefile Targets

Use the root-level `Makefile` to manage the stack:

```bash
make up               # Spin up the entire Docker infrastructure
make down             # Stop and tear down all service containers
make restart          # Re-build and restart all containers
make health           # Verify health checks for all infrastructure services
make test-setup       # Configure Python venv and install dependencies
make test-all         # Run all integration tests (Phase 0 + Phase 1)
```

---

## 📡 API Endpoints

### Auth Module
- `POST /api/auth/register` - Create user
- `POST /api/auth/login` - Get JWT and Refresh tokens
- `POST /api/auth/refresh` - Refresh access token
- `POST /api/auth/logout` - Revoke tokens

### Projects Module
- `POST /api/projects` - Create project
- `GET /api/projects` - List projects
- `GET /api/projects/{id}` - Get project details
- `PUT /api/projects/{id}/settings` - Update settings (AI toggle, API Key)

### WhatsApp Gateway
- `POST /api/whatsapp/session/start` - Initialize session
- `GET /api/whatsapp/session/qr` - Retrieve QR code
- `GET /api/whatsapp/session/status` - Check connection status
- `POST /api/whatsapp/send` - Send a text message

### CRM & Follow-Ups
- `GET /api/projects/{projectId}/customers` - List customers
- `GET /api/customers/{id}` - Get customer details
- `PUT /api/customers/{id}` - Update customer details
- `POST /api/customers/{customerId}/follow-ups` - Schedule follow-up
- `GET /api/projects/{projectId}/follow-ups` - List follow-ups by status

### Webhook
- `POST /api/webhooks/whatsapp/message` - Ingest gateway message

---

## 🧪 Verification & Testing

### Automated Tests
Run the entire suite of integration tests covering the complete message lifecycle:
```bash
make test-all
```

The suite covers:
1. **Infrastructure Health**: DB, Redis, RabbitMQ, Elasticsearch, MinIO connectivity.
2. **Auth Flow**: Registration, login, token refresh lifecycle.
3. **Project Isolation**: Confirming tenant context isolation rules.
4. **WhatsApp Session**: Gateway session initialization and mocking.
5. **Message Webhook & Aggregation**: Consecutive message buffer windows.
6. **Gemini AI Auto-Reply**: Context formatting, AI client mock generation, and reply execution.
7. **CRM & Follow-Ups**: Customer profile updates and the Hangfire-equivalent background scheduler.
