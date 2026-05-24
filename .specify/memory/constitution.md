<!--
SYNC IMPACT REPORT
==================
- Version change: Initial Template -> v1.0.0
- List of modified principles:
  - [PRINCIPLE_1_NAME] -> I. Modular Monolith Architecture
  - [PRINCIPLE_2_NAME] -> II. Strict Multi-Tenant Project Isolation
  - [PRINCIPLE_3_NAME] -> III. Gemini 3.5 Flash Unified AI Engine
  - [PRINCIPLE_4_NAME] -> IV. Human-Like Messaging and Aggregation
  - [PRINCIPLE_5_NAME] -> V. Risk-Based Action Approval System (Human-in-the-Loop)
- Added sections:
  - Tech Stack & Infrastructure
  - Development Rules & Best Practices
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

### V. Risk-Based Action Approval System (Human-in-the-Loop)
Every AI action must pass through the Risk Analyzer. Low-risk actions (tagging, notes, lead scoring) execute immediately. Medium-risk actions (CRM updates, supervisor transfers) log audits. High-risk or critical actions (marketing campaigns, discounts, price changes, or system data modifications) MUST require supervisor or administrator approval before execution.
*Rationale: Maintains human control over critical business decisions and protects database integrity from potential AI hallucinations.*

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

**Version**: 1.0.0 | **Ratified**: 2026-05-24 | **Last Amended**: 2026-05-24
