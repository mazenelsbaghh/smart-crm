# Implementation Plan: Core Foundation

**Branch**: `phase/1-core-foundation` | **Date**: 2026-05-24 | **Spec**: [spec.md](./spec.md)

**Input**: Feature specification from `/specs/002-core-foundation/spec.md`

## Summary

This plan sets up the core modular monolith architecture in ASP.NET Core, the Node.js Baileys WhatsApp Gateway, and integrates Redis-based message aggregation and the Gemini 3.5 Flash unified AI engine. 

- **Backend**: ASP.NET Core 8 Web API.
- **WhatsApp Service**: Node.js + Baileys gateway.
- **DB/Queue/Cache**: PostgreSQL (with Vector extension), RabbitMQ, Redis.
- **AI**: Google Gemini 3.5 Flash integration.

## Technical Context

**Language/Version**: C# (.NET 8), JavaScript/TypeScript (Node.js v20)

**Primary Dependencies**: 
- Backend: Microsoft.EntityFrameworkCore (v8), Npgsql.EntityFrameworkCore.PostgreSQL (v8), BCrypt.Net-Next (v4.0.3), System.IdentityModel.Tokens.Jwt (v8), RabbitMQ.Client (v6.8), StackExchange.Redis (v2.8)
- WhatsApp Service: @whiskeysockets/baileys (v6), express, redis, axios

**Storage**: PostgreSQL (Primary database), Redis (Aggregation cache and session state), local storage (for WhatsApp sessions)

**Testing**: pytest (running on host testing the containerized API endpoints)

**Target Platform**: Linux Server (Ubuntu) / macOS (Docker/Docker Compose containerized environment)

**Project Type**: Web Services (ASP.NET Core Monolith + Node.js Microservice Gateway)

**Performance Goals**: 
- Endpoint responses < 200ms
- Message aggregation latency: 5-second silence window from customer
- AI Auto-Reply trigger within 1.5 seconds of aggregation window completion

**Constraints**:
- Strict tenant isolation: All queries MUST scope to `ProjectId`
- Event-driven: Inter-module communication must use RabbitMQ event publisher/subscriber pattern.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- **Modular Monolith**: Yes. Core modules (Auth, Projects, Users, WhatsApp, Conversations, AI, CRM, FollowUp) are isolated inside `backend/src/Modules/` and communicate via event queues.
- **Strict Multi-Tenant Project Isolation**: Yes. All tables contain `ProjectId` and all database queries filter by active Project context.
- **Gemini 3.5 Flash Unified AI Engine**: Yes. The AI module connects directly to Gemini 3.5 Flash for message analysis and responses, without separate OCR/STT.
- **Human-Like Messaging and Aggregation**: Yes. Message Aggregator will use Redis to buffer messages and wait for a 5-second idle window.
- **Risk-Based Action Approval System**: Yes. Any action is routed through a risk analyzer check (though in Phase 1 we focus on auto-replies).

## Project Structure

### Documentation (this feature)

```text
specs/002-core-foundation/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technology choice records
├── data-model.md        # DB Entity definitions and schemas
├── quickstart.md        # How to run and test Phase 1
└── contracts/           # API contract definitions (schemas/JSON)
    ├── auth.json
    ├── projects.json
    ├── crm.json
    └── gateway.json
```

### Source Code (repository root)

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── Auth/
│   │   ├── Projects/
│   │   ├── Users/
│   │   ├── WhatsApp/
│   │   ├── Conversations/
│   │   ├── Messages/
│   │   └── AI/
│   ├── Shared/
│   │   ├── Domain/
│   │   ├── Application/
│   │   ├── Infrastructure/
│   │   ├── Events/
│   │   ├── Queue/
│   │   ├── Security/
│   │   └── Common/
│   └── Program.cs
├── Dockerfile
└── backend.csproj

whatsapp-gateway/
├── src/
│   ├── index.js
│   ├── baileys-manager.js
│   └── redis-client.js
├── Dockerfile
└── package.json
```

**Structure Decision**: Option 2 (Web application with C# ASP.NET Core backend and Node.js WhatsApp Gateway running as separate services orchestrating through Docker Compose).
