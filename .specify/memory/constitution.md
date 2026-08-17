<!--
SYNC IMPACT REPORT
==================
- Version change: v1.0.0 -> v2.0.0
- List of modified principles:
  - V. Risk-Based Action Approval System (Human-in-the-Loop) -> V. Risk-Based Action Authorization & Bounded Autonomy
- Added sections: None
- Removed sections: None
- Templates requiring updates:
  - .specify/templates/plan-template.md: ✅ updated / verified
  - .specify/templates/spec-template.md: ✅ updated / verified
  - .specify/templates/tasks-template.md: ✅ updated / verified
- Follow-up TODOs: None
-->

# Smart Customer Core Constitution

## Core Principles

### I. Modular Monolith Architecture
The backend application MUST be structured as a Modular Monolith inside a single ASP.NET Core project. Domain boundaries (Auth, Projects, WhatsApp, Conversations, AI, CRM, Workflows, etc.) must be strictly separated. Modules MUST NOT reference each other's database tables or internal services directly. All inter-module communication MUST occur asynchronously using event-driven architecture via RabbitMQ.
*Rationale: Keeps the codebase modular and organized, preventing tight coupling while ensuring a smooth future scalability path to microservices.*

### II. Strict Multi-Tenant Project Isolation
Data separation per project MUST be absolute. All database tables, users, roles, settings, customers, conversations, CRM fields, and knowledge bases MUST be explicitly isolated and linked to a unique `ProjectId`. Users are restricted to a single project context, and no data or context can ever leak across project boundaries.
*Rationale: Smart Customer Core is a unified internal CRM for multiple independent business projects, making data security and isolation a critical non-negotiable priority.*

### III. Gemini 3.5 Flash Unified AI Engine
All unstructured inputs, including Text, Images, and Audio/Voice Notes, MUST be sent directly to the Gemini 3.5 Flash model. The system MUST NOT use separate OCR or Speech-to-Text engines (like Whisper).
*Rationale: Minimizes infrastructure footprint, avoids multiple API integrations, and leverages the native multi-modal capabilities of Gemini to reduce processing latency.*

### IV. Human-Like Messaging and Aggregation
The WhatsApp module MUST aggregate consecutive messages from the same sender over a dynamic window (3-10s) to understand overall intent before generating a reply. AI-generated replies MUST be sent in natural chunks with realistic typing delays.
*Rationale: Simulates genuine human conversation, prevents flooding the customer with multiple disjointed messages, and protects the WhatsApp numbers from being flagged or banned.*

### V. Risk-Based Action Authorization & Bounded Autonomy
Every AI action MUST pass through a deterministic Risk Analyzer and MUST produce a durable audit record. Low-risk actions such as tagging, notes, and lead scoring MAY execute immediately. Medium-risk actions such as CRM updates and supervisor transfers MAY execute within code-owned validation and audit controls. High-risk or critical actions such as marketing campaigns, spend changes, discounts, price changes, or system data modifications MUST satisfy one of the following before execution:

1. A supervisor or administrator explicitly approves the individual action; or
2. An Owner or Admin has already granted an active, specific, time-bounded authorization envelope that defines the permitted project, action types, resources, platforms, financial caps, maximum change size, commercial facts, and emergency-stop conditions.

Actions inside a valid authorization envelope MAY execute autonomously only after independent review and deterministic safety checks. Any action outside the envelope, any attempt to broaden it, any action taken while required tracking or connection health is unsafe, or any action after the envelope expires MUST stop and require new authorized approval. Authorization envelopes MUST be revocable immediately, MUST NOT permit cross-project access, MUST NOT override protected business facts, and MUST NOT allow destructive deletion by AI.

*Rationale: Preserves accountable human control over risk boundaries while allowing explicitly authorized automation to operate safely without requiring repetitive approval for every bounded action.*

## Tech Stack & Infrastructure

The project runs on a single Ubuntu server using Docker/Docker Compose:
- **Backend**: ASP.NET Core API + Hangfire Background Worker Services.
- **WhatsApp Service**: Node.js + Baileys library gateway.
- **Databases**: PostgreSQL (primary with `pgvector`), Elasticsearch (search indexing), Redis (caching, SignalR scale-out, rate-limiting, and temporary aggregation).
- **Queues**: RabbitMQ for asynchronous event-driven queues.
- **Storage**: Local S3-compatible Object Storage for media.

## Development Rules & Best Practices

- **DRY & Shared Logic**: Common components, domain entities, database contexts, and queues must live in the `Shared` folder. Modules must not reference each other directly; they must communicate asynchronously via events.
- **Audit & Traceability**: All API requests, user updates, AI decisions, and critical status changes must produce structured events logged to PostgreSQL and indexed in Elasticsearch for audit trail and compliance.
- **Security Constraints**: Strict JWT-based authentication with refresh tokens. Input validation on both gateway and backend layers. Secrets and API keys must be encrypted and stored in environment variables, never committed to code.

## Governance

All pull requests and code modifications must verify compliance against this Constitution. Any updates to this document require an explicit version increment (`CONSTITUTION_VERSION`), updating of ratification and amendment dates, and updating dependent templates in `.specify/templates/`.

**Version**: 2.0.0 | **Ratified**: 2026-05-24 | **Last Amended**: 2026-08-17
