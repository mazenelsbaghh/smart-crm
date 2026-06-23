# Feature Specification: Knowledge Base AI Wizard

**Feature Branch**: `027-knowledge-base-ai-wizard`

**Created**: 2026-06-23

**Status**: Draft

**Input**: User description: "عايزك تضيف ف انيف بار تحط القاعده المعرفيه و عايز يتحط تيسكت وعايز يبقي كولتي عادي انو بعد مايكون تيكست يجرب يخليه سوال و جواب و ف الاول يسالني عليها علشان تبقي القعده المعرفيه تبقي شامله ولامه يعني يسالني علي كل حاجه فيها و عايزها تتخزن كسوال و جواب و الشرينك تكون مظبوطه"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Accessing the Knowledge Base & Inputting Raw Text (Priority: P1)

The user accesses the platform, sees "قاعدة المعرفة" (Knowledge Base) in the navigation sidebar, and clicks it to go to the management page. The page contains a text input area where they can type or paste unstructured text about their business, and a button to initiate the AI wizard.

**Why this priority**: Crucial for entry point and baseline MVP.

**Independent Test**: User can click the navbar link, view the textarea, paste text, and trigger the wizard.

**Acceptance Scenarios**:
1. **Given** the user is logged in, **When** they look at the sidebar, **Then** they see "قاعدة المعرفة" with a BookOpen icon.
2. **Given** the user is on the Knowledge Base page, **When** they paste text in the input area and click "بدء معالج الأسئلة والأجوبة", **Then** the AI Wizard starts.

---

### User Story 2 - Interactive AI Clarification Interview in Arabic (Priority: P1)

The AI analyzes the raw text pasted by the user and dynamically generates a list of clarifying questions in Arabic for everything it finds incomplete or unclear in the text. For each question, the AI provides 3 multiple-choice options (suggested answers) along with an option to write a custom answer (free-form text input). The UI presents these questions in an interactive, step-by-step wizard (Stepper) with premium animations.

**Why this priority**: Required by the user to ensure the knowledge base is comprehensive and covers all nuances before Q&A generation.

**Independent Test**: AI analyzes a brief prompt (e.g., "نحن شركة شحن في القاهرة") and asks clarifying questions (e.g., "ما هي أسعار الشحن؟", "ما هي مناطق التوصيل؟"), each with 3 suggested options and a custom input.

**Acceptance Scenarios**:
1. **Given** the user submitted raw text, **When** the AI analyzes it, **Then** it generates relevant clarifying questions in Arabic, each with exactly 3 predefined options and a custom input field.
2. **Given** the questions are generated, **When** they are presented in the wizard UI, **Then** the user can select one of the 3 options or type a custom answer, and navigate between questions with transitions.

---

### User Story 3 - Structured Q&A Generation and Correct Chunking/Shrinking (Priority: P1)

Once the user submits their answers to the clarifying questions, the AI compiles the raw text and answers, transforming them into structured Q&A pairs (سؤال وجواب). These pairs are displayed to the user for review (with editing and deleting capabilities). When saved, they are stored in the database. The backend splits the document chunks cleanly at complete Q&A boundaries (rather than by a random character limit), ensuring that every chunk holds a complete Q&A pair and does not truncate text mid-sentence (correct shrinking/chunking).

**Why this priority**: Essential to fulfill the user's requirement of Q&A storage and correct chunking.

**Independent Test**: The system saves the document, and the generated database chunks split cleanly at Q&A boundaries instead of breaking mid-sentence.

**Acceptance Scenarios**:
1. **Given** the user completes the interview, **When** the AI generates Q&A pairs, **Then** the user can edit or delete individual pairs.
2. **Given** the user saves the Q&As, **When** the backend chunks the document, **Then** it splits chunks cleanly at Q&A boundaries so that search matching retrieves the full Q&A block.

---

### Edge Cases

- **Empty or Too Short Text**: User submits empty text or text with less than 20 characters. The system should show a friendly validation message in Arabic.
- **AI Timeout or Network Error**: If the Gemini API fails during question generation or Q&A conversion, a friendly error message is shown, and the user can retry.
- **Large Text Chunks**: A single generated Q&A pair exceeds the maximum chunk length. The system should split it gracefully without throwing an exception.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST add a sidebar item "قاعدة المعرفة" routing to `/management/knowledge` in `ThinSidebar.tsx`.
- **FR-002**: Knowledge Base UI MUST provide a text area for raw text input and a button to initiate the AI wizard.
- **FR-003**: System MUST request Gemini (via `IGeminiClient`) to analyze raw text and produce clarifying questions in Arabic for any gaps, each containing exactly 3 suggested answers and a field for a custom answer.
- **FR-004**: UI MUST render a step-by-step interactive wizard stepper for the clarifying questions, displaying suggested answers and custom input options.
- **FR-005**: System MUST request Gemini to convert the original text and user answers into structured Q&A pairs in Arabic.
- **FR-006**: UI MUST allow the user to review, edit, add, or delete the generated Q&A pairs.
- **FR-007**: Backend MUST chunk the final content such that Q&As are kept together in chunks as much as possible, with correct vector embedding generation (correct shrinking).

### Key Entities

- **KnowledgeDocument**: Represents the finalized knowledge resource, containing the full text of the approved Q&As.
- **KnowledgeChunk**: Represents a vector-searchable chunk of the document. Each chunk should hold clean Q&A pairs.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Sidebar link "قاعدة المعرفة" is visible and routes to `/management/knowledge`.
- **SC-002**: AI generated questions are retrieved and displayed in less than 5 seconds from text submission.
- **SC-003**: Q&A pairs are cleanly saved, and database chunks do not truncate Q&As midway (i.e. every chunk starts with a "س:" or the question and contains the complete Q&A text).
- **SC-004**: System successfully searches and matches these chunks when querying the Vector DB in the chat gateway.

## Assumptions

- We will reuse the existing `KnowledgeDocument` and `KnowledgeChunk` models.
- The Gemini API will be used for both generating questions and converting text/answers to Q&As.
- The UI will be localized in Arabic to fit the existing application theme and branding.
- GSAP or CSS transition animations will be used to make the wizard feel premium and smooth.

