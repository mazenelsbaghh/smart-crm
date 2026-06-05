# Research and Design Decisions: Customer Blacklist for AI Exclusion

This document details the analysis, architectural changes, and design decisions for adding a customer blacklist to bypass AI auto-replies.

## 1. Bypassing AI Auto-Reply

### Technical Approach
- When an incoming message is received, it goes through `WebhookController. ReceiveMessage` which updates or creates the `Customer` and `Conversation` records, saves the message, broadcasts it via SignalR, and finally passes it to the `MessageAggregator`.
- We want the incoming message to show up in the agent's CRM chat interface so they can chat manually. However:
  1. We must suppress the "AI is typing..." indicator. In `WebhookController.cs`, we will check if the customer is blacklisted before broadcasting `AITyping`.
  2. We must prevent `AIReplyWorker` from generating and sending an auto-reply. In `AIReplyWorker.cs`, we will fetch the customer's status early in the message processing pipeline. If `IsBlacklisted` is true, we immediately log it and exit `HandleAsync` without calling `IAIMarketingBrain` or publishing an `AIReplyGeneratedEvent`.

### Rationale
Early exit in `AIReplyWorker.cs` prevents calling the Gemini LLM API (saving cost and rate limits) and prevents triggering any auto-reactions or publishing events. Suppressing `AITyping` in `WebhookController.cs` ensures the frontend UI doesn't show the user that the chatbot is typing, which would be confusing for a blacklisted user.

---

## 2. Database Schema Change

### Technical Approach
- We will add `public bool IsBlacklisted { get; set; } = false;` to the `Customer` class in `Modules.Conversations.Domain`.
- We will generate a new EF Core migration: `dotnet ef migrations add AddIsBlacklistedToCustomer` inside the `backend` project.
- The migration will add the `IsBlacklisted` column to the `Customers` database table with a default value of `false` (or `0` in PostgreSQL context, boolean `false`).

---

## 3. CRM API Changes

### Technical Approach
- **Customer List Endpoint (`GET /api/projects/{projectId}/customers`)**: Return the `IsBlacklisted` boolean field in the customer projection.
- **Customer Details Endpoint (`GET /api/customers/{id}`)**: Return the `IsBlacklisted` boolean field in the response.
- **Update Customer Endpoint (`PUT /api/customers/{id}`)**: Include `IsBlacklisted` in the request body (`UpdateCustomerRequest`) and apply it to the `Customer` entity in `CRMController.UpdateCustomer`.

---

## 4. Frontend UI Integration

### Technical Approach
- **CustomerDetail.tsx (Profile View)**:
  - Add a state variable `isBlacklisted` and load it from customer details.
  - Render a togglable checkbox/switch: "حظر الرد الآلي بالذكاء الاصطناعي (Blacklist)" in the profile edit form.
  - Send the updated `isBlacklisted` value in the payload to `crmService.updateCustomer`.
- **CustomerList.tsx (CRM Table)**:
  - Inside the customer cell or name box, check if `isBlacklisted` is true.
  - If true, display a clean, harmonized badge in Arabic: "مستبعد من الرد الآلي" (Excluded from Auto-Reply) or similar.
