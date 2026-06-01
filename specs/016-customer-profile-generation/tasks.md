# Tasks: AI Customer Profile Generation

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`) completed in `spec.md`
- [x] Phase 2: Technical Planning (`speckit-plan`) completed in `plan.md`
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed in this file

## Implementation Tasks

### Backend Implementation

- [x] T001 In C# interface file [ICustomerMemoryService](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Customers/Services/CustomerMemoryService.cs) and its implementation class `CustomerMemoryService`:
  - Add method `Task<CustomerMemory> GenerateCompleteProfileAsync(Guid projectId, Guid customerId)`.
  - Implement this method to:
    1. Query all `Conversation` IDs for the given `customerId` and `projectId`.
    2. Query all `Message` entities belonging to these conversations, sorted by `Timestamp` ascending.
    3. If there are no messages, throw an `ArgumentException` with the text `"لا توجد رسائل سابقة لهذا العميل لتوليد ملف التعريف."`.
    4. Construct a transcript string: joining message directions and contents with newlines: `"{m.Direction}: {m.Content}"`.
    5. Invoke `_geminiClient.GenerateReplyAsync(prompt)` using the unified extraction prompt.
    6. Parse the response into `MemoryExtractionResult`. If parsing fails, fall back to keyword extraction (similar to `UpdateMemoryFromConversationAsync`).
    7. Find or create the `CustomerMemory` record in the database for the given customer.
    8. Populate the memory with the extracted values: `FactsJson` (serialized list), `TriggersJson` (serialized list), `ObjectionsJson` (serialized list), and `LongTermSummary` (overwriting with the new summary). Set `LastUpdatedAt = DateTime.UtcNow`.
    9. Save changes to DB and return the updated `CustomerMemory`.

- [x] T002 In C# controller [CRMController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/CRM/API/CRMController.cs):
  - Inject `ICustomerMemoryService` into the constructor (ensure the class uses a private field `_customerMemoryService`).
  - Add HTTP POST endpoint `projects/{projectId}/customers/{customerId}/memory/generate`:
    - Call `_customerMemoryService.GenerateCompleteProfileAsync(projectId, customerId)`.
    - Return `Ok(memory)`.
    - Catch `ArgumentException` and return `BadRequest(ex.Message)`.

- [x] T003 In C# client file [GeminiClient.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/AI/Services/GeminiClient.cs):
  - In `GenerateReplyAsync`, update the prompt detection block:
    - Check if `messageContent.Contains("Analyze the following WhatsApp conversation")` or similar.
    - If so, return a mock extraction JSON response:
      ```json
      {
        "facts": ["مهتم بالدورة المكثفة", "يفضل التواصل واتساب", "يعيش في القاهرة"],
        "triggers": ["خصم لفترة محدودة", "البدء الفوري"],
        "objections": ["السعر مرتفع قليلاً"],
        "summary": "عميل مهتم بالتسجيل في الدورة ويبحث عن تفاصيل الأسعار وتسهيلات الدفع."
      }
      ```

---

### Frontend UI Updates

- [x] T004 In React component [CustomerDetail.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/components/shared/CustomerDetail.tsx):
  - Import `Sparkles` from `lucide-react`.
  - Add a React state `generatingMemory` (boolean, default `false`).
  - In the "AI Summary & Profile" section, under the form header or inside the edit memory form, render a button:
    - Text: `generatingMemory ? "جاري استخلاص البيانات..." : "تحديث تلقائي بالذكاء الاصطناعي"`
    - Icon: `Sparkles` (size 14).
    - Style: An elegant button (e.g. glassmorphism background, semi-transparent indigo border, hover transition).
    - Attribute: `type="button"`, `disabled={generatingMemory}`.
  - Implement `handleGenerateMemory`:
    - Set `generatingMemory(true)`.
    - Call `POST /api/projects/${projectId}/customers/${customerId}/memory/generate`.
    - On success:
      - Parse/update local states:
        - `setEditableSummary(data.longTermSummary || '')`
        - Parse facts from `data.factsJson` (array) and `setEditableFacts(facts.join(', '))`
        - Parse triggers from `data.triggersJson` (array) and `setEditableTriggers(triggers.join(', '))`
        - Parse objections from `data.objectionsJson` (array) and `setEditableObjections(objections.join(', '))`
      - Display an alert: `"تم توليد وتحديث ملف التعريف بنجاح!"`.
    - On error:
      - Display the error response or default message: `"فشل توليد ملف التعريف. تأكد من وجود رسائل سابقة للعميل."`.
    - Set `generatingMemory(false)`.

---

### Rebuild, Verify & Test

- [x] T005 Rebuild frontend and backend containers:
  - Run: `docker compose up -d --build backend frontend`
- [x] T006 Create python integration test file `tests/phase_3/test_customer_memory_generation.py`:
  - Write test case that queries the endpoint `/api/projects/{projectId}/customers/{customerId}/memory/generate` for a customer with messages and asserts that it returns 200 OK with the generated facts, triggers, objections, and summary.
  - Write test case that queries the endpoint for a customer with no messages and asserts that it returns 400 Bad Request with the error message.
  - Run: `make down` -> `make up` -> `pytest tests/phase_3/test_customer_memory_generation.py` -> verify passes.
