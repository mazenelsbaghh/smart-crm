# Quickstart: Fix WhatsApp Gateway

This guide covers setup and verification commands for the WhatsApp Gateway.

## Setup & Running locally

Start the WhatsApp gateway service in development mode:

```bash
cd whatsapp-gateway
npm install
npm run dev
```

Or using Docker Compose:

```bash
docker compose up -d whatsapp-gateway
```

## Verification Commands

To check the status of a WhatsApp session:

```bash
make whatsapp-status PROJECT_ID=<uuid>
```

To run Phase 1 tests (which cover the WhatsApp gateway APIs):

```bash
make test-phase-1
```
