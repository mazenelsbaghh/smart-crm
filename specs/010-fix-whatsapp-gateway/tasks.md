# Tasks: Fix WhatsApp Gateway Message Sending and Receiving

**Input**: Design documents from `/specs/010-fix-whatsapp-gateway/`

## Spec Kit Preparation Workflow

- [x] Spec Kit Phase 1: Feature Specification (`speckit-specify`) completed
- [x] Spec Kit Phase 2: Technical Planning (`speckit-plan`) completed
- [x] Spec Kit Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and basic structure

- [x] T001 Create achievements.md in root directory
- [x] T002 Create git branch 010-fix-whatsapp-gateway
- [x] T003 Generate spec.md, plan.md, research.md, data-model.md, contracts/gateway.json, and quickstart.md in specs/010-fix-whatsapp-gateway/

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure checks

- [ ] T004 Verify docker-compose.yml has environment variables configured for whatsapp-gateway and backend

---

## Phase 3: User Story 1 - Sending Messages from Agent to Customer (Priority: P1) 🎯 MVP

**Goal**: Implement JID formatting and connection validation when sending messages

**Independent Test**: Verify via pytest `tests/phase_1/test_whatsapp_gateway.py` or manually triggering send API using curl

### Implementation for User Story 1

- [ ] T005 [US1] Implement recipient phone number sanitization in `whatsapp-gateway/src/baileys-manager.js`'s `sendMessage` function to strip characters `+`, spaces, hyphens, and parentheses, appending `@s.whatsapp.net` unless it is already a JID
- [ ] T006 [US1] Add connection status validation in `whatsapp-gateway/src/baileys-manager.js`'s `sendMessage` function to throw an error if socket does not exist or status is not 'Connected'
- [ ] T007 [US1] Implement error logging and standard error forwarding inside `sendMessage` in `whatsapp-gateway/src/baileys-manager.js`

---

## Phase 4: User Story 2 - Receiving Messages from Customer (Priority: P1)

**Goal**: Extract message structures and forward to backend webhook

**Independent Test**: Simulating an incoming message on Baileys event listener and checking backend controller logs

### Implementation for User Story 2

- [ ] T008 [US2] Enhance message content extraction in `whatsapp-gateway/src/baileys-manager.js`'s `messages.upsert` event listener to handle conversation, extended text, image, and voice note blocks
- [ ] T009 [US2] Classify and set the `messageType` ('Text', 'Image', 'Voice') based on message structure in `whatsapp-gateway/src/baileys-manager.js`
- [ ] T010 [US2] Add robust try-catch block for the webhook POST request to prevent gateway crash if backend is temporarily unreachable

---

## Phase 5: User Story 3 - Session Auto-Restore on Startup (Priority: P2)

**Goal**: Restore logged-in sessions automatically on gateway startup

**Independent Test**: Re-run gateway service and verify session restore logs

### Implementation for User Story 3

- [ ] T011 [US3] Verify recursive directory scanning and startSession restore in `whatsapp-gateway/src/index.js`
- [ ] T012 [US3] Bind `creds.update` event to `saveCreds` function in `whatsapp-gateway/src/baileys-manager.js` to persist session key updates

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Testing, code review, and verification

- [ ] T013 Run `make test-phase-1` to verify all core tests pass
- [ ] T014 Review JID parsing console logs to confirm correct operation during manual or automated tests
- [ ] T015 Verify that the local Docker setup starts up cleanly without warning/error logs from the gateway

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: Completed.
- **Foundational (Phase 2)**: Core prerequisite checks.
- **User Story 1 (P1)**: High priority, blocks client replies.
- **User Story 2 (P1)**: High priority, blocks message receipt.
- **User Story 3 (P2)**: Medium priority, auto-restore reliability.
- **Polish (Phase 6)**: Run after implementation.

### Parallel Opportunities

- Phase 3, 4 and 5 implement different functions within `baileys-manager.js` and `index.js` and can be worked on sequentially or concurrently once foundation is verified.
