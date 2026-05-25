# Implementation Plan: Fix WhatsApp Gateway Message Sending and Receiving

**Branch**: `010-fix-whatsapp-gateway` | **Date**: 2026-05-25 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/010-fix-whatsapp-gateway/spec.md`

## Summary

Fix the message sending and receiving functionality of the Node.js `whatsapp-gateway` which uses the `@itsukichan/baileys` library. This involves:
1. Formatting the recipient phone number to the correct JID format `number@s.whatsapp.net` by removing non-numeric characters (`+`, spaces, hyphens, etc.).
2. Validating connection status before executing `sendMessage` and throwing appropriate errors if offline or not initialized.
3. Hooking up Baileys events like `creds.update` and socket state changes correctly to maintain and restore connections.
4. Parsing incoming message text/media structures accurately in `messages.upsert` and forwarding to the backend webhook.

## Technical Context

**Language/Version**: JavaScript (ES Modules, Node.js v20+)

**Primary Dependencies**: `@itsukichan/baileys` (v7.3.2), `express` (v4.19.2), `axios` (v1.6.8)

**Storage**: Local files `/app/sessions/<projectId>/` for authentication state.

**Testing**: Pytest integration tests under `tests/phase_1/test_whatsapp_gateway.py`.

**Target Platform**: Linux Server (Docker/Docker Compose).

**Project Type**: REST API Web Service.

**Performance Goals**:
- JID parsing and formatting in under 5ms.
- Connection auto-restore within 5s of container startup.

**Constraints**:
- Keep Node.js gateway stateless except for localized file-based WhatsApp authentication folders.
- Strict mapping of JID to prevent protocol-level payload rejections from WhatsApp servers.

## Constitution Check

- **Modular Monolith Architecture**: The gateway is a separate service and does not access the SQL database directly. It communicates with the backend only via HTTP requests. (Pass)
- **Strict Multi-Tenant Project Isolation**: Every session is isolated in its own sub-folder `/app/sessions/<projectId>` based on the tenant's `projectId`. (Pass)
- **Human-Like Messaging and Aggregation**: The gateway forwards raw incoming messages to the backend webhook, which performs the 5-second silence aggregation window before triggering Gemini. (Pass)

## Project Structure

### Documentation (this feature)

```text
specs/010-fix-whatsapp-gateway/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technology decisions & rationale
├── data-model.md        # Client-side data models & API responses
├── quickstart.md        # Developer setup & validation commands
└── checklists/
    └── requirements.md  # Specification Quality Checklist
```

### Source Code (repository root)

```text
whatsapp-gateway/
├── Dockerfile
├── package.json
└── src/
    ├── index.js             # Express app routing and startup session restore
    └── baileys-manager.js   # Baileys socket creation, event listeners, and send/receive logic
```

**Structure Decision**: Node.js microservice. Modify `src/baileys-manager.js` and `src/index.js` in place to implement formatting, robust event handling, and error handling.

## Complexity Tracking

*No violations of the Constitution identified.*
