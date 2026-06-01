# Implementation Plan: Customer Smart Label

**Branch**: `014-customer-smart-label` | **Date**: 2026-05-25 | **Spec**: [spec.md](spec.md)

## Summary

This plan details the technical steps to add an AI-predicted smart label to each customer. The label updates automatically on every incoming message, persists in the database, broadcasts via SignalR in real-time, and displays in the Inbox sidebar, chat header, CRM table, and Pipeline board deal cards.

## Technical Context

- **Backend**: C# (.NET 9.0) with Entity Framework Core, PostgreSQL, RabbitMQ, StackExchange.Redis
- **Real-time Gateway**: SignalR Hubs
- **Frontend**: Next.js (App Router), TypeScript, HSL CSS variables

## Constitution Check

- **Modular Monolith**: Changes respect module boundaries. Database changes are confined to the `Conversations` module's `Customer` entity, while AI analysis and CRM engine process and update this field.
- **Human-Like Messaging**: Real-time SignalR updates prevent any latency in label changes on the user interface.

## Proposed Changes

### Database & Domain Models

#### [MODIFY] [Customer.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Conversations/Domain/Customer.cs)
- Add a nullable string property `Label` representing the AI-classified tag:
  ```csharp
  public string? Label { get; set; }
  ```

#### [NEW] [Migration] Create EF Core Migration
- Add a migration `AddCustomerLabel` to add the `Label` column to the `Customers` table in PostgreSQL.

---

### AI Analysis & Prompting

#### [MODIFY] [AIMarketingBrain.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Services/AIMarketingBrain.cs)
- Add `Label` string property to `MarketingAnalysisResult`.
- Update the Gemini system prompt instructions to request the classification label:
  ```json
  {
    "intent": "...",
    "sentiment": "...",
    "replyStyle": "...",
    "label": "a short Arabic label (max 3 words) classifying the customer's current state/need based on the message, e.g., 'استفسار عن السعر', 'طلب شراء', 'شكوى', 'ترحيب'",
    "entities": { ... },
    "replyContent": "...",
    "confidence": 0.95
  }
  ```
- Parse the `"label"` property in `AnalyzeAndGenerateReplyAsync` and set it on the returning `MarketingAnalysisResult`.

---

### Worker & Event Orchestration

#### [MODIFY] [CRMUpdateSuggestedEvent.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Shared/Events/CRMUpdateSuggestedEvent.cs) (or inline definition)
- Add `Label` string property to the event.

#### [MODIFY] [AIReplyWorker.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Workers/AIReplyWorker.cs)
- Pass the parsed `analysisResult.Label` into `CRMUpdateSuggestedEvent.Label` when publishing.

#### [MODIFY] [CRMAutoUpdateEngine.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/Services/CRMAutoUpdateEngine.cs)
- Always update `customer.Label` to match the incoming `@event.Label` (or fallback to `"استفسار عام"` if empty/null) and save changes.
- Broadcast the updated customer profile to all agents via SignalR using `NotificationHub` so the UI receives it instantly.

---

### Frontend Services & Types

#### [MODIFY] [crm.ts](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/services/crm.ts)
- Add `label?: string;` to `Customer` interface.

#### [MODIFY] [chat.ts](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/types/chat.ts)
- Add `label?: string;` to the nested `Customer` interface inside `Conversation`.

---

### Frontend Components

#### [MODIFY] [Inbox.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/inbox/Inbox.tsx)
- Render a label badge next to the customer name in the conversation cards (using a clean styling class).
- Render a label badge in the chat header next to the active customer's name.
- Listen for real-time customer updates (via SignalR hub events or message logs) and update the local state of `conversations` and `activeConv` to reflect the new label instantly.

#### [MODIFY] [CustomerList.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/crm/CustomerList.tsx)
- Render the customer's smart label badge in the customer name cell below the name.

#### [MODIFY] [PipelineBoard.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/crm/PipelineBoard.tsx)
- Render the customer's smart label badge inside the deal card below the customer's name.

#### [MODIFY] [CustomerDetail.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/components/shared/CustomerDetail.tsx)
- Display the smart label inside the modal.

---

## Verification Plan

### Automated Tests
- Run `make test-phase-1` and `make test-phase-3` to ensure no regressions occur.
- Write a specific unit/integration test to check that sending a WhatsApp message updates the customer's label in PostgreSQL and propagates to CRM.

### Manual Verification
- Send a message like "بكم السعر؟" to the gateway.
- Verify that the label next to the customer in the inbox and CRM deal cards updates to "استفسار عن السعر" or similar in real-time.
