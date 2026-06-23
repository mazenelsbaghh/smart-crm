# Research & Design Decisions: Knowledge Base AI Wizard

## Decisions & Rationale

### 1. Backend API Design for the Wizard

We need two new endpoints on `KnowledgeBaseController` to handle the interactive AI workflow:

1. **Analyze Raw Text & Generate Questions**:
   - **Route**: `POST api/projects/{projectId}/knowledge/wizard/analyze`
   - **Input**: `{ "rawText": "..." }`
   - **Output**: A JSON array of questions, each with exactly 3 suggested option answers and an optional free-text answer:
     ```json
     [
       {
         "question": "ما هي مواعيد العمل الرسمية لديكم؟",
         "options": ["من 9 صباحاً إلى 5 مساءً", "من 10 صباحاً إلى 10 مساءً", "طوال أيام الأسبوع على مدار 24 ساعة"]
       }
     ]
     ```
   - **Implementation**: Calls Gemini 3.5 Flash with a system prompt asking it to find gaps, missing details, or assumptions in the raw text, and formulate clarifying questions in Arabic with 3 suggested options.

2. **Generate Q&As**:
   - **Route**: `POST api/projects/{projectId}/knowledge/wizard/generate`
   - **Input**:
     ```json
     {
       "rawText": "...",
       "answers": [
         { "question": "...", "answer": "..." }
       ]
     }
     ```
   - **Output**: JSON array of finalized Q&A pairs:
     ```json
     [
       {
         "question": "هل يتوفر لديكم شحن للمحافظات؟",
         "answer": "نعم، متوفر شحن لجميع محافظات مصر وتستغرق مدة التوصيل من يومين إلى 4 أيام عمل."
       }
     ]
     ```
   - **Implementation**: Calls Gemini 3.5 Flash to combine the raw text and answers, formulating a set of comprehensive Q&A pairs in clean Arabic.

### 2. Frontend Interactive Stepper UI

- **Location**: `/management/knowledge` route in `KnowledgeBase.tsx` page.
- **Visuals**: A clean, premium modal or stepper card that overlays or replaces the text area.
- **Workflow**:
  1. User pastes raw text and clicks **"بدء معالج الذكاء الاصطناعي"**.
  2. Stepper starts. For each question:
     - Show the question text clearly.
     - Present the 3 suggested answers as clickable buttons (pills) to auto-fill the answer.
     - Provide a textarea for a custom answer if the suggested options aren't fully accurate.
     - Navigation controls: **"السابق"** and **"التالي / تأكيد"**.
  3. After completing the stepper, the user clicks **"توليد الأسئلة والأجوبة"**.
  4. The generated Q&A pairs are shown in an editable list. The user can add new pairs, edit the text of any pair, or delete pairs.
  5. The user clicks **"حفظ ونشر"** to create a `KnowledgeDocument` in the backend database.

### 3. Chunking & Shrinking Strategy ("الشرينك تكون مظبوطه")

To ensure Q&A pairs are never split across separate vector chunks, we will modify the chunking logic in `KnowledgeBaseService.cs`:

- **Detection**: Check if the document content follows the structured Q&A format (i.e. lines starting with `س:` and `ج:`).
- **Algorithm**:
  1. Group each `س:` and its subsequent `ج:` into a single `QaBlock`.
  2. Iterate through `QaBlocks`.
  3. Combine multiple `QaBlocks` into a single `KnowledgeChunk` as long as their total character count is less than 800 characters.
  4. If adding another `QaBlock` would exceed 800 characters, save the current chunk and start a new one.
  5. If a single `QaBlock` itself exceeds 800 characters, save it as a standalone chunk (and only split it by paragraph if it exceeds 1500 characters to prevent overflow, though in practice Gemini-generated Q&As are short).
- **Benefits**: Ensures complete question-answer units are stored together. When a customer asks a question, the vector search retrieves the complete Q&A block instead of getting a cut-off question or answer.

## Alternatives Considered

- **Inline Chatbot for Clarification**: Rejected because a chatbot conversation is unpredictable and slower for users compared to a structured multi-step stepper with suggested answers.
- **Standard Character-Based Chunking**: Rejected because it frequently splits questions from their answers, resulting in poor AI replies in WhatsApp gateway.
