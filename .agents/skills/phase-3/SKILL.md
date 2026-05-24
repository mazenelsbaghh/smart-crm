---
name: "phase-3"
description: "Company Brain, Knowledge Base, Workflows & Approval System"
compatibility: "Smart Customer Core Phase 3"
metadata:
  author: "community"
  source: "phase-3/SKILL.md"
---

# Phase 3: Company Brain, Knowledge Base, Workflows & Approval System

This module provides semantic search, workflow triggers, and manual approvals for high-risk actions.

## 1. Company Brain & Knowledge Base (US1 & US2)

Synchronize and query documents:
- **Sync Catalog**: `POST /api/projects/{projectId}/brain/sync`
- **Semantic Query**: `GET /api/projects/{projectId}/brain/search?q=search_term`
- **Document Approvals**:
  - Approve draft: `PUT /api/knowledge/{id}/approve`
  - Reject draft: `PUT /api/knowledge/{id}/reject`

Documents must be in status `"Published"` to be retrieved by the company brain.

## 2. Workflows & Automations (US3)

Workflows are triggered by events (e.g. `CustomerTagAdded`).
- **Create Workflow**: `POST /api/projects/{projectId}/workflows`
- **Trigger**: Published automatically via CRM updates.
- **Actions**: e.g., `"UpdateCRM"` parameter modifications.

## 3. Risk-Based Action Approval System (US4)

Any action execution evaluates risk level using `RiskAnalyzer`:
- **Execute Action**: `POST /api/projects/{projectId}/actions/execute`
- **Evaluate & Approve**:
  - High/Critical risk actions are paused and placed in `"Pending"` status.
  - Approve: `POST /api/approvals/{id}/approve` (executes the action and saves changes)
  - Reject: `POST /api/approvals/{id}/reject`

## 4. Customer Memory & Integrations (US5)

- **External Integrations**:
  - Configure: `POST /api/projects/{projectId}/integrations`
  - Sync Job: `POST /api/projects/{projectId}/integrations/{id}/sync` (runs asynchronously using Hangfire)
- **Customer Memory**:
  - Extracted automatically upon conversation status change to `"Closed"`.
  - Retrieve: `GET /api/customers/{customerId}/memory`
