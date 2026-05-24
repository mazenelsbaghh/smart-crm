# Technical Research & Decisions: Company Brain, Knowledge Base, Workflows & Approval System

## 1. Vector Embeddings & Semantic Search (pgvector)
- **Decision**: Use PostgreSQL's `pgvector` extension to store and query knowledge base chunk embeddings.
- **Embedding Model**: Use Gemini's `text-embedding-004` model which generates 768-dimensional vectors.
- **EF Core Mapping**: Since `pgvector` is available on the containerized PostgreSQL database, we will map the vector as a `float[]` or a `Vector` type in EF Core. We will query using Npgsql cosine distance functions `db.KnowledgeChunks.OrderBy(c => c.Embedding.CosineDistance(queryEmbedding))` or raw SQL `SELECT * FROM chunks ORDER BY embedding <=> @query LIMIT @limit`.
- **Alternatives Considered**: 
  - *Elasticsearch Dense Float Vectors*: Rejected to keep storage unified in PostgreSQL and avoid indexing latency between systems for immediate search availability.
  - *In-memory vector search*: Rejected due to lack of durability and scalability for larger document sets.

## 2. Event-Driven Workflow Automation Engine
- **Decision**: Build a lightweight trigger-condition-action workflow engine inside the ASP.NET Core modular monolith framework.
- **Triggers**: Events published to RabbitMQ (e.g., `ConversationCreated`, `CustomerTagAdded`, `MessageReceived`).
- **Condition Evaluator**: Evaluate conditions (e.g., Tag == 'VIP', Sentiment == 'angry') using dynamic property checks.
- **Executor**: Executed asynchronously by a background `WorkflowWorker` running as a Hangfire job or a hosted background consumer.
- **Alternatives Considered**:
  - *Elsa Workflows (Third-party library)*: Rejected to avoid adding excessive dependencies and keeping database schema control simple and multi-tenant isolated.

## 3. Risk-Based Action Approval System (Human-in-the-Loop)
- **Decision**: Define a `RiskAnalyzer` service that inspects requested AI actions.
- **Risk Levels**:
  - **Low**: Executed immediately (e.g. tagging customer, writing notes).
  - **Medium**: Executed + audited (e.g. updating non-critical CRM fields).
  - **High**: Intercepted, created as a `Pending` `ApprovalRequest` record, and a SignalR notification pushed to supervisors. Requires a manual HTTP post to `/api/approvals/{id}/approve` to execute.
  - **Critical**: Blocked entirely (e.g. bulk deleting, changing global settings).
- **Execution Flow**: When an AI-suggested action is intercepted, its callback metadata (serialized action JSON and action type) is saved in the database. Upon approval, the corresponding module's handler executes the serialized action.

## 4. Periodic Integration Syncing
- **Decision**: Periodic pull syncs (customers, price lists, catalog) will be executed via scheduled Hangfire recurring jobs calling custom REST connectors. Webhook dispatches to external systems will be handled asynchronously using a RabbitMQ outbox queue to handle retries and rate limits.
