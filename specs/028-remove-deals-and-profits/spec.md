# Feature Specification: remove-deals-and-profits

**Feature Branch**: `028-remove-deals-and-profits`

**Created**: 2026-06-23

**Status**: Draft

**Input**: User description: "انا كنت عايز اشيل الارباح و الديلز و كده"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Clean Sidebar Navigation (Priority: P1)

As an application user, I want the sidebar menu to be clean of sales/deals terminology so that I focus only on messaging, clients, follow-ups, campaigns, workflows, knowledge base, and approvals.

**Why this priority**: Crucial for cleaning up the UI from unused sales-related modules.

**Independent Test**: Verify that the "مسار الصفقات" navigation link is completely removed from the desktop and mobile sidebars.

**Acceptance Scenarios**:

1. **Given** the user is logged into the application, **When** they view the side navigation, **Then** they should not see the "مسار الصفقات" menu item.
2. **Given** the user tries to manually navigate to `/crm/pipeline`, **Then** the application should either redirect them to `/dashboard` or show a 404/Not Found page, and not render the pipeline board.

---

### User Story 2 - Clean Dashboard Stats & Metrics (Priority: P1)

As an application user, I want the dashboard KPI dashboard to only display relevant metrics like Total Clients and Average Lead Score, removing Open Deals and Closed Revenue/Profits so that it matches our non-sales CRM operations.

**Why this priority**: Essential to align the main dashboard overview with the new client focus.

**Independent Test**: Verify that the "الصفقات المفتوحة" and "الإيراد المغلق" KPI cards are removed from the dashboard page.

**Acceptance Scenarios**:

1. **Given** the user is on the `/dashboard` page, **When** they look at the KPI stats grid, **Then** they should only see "إجمالي العملاء" (Total Clients) and "متوسط تقييم العملاء" (Average Lead Score) cards. The cards for "الصفقات المفتوحة" (Open Deals) and "الإيراد المغلق" (Closed Revenue) must be completely hidden/removed.

---

### User Story 3 - Clean Dashboard Quick Actions (Priority: P2)

As an application user, I want the quick actions/shortcuts card on the dashboard to not display the "مسار الصفقات" action card so that I do not trigger redundant modules.

**Why this priority**: High priority UX polish to ensure no dead links exist on the dashboard.

**Independent Test**: Verify that the quick action shortcut to "/crm/pipeline" is absent.

**Acceptance Scenarios**:

1. **Given** the user is on the `/dashboard` page, **When** they view the "إجراءات سريعة" (Quick Actions) panel, **Then** they should see other actions (e.g. "إدارة المحادثات") but the "مسار الصفقات" card must be completely removed.

---

### User Story 4 - Clean CRM Client Detailed Panel (Priority: P2)

As an application user, I want the detailed customer context page to not show any fields for Budget or Sales Pipeline Stage so that we only manage names, contact details, cities, notes, tags, follow-ups, and AI summaries.

**Why this priority**: Important to keep customer profiles simple and focused only on communications and scheduling.

**Independent Test**: Verify that in Customer Detail modal, budget and pipeline stages fields are hidden/removed.

**Acceptance Scenarios**:

1. **Given** the user is viewing a customer profile modal in `CustomerList.tsx`, **When** the details form renders, **Then** the fields "الميزانية ($)" and "مرحلة مسار المبيعات (Pipeline Stage)" must not be visible or editable.

### Edge Cases

- **Existing Data Integrity**: While the UI inputs for Budget and PipelineStage are removed, the database schema fields should remain intact to prevent breaking backend endpoints, database migrations, or existing database records. The backend should handle requests with default or existing stage values.
- **Direct Navigation**: If a user has bookmarked `/crm/pipeline`, navigating to it should redirect gracefully (e.g. to `/dashboard` or `/crm`) instead of causing a frontend crash.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: System MUST remove the "مسار الصفقات" menu item from navigation list in layout.
- **FR-002**: System MUST redirect or block access to `/crm/pipeline` to prevent rendering of the PipelineBoard page.
- **FR-003**: System MUST remove the KPI card for "الصفقات المفتوحة" from the Dashboard.
- **FR-004**: System MUST remove the KPI card for "الإيراد المغلق" from the Dashboard.
- **FR-005**: System MUST remove the quick action button for "مسار الصفقات" from the Dashboard quick action shortcuts list.
- **FR-006**: System MUST remove "الميزانية ($)" input field from `CustomerDetail.tsx`.
- **FR-007**: System MUST remove "مرحلة مسار المبيعات (Pipeline Stage)" selector field from `CustomerDetail.tsx`.

### Key Entities

- **Customer**: Represents a CRM client. Properties such as `budget` and `pipelineStage` are no longer exposed in the UI forms, though they remain in the database schema to preserve stability.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% success rate in hiding all deals and profits statistics from the user interface.
- **SC-002**: 0 items pointing to /crm/pipeline remain in navigation menus.
- **SC-003**: Frontend builds successfully without any TS/TSX compilation errors or dead imports related to the removed elements.

## Assumptions

- We do not need to delete the `Deals` table or remove the fields from the backend database schema, as doing so would require complex database migrations and could break historical database records or internal analytics engines. Hiding and disabling them from the UI satisfies the request while preserving backend stability.
