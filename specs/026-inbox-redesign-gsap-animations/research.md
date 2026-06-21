# Research: UX/UI Unified Inbox Redesign & GSAP Animations

## 1. Database Migrations

### Decision
We will add `PurchaseProbability` (int), `AIInsights` (string/text), and `AutomationRules` (string/text) to the `Customer.cs` EF Core model.
We will run:
```bash
dotnet ef migrations add AddCrmExtraFields
```
inside the `backend/` directory to create a migration. The backend automatically calls `context.Database.Migrate()` on startup, so the schema will be updated automatically in the development and production environments.

### Rationale
Keeping these fields directly in the `Customer` table is simple, highly performant, and conforms to Principle II (Multi-Tenant Project Isolation) since the table already has `ProjectId` scopes and global query filters.

### Alternatives Considered
- Storing fields in a separate `CustomerCrmDetails` table. Rejected because it adds unnecessary table joins and complexity without any performance benefit.

---

## 2. GSAP Animations in Next.js (React 19)

### Decision
We will install `gsap` and `@gsap/react` in the frontend dependencies.
To build animations, we will use the `@gsap/react` hook `useGSAP` to safely register and scope animations, avoiding memory leaks on component unmount.
For stagger effects (e.g. Metric cards entry):
```typescript
useGSAP(() => {
  gsap.from(".kpi-card", {
    y: 30,
    opacity: 0,
    stagger: 0.1,
    duration: 0.4,
    ease: "power2.out"
  });
}, { scope: containerRef });
```

### Rationale
`useGSAP` is the officially supported, hook-based wrapper for GSAP in React environments, making animation scoping clean and safe under React 19 concurrent renders.

---

## 3. UI Component Unification

### Decision
We will create a unified `InboxLayout.tsx` component in `src/packages/inbox/`.
This component will structure:
1. Left Sidebar menu.
2. Conversation Worklist (filtering logic, metrics, list of cards).
3. Workspace (Active Customer header, tabs, chat history, composer).
4. Right context sidebar panel.

`Inbox.tsx`, `MessengerInbox.tsx`, and `CommentsInbox.tsx` will imports and render `InboxLayout`, passing channel-specific parameters (e.g. channel: `'WhatsApp' | 'Messenger' | 'Comments'`), list items, message histories, and socket events.

### Rationale
Reduces code duplication across the three inboxes by 60-70%, ensuring styling changes or layout modifications are updated globally from one place, rather than duplicating the layout across three separate files.
