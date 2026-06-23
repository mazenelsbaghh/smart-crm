# Technical Research: remove-deals-and-profits

**Created**: 2026-06-23

## Decisions & Rationale

### Decision 1: Do Not Modify Backend Database Schema or Controllers
- **Choice**: Keep the C# model fields for budget, pipeline stage, and `Deals` table. Only remove their display and inputs from the frontend.
- **Rationale**: Deleting database columns or tables could break dependency references in historical records, C# migrations, internal analytics queries, or API endpoints. Leaving the database untouched while stripping the fields from frontend views is extremely safe, simple, and satisfies the user's intent to "remove the profits, deals and related stuff".
- **Alternatives Considered**: Modifying the database schema. Rejected because of migration complexity and potential risk of database downtime or data loss on the remote production server.

### Decision 2: Remove Quick Action Shortcut and KPI Stats from Dashboard
- **Choice**: Completely remove the "Open Deals" and "Closed Revenue" statistics cards from the dashboard, along with the "Pipeline Board" link in Quick Actions.
- **Rationale**: Removing these elements ensures the dashboard only presents customer count and customer lead score, keeping the UX clean and accurate.
- **Alternatives Considered**: Leaving them as $0 or hidden behind a setting. Rejected because the user wants them removed completely.

### Decision 3: Remove Budget and Pipeline Stage inputs from CustomerDetail modal
- **Choice**: Remove "الميزانية ($)" and "مرحلة مسار المبيعات (Pipeline Stage)" inputs.
- **Rationale**: These input fields are only relevant to deals and profits, so removing them is required to achieve a clean CRM customer profile interface.
