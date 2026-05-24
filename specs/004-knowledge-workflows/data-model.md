# Database Schema Design: Company Brain, Knowledge Base, Workflows & Approval System

## New Entities & Fields

### 1. Knowledge Base Module

#### `KnowledgeDocument`
Represents a document, FAQ, policy, or catalog item in the knowledge base.
- `Id`: UUID (Primary Key)
- `ProjectId`: UUID (Index, Foreign Key to Projects)
- `Title`: VARCHAR(255)
- `Content`: TEXT
- `SourceUrl`: VARCHAR(2048) (Nullable)
- `Version`: INT
- `Status`: VARCHAR(50) (Draft, Published, Archived)
- `CreatedAt`: TIMESTAMP WITH TIME ZONE
- `UpdatedAt`: TIMESTAMP WITH TIME ZONE

#### `KnowledgeChunk`
Represents a text chunk of a knowledge document with its corresponding semantic vector embedding.
- `Id`: UUID (Primary Key)
- `KnowledgeDocumentId`: UUID (Foreign Key to KnowledgeDocuments, ON DELETE CASCADE)
- `ChunkText`: TEXT
- `Embedding`: VECTOR(768) (PgVector database type, index created with HNSW index)
- `CreatedAt`: TIMESTAMP WITH TIME ZONE

---

### 2. Automation Workflows Module

#### `AutomationWorkflow`
Defines the automatic flow trigger, filters, and actions.
- `Id`: UUID (Primary Key)
- `ProjectId`: UUID (Index, Foreign Key to Projects)
- `Name`: VARCHAR(255)
- `TriggerType`: VARCHAR(100) (e.g., MessageReceived, TagAdded, ConversationCreated)
- `FiltersJson`: TEXT / JSONB (e.g., condition on customer tags, message intent, sentiment)
- `ActionsJson`: TEXT / JSONB (e.g., set tag, update lead stage, schedule follow-up, send message)
- `IsActive`: BOOLEAN
- `Version`: INT
- `CreatedAt`: TIMESTAMP WITH TIME ZONE

#### `WorkflowExecutionLog`
Audit trail of workflow executions.
- `Id`: UUID (Primary Key)
- `WorkflowId`: UUID (Foreign Key to AutomationWorkflows, ON DELETE CASCADE)
- `CustomerId`: UUID (Foreign Key to Customers, Nullable)
- `Status`: VARCHAR(50) (Running, Completed, Failed)
- `ExecutedActions`: TEXT / JSONB
- `StartedAt`: TIMESTAMP WITH TIME ZONE
- `FinishedAt`: TIMESTAMP WITH TIME ZONE
- `ErrorMessage`: TEXT (Nullable)

---

### 3. AI Risk & Action Approval Module

#### `ApprovalRequest`
Represents a pending action suggested by AI or configured workflows that requires manual authorization before executing.
- `Id`: UUID (Primary Key)
- `ProjectId`: UUID (Index, Foreign Key to Projects)
- `ActionType`: VARCHAR(100) (e.g., CRMUpdate, SendDiscount, OutboundMessage, ModifyRecord)
- `PayloadJson`: TEXT / JSONB (Contains the serialized data required to execute the action)
- `RiskLevel`: VARCHAR(50) (Low, Medium, High, Critical)
- `Status`: VARCHAR(50) (Pending, Approved, Rejected)
- `RequestedBy`: VARCHAR(100) (e.g., "AI_Worker" or specific User ID)
- `ReviewedBy`: UUID (Foreign Key to Users, Nullable)
- `ReviewNotes`: TEXT (Nullable)
- `CreatedAt`: TIMESTAMP WITH TIME ZONE
- `ReviewedAt`: TIMESTAMP WITH TIME ZONE

---

### 4. Customer Memory Module

#### `CustomerMemory`
Maintains long-term structured and unstructured contexts for a customer.
- `Id`: UUID (Primary Key)
- `ProjectId`: UUID (Index, Foreign Key to Projects)
- `CustomerId`: UUID (Foreign Key to Customers, Unique, ON DELETE CASCADE)
- `FactsJson`: TEXT / JSONB (Array of extracted facts: e.g., "Prefers morning calls", "Has 3 kids")
- `TriggersJson`: TEXT / JSONB (Array of buying triggers)
- `ObjectionsJson`: TEXT / JSONB (Array of customer concerns or objections)
- `LongTermSummary`: TEXT (High-level narrative of the relationship)
- `LastUpdatedAt`: TIMESTAMP WITH TIME ZONE

---

### 5. Integrations Module

#### `ProjectIntegration`
Tracks API connections config for external data sync.
- `Id`: UUID (Primary Key)
- `ProjectId`: UUID (Index, Foreign Key to Projects)
- `ProviderName`: VARCHAR(100) (e.g., Shopify, CustomAPI)
- `ConfigJson`: TEXT / JSONB (Encrypted API keys, URLs, authentication configurations)
- `IsActive`: BOOLEAN
- `SyncIntervalMinutes`: INT
- `LastSyncAt`: TIMESTAMP WITH TIME ZONE
