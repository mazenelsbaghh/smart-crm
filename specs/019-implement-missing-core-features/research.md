# Research and Design Decisions: Implement Missing Core Features

This document outlines the core technical research, architectural patterns, and design decisions for adding Workflow Actions, Advanced Routing, and the Knowledge Suggestion/Approval workflow.

## 1. Workflow Actions (SendMessage & SendAlert)

### Technical Approach
- **SendMessage**: When `WorkflowEngine` executes a `SendMessage` action:
  - It will resolve the `IEventBus` and publish an `AIReplyGeneratedEvent` (which triggers `ReplySender` queue/worker asynchronously), or invoke the WhatsApp gateway controller directly. Publishing an event is cleaner and matches the asynchronous Monolith principle.
  - The parameter will contain a `TemplateText` string (e.g., "Hello {{CustomerName}}"). We will parse `{{CustomerName}}` or other custom fields from the customer entity before dispatching.
- **SendAlert**: When `WorkflowEngine` executes `SendAlert` action:
  - It will create a `NotificationAlert` entity in the DB.
  - It will invoke `IHubContext<NotificationHub>` to push the alert to all connected dashboard users in the tenant group (`project_{projectId}`).

### Rationale
Using the `IEventBus` for messages maintains strict modular decoupling, preventing the `Workflows` module from depending directly on the `WhatsApp` or `Conversations` modules' databases or concrete handlers. SignalR push via `IHubContext` ensures immediate front-end UI alerts without polling.

---

## 2. Advanced Routing (VIP, Complaint, Offline/Idle Reassignment)

### Technical Approach
- **VIP Routing**:
  - We look at the customer's `LeadScore` in `AssignConversationAsync`. If it is >= 80, we query the project users database for a user with the role of `Admin` or `Owner`. If found, we route the conversation directly to them.
- **Complaint Routing**:
  - If the conversation status is set to "Pending" because of negative sentiment, or if there is a complaint, we fetch the supervisor users (`Supervisor` role) and assign it to them.
- **Offline/Idle Reassignment**:
  - When a customer message is received in `WebhookController.cs` or `ReceiveMessage`:
    - We check if the assigned agent is offline in Redis (using `presence` key).
    - Or if the agent has been idle (no messages sent by the agent in the last 10 minutes, but customer has sent messages).
    - If either condition is true, we call `IAssignmentEngine.AssignConversationAsync` with `agentId = null` to trigger auto-reassignment to the next online agent.

### Rationale
Using Redis presence keys (`project:{projectId}:agent:{agentId}:presence`) is extremely fast and prevents heavy DB reads for active agent status.

---

## 3. Knowledge Base Status Lifecycle

### Technical Approach
- **ApprovalStatus**:
  - We will introduce an enum `KnowledgeApprovalStatus` (`Draft`, `PendingApproval`, `Approved`, `Rejected`).
  - We will add an `ApprovalStatus` property to `KnowledgeDocument` (and filter its chunks in `AICompanyBrain` to only include documents where `ApprovalStatus == Approved`).
- **REST Endpoints**:
  - `POST /api/projects/{projectId}/brain/suggest`: Create a pending suggestion.
  - `POST /api/projects/{projectId}/brain/{documentId}/approve`: Update status to `Approved` and generate embeddings in the vector store.
  - `POST /api/projects/{projectId}/brain/{documentId}/reject`: Update status to `Rejected`.

### Rationale
Ensuring pgvector embeddings queries are filtered by approval status prevents unverified company documents from polluting AI prompts.
