# Technical Implementation Plan: AI Customer Profile Generation

## Overview
This plan implements the manual trigger for AI customer profile generation. It leverages existing schemas and services to construct a unified conversation transcript, analyze it via Gemini AI, save the extracted profile, and update the CRM UI dynamically.

## Proposed Changes

### Backend

#### [MODIFY] [ICustomerMemoryService](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Customers/Services/CustomerMemoryService.cs)
- Add method signature:
  ```csharp
  Task<CustomerMemory> GenerateCompleteProfileAsync(Guid projectId, Guid customerId);
  ```
- Implement the method in `CustomerMemoryService`:
  - Query all conversation IDs for `customerId` and `projectId`.
  - Fetch all messages for these conversation IDs sorted by timestamp.
  - If no messages exist, throw an `ArgumentException` with the message `"لا توجد رسائل سابقة لهذا العميل لتوليد ملف التعريف."`.
  - Construct the transcript: `"{Direction}: {Content}"`.
  - Prompt Gemini to extract summary, facts, triggers, and objections in JSON format.
  - Parse the JSON response. If parsing fails, fall back to keyword parsing (similar to existing implementation).
  - Find or create the `CustomerMemory` record. Overwrite its contents with the newly generated profile.
  - Save changes and return the updated `CustomerMemory` record.

#### [MODIFY] [CRMController](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/API/CRMController.cs)
- Inject `ICustomerMemoryService` in the constructor.
- Add HTTP POST endpoint:
  ```csharp
  [HttpPost("projects/{projectId}/customers/{customerId}/memory/generate")]
  public async Task<IActionResult> GenerateCustomerProfile(Guid projectId, Guid customerId)
  {
      try
      {
          var memory = await _customerMemoryService.GenerateCompleteProfileAsync(projectId, customerId);
          return Ok(memory);
      }
      catch (ArgumentException ex)
      {
          return BadRequest(ex.Message);
      }
  }
  ```

#### [MODIFY] [GeminiClient](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Services/GeminiClient.cs)
- Update mock logic to recognize the new customer profile generation prompt (when it contains `"Analyze the following WhatsApp conversation"` and requests facts/triggers/objections/summary) and return a valid structured mock JSON response.

---

### Frontend

#### [MODIFY] [CustomerDetail](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/components/shared/CustomerDetail.tsx)
- Add state: `generatingMemory` (boolean).
- Render a button in the AI Summary section:
  - Text: `"تحديث تلقائي بالذكاء الاصطناعي"` (Auto-update with AI) with a Lucide `Sparkles` icon.
  - Style: Styled to match the dark theme and glassmorphism.
  - Disabled during execution. Show loading text `"جاري استخلاص البيانات..."`.
- Implementation of `handleGenerateMemory`:
  - Call `POST /api/projects/${projectId}/customers/${customerId}/memory/generate`.
  - On success:
    - Update states: `editableSummary`, `editableFacts`, `editableObjections`, `editableTriggers`.
    - Alert success using `"تم توليد وتحديث ملف التعريف بنجاح!"`.
  - On failure: show the backend error message or default error.

---

## Verification Plan

### Automated Tests
- Create a new integration test file: `tests/phase_3/test_customer_memory_generation.py`.
- Test cases:
  1. Triggering generate profile for customer with no messages returns `400 Bad Request` with correct error message.
  2. Triggering generate profile for customer with conversations successfully returns the extracted JSON memory structure and saves it to the database.

### Manual Verification
- Open the CRM drawer.
- Select a customer with conversations.
- Click the "تحديث تلقائي بالذكاء الاصطناعي" button.
- Verify that loading text appears, memory fields are populated immediately, and the success message is shown.
