# Tasks: Customer Blacklist for AI Exclusion

**Input**: Design documents from `/specs/020-customer-blacklist/`

**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, quickstart.md

## Spec Kit Preparation Workflow
- [x] Phase 1: Feature Specification (`speckit-specify`) completed
- [x] Phase 2: Technical Planning (`speckit-plan`) completed
- [x] Phase 3: Detailed Task Breakdown (`speckit-tasks`) completed

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Database schema creation.

- [x] T001 Generate an EF Core migration in `backend/` by running `dotnet ef migrations add AddIsBlacklistedToCustomer` on the host machine.
- [x] T002 Apply migrations to the database by restarting the backend container using `make db-migrate` or running the update process.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core model updates.

- [x] T003 In file `backend/src/Modules/Conversations/Domain/Customer.cs`, add property `public bool IsBlacklisted { get; set; } = false;` to the `Customer` class definition.
- [x] T004 In file `backend/src/Shared/Infrastructure/DbSeeder.cs`, check if any seeded customers require initialization, ensuring they default `IsBlacklisted` to `false` (implicit by default value or set explicitly).

**Checkpoint**: Foundation ready - user story implementation can now begin.

---

## Phase 3: User Story 1 - Toggle Blacklist Status for a Customer (Priority: P1)

**Goal**: Implement the toggling behavior in frontend and backend.

**Independent Test**: Update a customer blacklist status from the frontend CRM card and check if the change is saved in the database.

### Implementation for User Story 1 (Backend API)

- [x] T005 Update the `UpdateCustomerRequest` class in `backend/src/Modules/CRM/API/CRMController.cs` to add property `public bool? IsBlacklisted { get; set; }`.
- [x] T006 Update the `UpdateCustomer` action method in `backend/src/Modules/CRM/API/CRMController.cs` to apply `request.IsBlacklisted.Value` to `customer.IsBlacklisted` if `request.IsBlacklisted.HasValue` is true.
- [x] T007 Update the projection outputs in `GetCustomers`, `GetCustomer`, and `UpdateCustomer` action methods of `backend/src/Modules/CRM/API/CRMController.cs` to include `IsBlacklisted = c.IsBlacklisted` (or `customer.IsBlacklisted`).

### Implementation for User Story 1 (Frontend CRM)

- [x] T008 In file `frontend/src/services/crm.ts`, update the `Customer` interface definition to include `isBlacklisted?: boolean;`.
- [x] T009 In file `frontend/src/components/shared/CustomerDetail.tsx`, add a state variable `const [isBlacklisted, setIsBlacklisted] = useState(false);` and initialize it inside `fetchCustomerData` using `data.isBlacklisted || false`.
- [x] T010 In file `frontend/src/components/shared/CustomerDetail.tsx`, update `handleSave` to include `isBlacklisted` in the payload passed to `crmService.updateCustomer`.
- [x] T011 In file `frontend/src/components/shared/CustomerDetail.tsx`, add a checkbox field in the profile edit form (e.g. right before the Notes textarea) with label:
  `حظر الرد الآلي بالذكاء الاصطناعي (Blacklist)` and link it to the `isBlacklisted` state.

---

## Phase 4: User Story 2 - AI Auto-Reply Exclusion (Priority: P1)

**Goal**: Bypassing auto-replies and typing indicators for blacklisted customers.

**Independent Test**: Verify that sending a message from a blacklisted customer number does not trigger the AI reply or typing status.

### Implementation for User Story 2

- [x] T012 In file `backend/src/Modules/Conversations/API/WebhookController.cs` in the `ReceiveMessage` method, check `customer.IsBlacklisted` before broadcasting `AITyping = true`. Only broadcast `AITyping = true` if `!customer.IsBlacklisted`.
- [x] T013 In file `backend/src/Modules/AI/Workers/AIReplyWorker.cs` in the `HandleAsync` method, fetch the customer early, check if `customer != null && customer.IsBlacklisted` is true, and if so, log `"[AIReplyWorker] Customer is blacklisted. Skipping AI reply."` and return immediately.

---

## Phase 5: User Story 3 - Visual Indicators in CRM Customer List (Priority: P2)

**Goal**: Render visual indicators for blacklisted customers.

**Independent Test**: Load the Customer List in CRM and see the red/muted badge next to the blacklisted customer.

### Implementation for User Story 3

- [x] T014 In file `frontend/src/packages/crm/CustomerList.tsx` inside the customer table cell mapping, check if `c.isBlacklisted` is true.
- [x] T015 If `c.isBlacklisted` is true, render a visual badge (styled with custom CSS red/muted colors) saying: `حظر رد آلي` (Blocked AI) next to their phone/label inside the cell.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cleanup, builds, and test verification.

- [x] T016 Verify that frontend builds successfully using `npm run build` or validating code without typescript errors.
- [x] T017 Verify that backend builds successfully.
- [x] T018 Write a new python test case in `tests/phase_2/test_human_messaging.py` to ensure that blacklisting a customer skips AI reply generation.
- [x] T019 Run tests using `make test-phase-2` and ensure all tests pass.
