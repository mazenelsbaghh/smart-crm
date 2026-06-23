# Implementation Plan: Knowledge Base AI Wizard

**Branch**: `027-knowledge-base-ai-wizard` | **Date**: 2026-06-23 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/027-knowledge-base-ai-wizard/spec.md`

## Summary

Implement an AI-powered Knowledge Base Wizard that allows users to paste raw text, guides them through an interactive Arabic clarifying interview (questions with 3 options + a custom input), compiles their answers into structured Q&A pairs, and saves them to the vector-enabled database with correct boundary-aware chunking.

## Technical Context

**Language/Version**: C# (.NET 8), TypeScript (React 18 / Next.js)

**Primary Dependencies**: ASP.NET Core, Microsoft.EntityFrameworkCore, Gemini 3.5 Flash (`IGeminiClient`)

**Storage**: PostgreSQL (`pgvector` for embeddings via `Pgvector.EntityFrameworkCore`)

**Testing**: Manual E2E verification + backend compilation validation

**Target Platform**: Ubuntu Linux server (Docker/Docker Compose deployment)

**Project Type**: Web Application (React frontend + ASP.NET Core backend api)

**Performance Goals**: AI clarification questions generated under 5 seconds; page routing < 200 milliseconds.

**Constraints**: Strict multi-tenant isolation context (`ProjectId`). Chunking size limit <= 800 characters per chunk, with boundary-aware Q&A preservation.

**Scale/Scope**: Single multi-step React stepper component, updated sidebar, two backend API endpoints, and modified chunking service.

## Constitution Check

- **Principle I (Modular Monolith)**: Complies. All backend code resides inside the `Modules.Brain` namespace and does not tightly couple with other modules.
- **Principle II (Strict Multi-Tenant Isolation)**: Complies. All operations query and persist data restricted by the user's `ProjectId`.
- **Principle III (Gemini 3.5 Flash)**: Complies. Questions and Q&A pairs are generated directly by calling `IGeminiClient` with the Gemini 3.5 Flash model.
- **Principle V (Human-in-the-Loop)**: Complies. Documents generated via wizard default to "Draft" state, requiring explicit review/approval or editing in the UI.

## Project Structure

### Documentation (this feature)

```text
specs/027-knowledge-base-ai-wizard/
├── plan.md              # This file
├── research.md          # Research decisions
├── data-model.md        # Data model representation
├── quickstart.md        # Run and verification steps
├── contracts/
│   └── api.md           # API endpoints contracts
└── checklists/
    └── requirements.md  # Spec checklist
```

### Source Code (repository root)

```text
backend/
└── src/
    └── Modules/
        └── Brain/
            ├── API/
            │   └── KnowledgeBaseController.cs     # Add wizard/analyze and wizard/generate endpoints
            └── Services/
                └── KnowledgeBaseService.cs        # Implement boundary-aware Q&A chunking algorithm

frontend/
└── src/
    └── packages/
        ├── inbox/
        │   └── shared/
        │       └── ThinSidebar.tsx                # Add "قاعدة المعرفة" link to sidebar
        └── management/
            └── KnowledgeBase.tsx                  # Integrate raw text input, interactive animated stepper wizard, and reviewable Q&A list
```

**Structure Decision**: Web application option (split backend/frontend directories).

## Phase 0: Outline & Research

We researched how to partition and represent Q&A entries in `research.md`. The design leverages C# text grouping of Q&A blocks to ensure the backend generates complete and contiguous chunks.

## Phase 1: Design & Contracts

We defined the interface contract for two new API endpoints under `contracts/api.md` and represented the data schema updates in `data-model.md`.

### Proposed Changes

#### Backend Brain Module

##### [MODIFY] [KnowledgeBaseController.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Brain/API/KnowledgeBaseController.cs)
- Add API models for the Wizard endpoints:
  - `WizardAnalyzeRequest { RawText }`
  - `WizardQuestionDto { Question, Options }`
  - `WizardGenerateRequest { RawText, Answers }`
  - `WizardAnswerDto { Question, Answer }`
  - `WizardQaPairDto { Question, Answer }`
- Add two HTTP POST actions:
  - `POST api/projects/{projectId}/knowledge/wizard/analyze`
  - `POST api/projects/{projectId}/knowledge/wizard/generate`
- Inject `IGeminiClient` into the controller to handle prompts.

##### [MODIFY] [KnowledgeBaseService.cs](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/backend/src/Modules/Brain/Services/KnowledgeBaseService.cs)
- Update `GenerateChunksAndEmbeddingsAsync` method:
  - Check if `doc.Content` contains Q&A structure (lines starting with `س:` and `ج:`).
  - If Q&A matches, group questions and answers together as complete blocks.
  - Accumulate Q&A blocks into chunks up to 800 characters.
  - If standard text, fall back to paragraph-based chunking.

#### Frontend CRM/Management Module

##### [MODIFY] [ThinSidebar.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/inbox/shared/ThinSidebar.tsx)
- Import `BookOpen` icon.
- Add `{ name: 'قاعدة المعرفة', path: '/management/knowledge', icon: BookOpen }` before the Settings menu item.

##### [MODIFY] [KnowledgeBase.tsx](file:///Users/mazenelsbagh/mazen%20mac/apps/smart%20whatsapp/frontend/src/packages/management/KnowledgeBase.tsx)
- Add a new "معالج الذكاء الاصطناعي" button next to "إضافة نص يدوياً".
- When clicked, open a stepper panel/modal:
  - **Step 1: Raw Text Input**. User enters raw text. Click next to trigger `wizard/analyze`.
  - **Step 2: Clarifying Questions**. Display stepper one question at a time. Each question shows 3 suggested options and a custom input. Click next to move through questions.
  - **Step 3: Review Q&A List**. Displays generated Q&A list. User can add, edit, or delete items.
  - **Step 4: Save Document**. Saves document with chosen Title and structured Q&A content.
- Enhance with CSS/GSAP smooth transitions.

## Verification Plan

### Automated Tests
- Run backend compilation: `dotnet build` inside the backend directory.
- Run frontend validation: `npm run build` inside the frontend directory (if configured, or check for compilation).

### Manual Verification
- Deploy to localhost and run verification steps detailed in `quickstart.md`.
- Verify vector storage and boundary-safe chunking via Postgres pgvector checks.
