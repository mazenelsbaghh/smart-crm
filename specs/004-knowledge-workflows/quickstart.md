# Quickstart Guide: Testing & Running Phase 3

This document guides you through running and verifying the Company Brain, Knowledge Base, Workflows, Risk Approvals, Customer Memory, and Sync Integration features.

## 1. Running the Services

Start all Docker containers (which includes PostgreSQL with `pgvector`, Redis, RabbitMQ, and Elasticsearch):
```bash
make up
```

Verify service health:
```bash
make health
```

Ensure EF Core migrations are applied to create the new tables (KnowledgeDocument, KnowledgeChunk, AutomationWorkflow, WorkflowExecutionLog, ApprovalRequest, CustomerMemory, and ProjectIntegration):
```bash
make db-migrate
```

---

## 2. Running Phase 3 Tests

Ensure your test environment is set up:
```bash
make test-setup
```

Run all Phase 3 integration and unit tests:
```bash
.venv/bin/pytest tests/phase_3/ -v --tb=short
```

---

## 3. Manual Verification & Key Endpoints

- **Semantic Memory Query**:
  Call the search endpoint to run a cosine similarity query:
  `GET /api/projects/{projectId}/brain/search?q=YOUR_QUERY`

- **Knowledge Sync**:
  Trigger a manual sync of the knowledge base from external endpoints:
  `POST /api/projects/{projectId}/integrations/{integrationId}/sync`

- **Approval Queue**:
  Inspect high-risk actions awaiting supervisor clearance:
  `GET /api/projects/{projectId}/approvals?status=Pending`
  
  Approve a request:
  `POST /api/approvals/{id}/approve`
