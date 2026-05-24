# Research: Campaigns, Advanced Analytics & Reporting

## Unknowns & Key Design Decisions

### Decision 1: Campaign Execution and Anti-Ban Delay Mechanics
- **Chosen Approach**: Execute campaign sending using a dedicated Hangfire background job (`CampaignSenderJob`) rather than an in-memory worker. The job processes contacts in batches and schedules individual message deliveries via RabbitMQ/Hangfire with a randomized delay (e.g. 5 to 15 seconds) to avoid blocking the main execution thread and prevent WhatsApp rate-limit bans.
- **Rationale**: Hangfire provides built-in persistence, retries, and rate limiting. Placing a random delay between messages directly mimics human typing speed. By scheduling individual message delivery tasks in a distributed queue rather than sleeping a single thread, we ensure the system is resilient to server restarts.
- **Alternatives Considered**: In-memory task processor using `Channels` or `BackgroundService` with `Task.Delay`. Rejected because it lacks persistence; if the server restarts, the campaign progress is lost, leading to duplicated messages or half-sent campaigns.

### Decision 2: A/B Campaign Testing Split Method
- **Chosen Approach**: Simple deterministic hash-based partitioning of the audience. The system hashes the Customer ID + Campaign ID and uses modulo-2 to split the segment 50/50 into Variant A and Variant B.
- **Rationale**: Highly efficient, stateless, repeatable, and requires no extra tables to store which customer got which variant. It guarantees even distribution without random seed variance.
- **Alternatives Considered**: Dynamic database flagging of assigned variant on a join table. Rejected because it increases database write overhead unnecessarily.

### Decision 3: Pre-aggregated Analytics Snapshots
- **Chosen Approach**: Use a PostgreSQL `AnalyticsSnapshot` table to store daily aggregates. A Hangfire CRON job runs nightly to calculate metrics (conversion rates, response times, AI handoffs) and writes them to this table.
- **Rationale**: Querying raw conversation logs and message tables dynamically to compute analytics on the fly will lead to poor database performance as data scales. Pre-aggregating data ensures the analytics dashboard loads instantly (< 1s).
- **Alternatives Considered**: Direct PostgreSQL live queries. Rejected due to scale constraints.

### Decision 4: Elasticsearch Indexing Strategy
- **Chosen Approach**: Event-driven near realtime (NRT) indexing. When a Conversation, Message, or Customer is created or modified, the backend publishes a RabbitMQ event (e.g., `ConversationIndexed`, `MessageIndexed`). An `ElasticsearchIndexerWorker` consumes these events and writes/updates indices in Elasticsearch.
- **Rationale**: Keeps the write path of the core API fast and non-blocking. If Elasticsearch is temporarily unavailable, RabbitMQ queues the events, preventing data loss.
- **Alternatives Considered**: Synchronous indexing in the controller/handler. Rejected because it violates the Modular Monolith principles and makes the API slow if Elasticsearch has latency spikes.

## Best Practices & Patterns

### 1. Elasticsearch Search API
- Use the official Elastic.Clients.Elasticsearch SDK (v8) for C#.
- Implement index aliases (`smart_whatsapp_conversations`, `smart_whatsapp_customers`) to allow zero-downtime reindexing.

### 2. Multi-Tenant Isolation in Elasticsearch
- Each document in Elasticsearch MUST contain a `ProjectId` field.
- Every search query MUST include a strict filter clause: `term: { projectId: currentProjectId }`. This prevents tenant cross-leaks.

### 3. Anti-Ban Throttling Rules
- Random delay: `new Random().Next(5000, 15000)` milliseconds.
- Queue-level throttle: Max 250 messages per hour per connected WhatsApp line to keep inside WhatsApp gateway's safe bounds.
