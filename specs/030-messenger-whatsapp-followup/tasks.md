# Tasks: Messenger & WhatsApp Follow-Up Integration

**Input**: Design documents from `/specs/030-messenger-whatsapp-followup/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

## Spec Kit Preparation Workflow

- [x] Phase 1: Feature Specification (`speckit-specify`)
- [x] Phase 2: Arabic Clarification (`speckit-clarify`)
- [x] Phase 3: Technical Planning (`speckit-plan`)
- [x] Phase 4: Detailed Task Breakdown (`speckit-tasks`)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 Create project structure specs/030-messenger-whatsapp-followup per implementation plan
- [x] T002 Update plan reference in AGENTS.md to specs/030-messenger-whatsapp-followup/plan.md

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

- [x] T003 Verify AppDbContext.cs contains necessary DbSets (Projects, ProjectSettings, Customers, FollowUps, ConnectedPages)

---

## Phase 3: User Story 1 - Messenger First Session Reminder (Priority: P1)

**Goal**: Messenger AI responder explicitly reminds the customer that their first session is free.

**Independent Test**: Send a Messenger message to the FB page and check if the AI responder includes the Arabic free session reminder.

### Implementation for User Story 1

- [x] T004 [US1] Update `backend/src/Modules/AI/Workers/AIReplyWorker.cs` to append a "first session free" reminder guideline to the `channelAwarenessContext` if the incoming channel is Messenger. Expected result is that Gemini receives this directive in its context.

---

## Phase 4: User Story 2 - Capture Phone Number and Send WhatsApp Message (Priority: P1)

**Goal**: Extract phone number from Messenger messages, update Customer profile, and trigger welcome WhatsApp message, with fallback to Messenger alert on failure.

**Independent Test**: Simulate sending an Egyptian phone number via Messenger; check that a WhatsApp message is sent, Messenger alerts are sent, and database updates occur.

### Implementation for User Story 2

- [x] T005 [P] [US2] Implement helper methods `NormalizeDigits` and `ExtractEgyptianPhoneNumber` in `backend/src/Modules/AI/Workers/AIReplyWorker.cs` using Regular Expressions to identify valid Egyptian numbers from chat inputs. Verify it passes basic inputs.
- [x] T006 [US2] Update `backend/src/Modules/AI/Workers/AIReplyWorker.cs` to intercept Messenger messages, extract phone numbers, call the WhatsApp Gateway `/api/whatsapp/send`, send a Messenger confirmation on success, and send a Messenger fallback warning message on HTTP/sending failure.
- [x] T007 [US2] Implement database logic in `backend/src/Modules/AI/Workers/AIReplyWorker.cs` to cancel (mark "Cancelled") all pending follow-ups for the customer when the transition succeeds.

---

## Phase 5: User Story 3 - Transition Follow-Up Channel (Priority: P2)

**Goal**: Route scheduled follow-ups to Messenger if customer has no PhoneNumber but has FacebookPSID.

**Independent Test**: Schedule a follow-up for a customer who only has FacebookPSID and verify that the Hangfire scheduler routes the follow-up text to the Facebook page's Graph API.

### Implementation for User Story 3

- [x] T008 [US3] Update `backend/src/Modules/CRM/Services/FollowUpScheduler.cs` to check if `customer.PhoneNumber` is empty and `customer.FacebookPSID` is not empty, resolve `IFacebookGraphService` via DI, and route the message via Messenger instead of WhatsApp. Expected result is successful message delivery on Messenger.

---

## Phase 6: Polish, Verification & Quality Guards

**Purpose**: Verify implementation quality, compile code, and run tests.

- [x] T009 Perform deep architectural audit on Monolith code layers and project isolation boundaries in the changed backend files.
- [x] T010 Run `clean-code-guard` against changed backend production code files to ensure SOLID principles.
- [x] T011 Run `test-guard` against changed test files (if any).
- [x] T012 Run final feature tests via `pytest` to verify Messenger to WhatsApp transition logic. Expected outcome is 100% test pass.
- [x] T013 Final build compilation check with `dotnet build` at backend directory.
