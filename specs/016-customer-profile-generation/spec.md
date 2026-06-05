# Feature Specification: On-Demand AI Customer Profile Generation

## Overview
This feature allows sales agents/owners to manually trigger a comprehensive AI-driven analysis of a customer's entire conversation history directly from the CRM Customer Drawer. When triggered, the AI analyzes all exchanged messages to construct a unified profile: long-term summary, facts, objections, and buying triggers, saving it to the customer memory and displaying it instantly on-screen.

## User Experience (UX) & Interface Requirements
1. **Trigger Button**:
   - Locate the button under the **AI Summary & Profile** section in the Customer Detail drawer.
   - Label: `"تحديث ملف تعريف العميل بالذكاء الاصطناعي"` (Update customer profile with AI).
   - Style: Sleek indigo background, micro-transition hover state, with a Sparkles/Brain icon.
   - Loading State: Disable the button and change text to `"جاري تحليل المحادثات واستخلاص البيانات..."` with a spinner.
   - Feedback: Show a localized Arabic success alert (`"تم توليد وتحديث ملف التعريف بنجاح!"`) or error alert if no message history exists.

2. **Immediate Form Update**:
   - On successful response from the backend, the React state fields for **AI Summary**, **Facts**, **Objections**, and **Triggers** must immediately populate with the new data without requiring a page refresh.

## Functional & Technical Requirements

### Backend API
1. **Endpoint**: `POST /api/projects/{projectId}/customers/{customerId}/memory/generate`
2. **Behavior**:
   - Query all `Conversations` for the given `customerId` and `projectId`.
   - Query all `Messages` belonging to these conversations, ordered chronologically.
   - If no messages are found, return `400 Bad Request` with an Arabic error message: `"لا توجد رسائل أو محادثات سابقة لهذا العميل لتوليد ملف التعريف."`
   - Build a unified transcript formatted as:
     `{Direction}: {Content}`
   - Invoke the `IGeminiClient` with a structured extraction prompt. The prompt must request the response in strict JSON format:
     ```json
     {
       "facts": ["الحقيقة 1", "الحقيقة 2"],
       "triggers": ["المحفز 1"],
       "objections": ["الاعتراض 1"],
       "summary": "ملخص شامل لشخصية العميل وطلباته..."
     }
     ```
   - Update or insert the `CustomerMemory` record in the database for the given customer.
   - Return the updated `CustomerMemory` entity (`200 OK`).

### Mock Mode / Testing Fallback
- In `GeminiClient.cs`, if the API key is not present or mock mode is active, detect the customer memory generation prompt and return a valid structured mock JSON payload containing sample facts, objections, triggers, and summary (e.g., Cairo city, interested in price, etc.).

## Open Questions & Answers
- **Q**: What if the customer has conversations across different projects?
- **A**: The query is strictly scoped to the active project's database records.
