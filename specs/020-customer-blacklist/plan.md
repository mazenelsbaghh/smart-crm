# Implementation Plan: Customer Blacklist for AI Exclusion

**Branch**: `020-customer-blacklist` | **Date**: 2026-06-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/020-customer-blacklist/spec.md`

## Summary

Implement a customer blacklist feature so that the AI auto-reply is bypassed for blacklisted customers. This involves adding an `IsBlacklisted` boolean field to the `Customer` entity, generating database migrations, updating CRM API endpoints to read/write the blacklist status, suppressing the typing indicator in `WebhookController`, skipping reply generation in `AIReplyWorker`, and adding toggling/display elements in the CRM frontend React application.

## Technical Context

**Language/Version**: C# (.NET 9), TS/React 18

**Primary Dependencies**: Microsoft.EntityFrameworkCore, Microsoft.AspNetCore.SignalR

**Storage**: PostgreSQL

**Testing**: Pytest integration tests & C# Integration/Unit Tests

**Target Platform**: Docker Containerized Environment

**Project Type**: Web Application

**Performance Goals**: Instant toggles and visual list badge updates under 100ms. Bypassing LLM generation saves up to 4 seconds of processing time per incoming message.

**Constraints**: Tenant isolation by `ProjectId` when querying/updating.

## Constitution Check

| Principle | Check | Status / Justification |
| :--- | :--- | :--- |
| **I. Modular Monolith** | Separate CRM updates from WhatsApp webhook logic. | **PASSED**. AI Auto-Reply worker queries customer from DB and skips cleanly without crossing boundaries. |
| **II. Project Isolation** | Multi-tenant isolation by `ProjectId`. | **PASSED**. CRM update endpoints enforce project isolation; webhook normalization queries customer within project boundaries. |
| **III. Gemini Unified AI** | Bypassing LLM calls for blacklisted users. | **PASSED**. Skipping reply generation early saves resource constraints. |
| **IV. Human-Like Messaging** | Prevent showing AI typing indicator for blacklisted users. | **PASSED**. Suppresses `AITyping` signalr event broadcast. |
| **V. Risk-Based Action Approval** | Simple administrative flag. | **PASSED**. Easy blacklisting/whitelisting dashboard control. |

## Project Structure

### Documentation (this feature)

```text
specs/020-customer-blacklist/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Technical design analysis
├── data-model.md        # Database schema updates
└── quickstart.md        # Feature setup guide
```

### Source Code

```text
backend/
├── src/
│   ├── Modules/
│   │   ├── Conversations/
│   │   │   ├── Domain/
│   │   │   │   └── Customer.cs               # Add IsBlacklisted boolean property
│   │   │   ├── API/
│   │   │   │   └── WebhookController.cs      # Suppress typing indicator if IsBlacklisted is true
│   │   ├── CRM/
│   │   │   ├── API/
│   │   │   │   └── CRMController.cs          # Update UpdateCustomer endpoint and projections
│   │   └── AI/
│   │       └── Workers/
│   │           └── AIReplyWorker.cs          # Bypass reply generation if IsBlacklisted is true
frontend/
└── src/
    ├── services/
    │   └── crm.ts                            # Update Customer interface
    ├── packages/
    │   └── crm/
    │       └── CustomerList.tsx              # Display blacklist badge
    └── components/
        └── shared/
            └── CustomerDetail.tsx            # Add "Is Blacklisted" toggle switch in form
```

**Structure Decision**: Web application (Backend modular monolith and Frontend package updates).
