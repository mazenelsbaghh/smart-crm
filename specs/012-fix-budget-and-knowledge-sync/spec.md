# Feature Specification: CRM Customer Budget & Knowledge Sync Seeding Fixes

**Feature Branch**: `012-fix-budget-and-knowledge-sync`

**Created**: 2026-05-25

**Status**: Draft

**Input**: User description: "Fix CRM customer budget updating and tabbing. Ensure budget fields support setting, resetting, and clearing. Ensure the AI Sync Brain button does not delete user-defined manual files and replace them with mock data."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Persist and Clear Customer Budget (Priority: P1)

Users should be able to update a customer's budget field inside the Inbox details panel, and clear it when needed.

**Why this priority**: Correct financial metrics tracking is essential for sales pipeline routing and deals valuation.

**Independent Test**: Can be tested by updating a customer's budget in the inbox sidebar, saving, and checking if the value persists on page refresh and syncs to their active deal amount.

**Acceptance Scenarios**:

1. **Given** a customer has a budget of `$5000` and an active open deal in the pipeline, **When** the agent clears the budget field and clicks save, **Then** the customer's budget is saved as `null` in the database, and the active deal amount is updated to `0`.
2. **Given** a customer has no budget (`null`), **When** the agent sets the budget to `$7500` and clicks save, **Then** the budget is saved as `7500.00` in the database, and the active deal amount is updated to `7500.00`.

---

### User Story 2 - Sync AI Brain without Erasing User Documents (Priority: P1)

Users should be able to trigger the AI Sync button to ensure their uploaded/created documents are properly indexed by the semantic search engine, without losing their manually uploaded files.

**Why this priority**: Data loss during synchronization operations is unacceptable and blocks the user from customizing the AI's reference context.

**Independent Test**: Can be tested by uploading a manual document, clicking "Sync AI Brain", and verifying that the manual document remains in the list and its embeddings are correctly generated.

**Acceptance Scenarios**:

1. **Given** the database is completely empty of knowledge documents, **When** the user clicks "Sync AI Brain", **Then** the system seeds the 3 default policy templates (Pricing, Shipping, Refund) and indexing is completed.
2. **Given** the database contains manual documents uploaded by the user, **When** the user clicks "Sync AI Brain", **Then** the manual documents are NOT deleted, and the default templates are NOT added. The system indexes any unindexed documents and keeps the existing ones safe.

---

### Edge Cases

- What happens if the customer has no active open deal in the pipeline when their budget is updated?
  - A new deal is created in the default "New" stage with the specified budget (or 0 if budget is cleared).
- What happens if the Gemini embedding generator fails during document synchronization?
  - The document stays in "Published" or "Draft" state, but the sync logs show a warning or error, and other documents' indexing continues.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST support setting the customer's budget to `null` (cleared) when updating their profile.
- **FR-002**: The system MUST synchronize the updated customer budget with their active open deal amount.
- **FR-003**: The AI Sync function MUST NOT delete existing user-defined knowledge base documents.
- **FR-004**: The AI Sync function MUST only seed the 3 default policy templates if there are zero knowledge documents in the project's knowledge base.
- **FR-005**: The AI Sync function MUST re-index existing documents for the project if they lack chunks or embeddings.

### Key Entities

- **Customer**: Represents the client, containing fields like Name, PhoneNumber, Budget (nullable decimal), and LeadScore.
- **Deal**: Represents the financial deal associated with the customer, containing the Amount (decimal) and PipelineStageId.
- **KnowledgeDocument**: Represents a document uploaded or created in the knowledge base.
- **KnowledgeChunk**: Represents a text chunk of the KnowledgeDocument with its associated pgvector Embedding.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Clearing the budget input on the frontend resets the database field and persists across page refreshes.
- **SC-002**: Active deal amount updates immediately to match the newly saved customer budget.
- **SC-003**: Clicking "Sync AI Brain" preserves 100% of user-uploaded files, with zero documents lost.

## Assumptions

- We assume the existing UI form inputs for budget and document deletion are correct and only backend controller/service logic requires modification.
- We assume that the PostgreSQL database with the pgvector extension is functioning.
