# Implementation Plan: Real-time Chat Sync, AI Labeling Flexibility, and Follow-up Automation Rules

**Branch**: `018-chat-updates-labels-followups` | **Date**: 2026-06-02 | **Spec**: [spec.md](file:///Users/mazenelsbagh/mazen%20mac/apps/smart whatsapp/specs/018-chat-updates-labels-followups/spec.md)

**Input**: Feature specification from `/specs/018-chat-updates-labels-followups/spec.md`

## Summary

Implement real-time sync enhancements in the React frontend inbox, refine AI system prompt instructions for flexible customer labeling, and automate the follow-up lifecycle in the C# backend.

## Technical Context

**Language/Version**: C# (.NET 8), TypeScript (React 18)

**Primary Dependencies**: `@microsoft/signalr`, `Microsoft.AspNetCore.SignalR`

**Storage**: PostgreSQL

**Testing**: Pytest integration tests & C# Unit Tests

**Target Platform**: Docker Containerized Environment

**Project Type**: Web Application

**Performance Goals**: Real-time SignalR message dispatch under 500ms. Follow-up lifecycle state updates under 100ms.

**Constraints**: Multi-tenant isolation by `ProjectId` in database queries.

## Constitution Check

| Principle | Check | Status / Justification |
| :--- | :--- | :--- |
| **I. Modular Monolith** | Code changes inside respective modules. | **PASSED**. Changes are cleanly kept in the `Conversations`, `CRM`, and `AI` modules. |
| **II. Project Isolation** | Multi-tenant query isolation. | **PASSED**. All DB queries partition by `ProjectId`. |
| **III. Gemini 3.5 Unified AI** | Flexible classification label. | **PASSED**. Prompt updated to let Gemini generate new specific labels when needed. |

## Project Structure

### Documentation (this feature)

```text
specs/018-chat-updates-labels-followups/
├── spec.md              # Feature specification
├── plan.md              # This file
└── checklists/
    └── requirements.md  # Spec quality checklist
```

### Source Code

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── AI/
│   │   │   └── Services/
│   │   │       ├── AIMarketingBrain.cs         # AI labeling system prompt relaxation
│   │   │       └── GeminiClient.cs             # Mock labeling update for "تفاصيل"
│   │   ├── Conversations/
│   │   │   └── API/
│   │   │       ├── WebhookController.cs        # Cancel old follow-ups, insert default new follow-up
│   │   │       └── ConversationController.cs   # Cancel old follow-ups, insert default new follow-up
│   │   └── CRM/
│   │       └── Services/
│   │           └── CRMAutoUpdateEngine.cs      # Apply/override follow-up or mark as Completed if not needed
frontend/
└── src/
    └── packages/
        └── inbox/
            └── Inbox.tsx                       # Live fetch for new chats, dynamic sorting
```

**Structure Decision**: Web application (encompassing Backend modular monolith and Frontend packages).

## Proposed Changes

### Frontend - React Inbox Component

#### [MODIFY] [Inbox.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/inbox/Inbox.tsx)
- In the SignalR `ReceiveMessage` event listener callback:
  - Check if the incoming conversation exists in the local state list. If not, trigger `fetchConversations()` to pull the new conversation card.
  - Dynamically update the `lastMessageAt` timestamp of the conversation when a message arrives.
- Update `filteredConversations` sorting logic to order conversations descending by `lastMessageAt` timestamp so new/active chats instantly float to the top of the sidebar.

### Backend - AI & CRM Modules

#### [MODIFY] [AIMarketingBrain.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Services/AIMarketingBrain.cs)
- Update system prompt instructions to allow the AI to invent new descriptive Arabic labels (max 3 words) if none of the existing labels fit the query, preventing biased labeling.

#### [MODIFY] [GeminiClient.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Services/GeminiClient.cs)
- Update fallback mock responses for queries containing "تفاصيل" to use "استفسار عن التفاصيل" as the label instead of forcing "استفسار عن السعر".

#### [MODIFY] [WebhookController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Conversations/API/WebhookController.cs)
- In `ReceiveMessage`, when a customer's incoming message is received:
  - Query and mark all "Pending" follow-ups for that customer as "Completed".
  - Insert a new default pending follow-up (24 hours in the future) with friendly follow-up text.

#### [MODIFY] [ConversationController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Conversations/API/ConversationController.cs)
- In `SendMessage`, when an agent sends an outgoing message:
  - Query and mark all "Pending" follow-ups for that customer as "Completed".
  - Insert a new default pending follow-up (24 hours in the future) with friendly follow-up text.

#### [MODIFY] [CRMAutoUpdateEngine.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/Services/CRMAutoUpdateEngine.cs)
- Update `ProcessSuggestionAsync` follow-up block:
  - If AI says `FollowUpNeeded` is `true`, find the newly created `Pending` follow-up and update it with the AI's suggested timing and notes.
  - If AI says `FollowUpNeeded` is `false`, find the newly created `Pending` follow-up and change its status to `Completed` so it is not sent.

## Verification Plan

### Automated Tests
- Run existing integration tests or write targeted unit tests for `CRMAutoUpdateEngine` and controllers.

### Manual Verification
- Test real-time message sending/receiving and observe the UI updating instantly without refresh.
- Ask questions like "عايز تفاصيل الكورس" to verify the generated label.
- Verify in the database that pending follow-ups transition to "Completed" when a message is received or sent.
