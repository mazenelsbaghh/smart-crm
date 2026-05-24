# Quickstart: Campaigns, Advanced Analytics & Reporting

This document outlines the steps required to verify the Campaign Engine, Advanced Analytics, CRM Pipelines, and Elasticsearch indexing in development.

## Setup Requirements

Before testing Phase 4, verify that all external infrastructure services are healthy:
- **Elasticsearch** is running on port `9200`.
- **RabbitMQ** is running on port `5672` (management UI on `15672`).
- **PostgreSQL** database is migrated and seeded.

```bash
make up
make db-migrate
make db-seed
```

## Running Verification Tests

Run the complete suite of Phase 4 tests:

```bash
make test-phase-4
```

This command executes:
- Campaign generation, scheduling, and A/B test routing.
- Analytics calculations and pre-aggregated snapshot jobs.
- CRM pipeline stage transitions and deal status updates.
- Elasticsearch document indexing and full-text multi-tenant searches.

## API Execution Flow (Manual Testing)

### 1. Create a Segment
```bash
curl -X POST http://localhost:5000/api/projects/{projectId}/segments \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Cairo Hot Leads",
    "filterCriteriaJson": "{\"city\": \"Cairo\", \"leadScoreMin\": 70}"
  }'
```

### 2. Launch a Campaign (with A/B variants)
```bash
curl -X POST http://localhost:5000/api/projects/{projectId}/campaigns \
  -H "Content-Type: application/json" \
  -d '{
    "name": "Summer Sales Campaign",
    "segmentId": "SEGMENT_ID_HERE",
    "messageTemplateA": "Hi {{CustomerName}}, we have a special discount for you in Cairo! Use code SUMMER50.",
    "messageTemplateB": "Hello {{CustomerName}}! Ready for the summer? Get 50% off with code SUNNY50."
  }'
```

### 3. Schedule Campaign Broadcast
```bash
curl -X POST http://localhost:5000/api/campaigns/{campaignId}/schedule \
  -H "Content-Type: application/json" \
  -d '"2026-05-25T12:00:00Z"'
```

### 4. Perform Search (Elasticsearch)
```bash
curl "http://localhost:5000/api/projects/{projectId}/search?q=summer&type=messages"
```

### 5. Fetch Daily Analytics
```bash
curl http://localhost:5000/api/projects/{projectId}/analytics/sales
```
