# Technical Implementation Plan: Frontend Clean Code & CSS Modules Refactoring

## 1. Summary
Refactor the frontend Next.js App Router codebase to segregate concerns modularly. Reorganize components into dedicated packages and shared files. Extract all inline CSS definitions into Vanilla CSS Modules (`.module.css`) supporting standard CSS selectors (like `:hover`, `:disabled`, `@media`). Verify zero build warnings or compilation regressions.

---

## 2. Refactoring Structure & File Paths

We will move the current logic into a cleanly modular file layout:

| Source File | Destination File (TSX/CSS) | Role |
| :--- | :--- | :--- |
| `src/app/page.tsx` | `src/packages/auth/Login.tsx`<br>`src/packages/auth/auth.module.css` | **Login Form**: Login state, submission logic, auth layouts. |
| `src/app/register/page.tsx` | `src/packages/auth/Register.tsx`<br>`src/packages/auth/auth.module.css` | **Register Form**: Agent creation interface. |
| `src/app/(dashboard)/layout.tsx` | `src/components/layout/Sidebar.tsx`<br>`src/components/layout/Header.tsx`<br>`src/components/layout/layout.module.css` | **Global App Shell**: Projects switching, collapsible sidebar. |
| `src/app/(dashboard)/dashboard/page.tsx` | `src/packages/dashboard/Dashboard.tsx`<br>`src/packages/dashboard/dashboard.module.css` | **Metrics Dashboard**: Quick links, summary charts. |
| `src/app/(dashboard)/inbox/page.tsx` | `src/packages/inbox/Inbox.tsx`<br>`src/packages/inbox/inbox.module.css` | **Real-Time Inbox**: SignalR listeners, chat panel. |
| `src/app/(dashboard)/crm/page.tsx` | `src/packages/crm/CustomerList.tsx`<br>`src/packages/crm/crm.module.css` | **Customer Directory**: Contacts filtering, list table. |
| `src/app/(dashboard)/crm/pipeline/page.tsx` | `src/packages/crm/PipelineBoard.tsx`<br>`src/packages/crm/crm.module.css` | **Kanban Deals Board**: Opportunity stages, drag-drop. |
| `src/components/CustomerDetail.tsx` | `src/components/shared/CustomerDetail.tsx`<br>`src/components/shared/customer-detail.module.css` | **Profile Drawer**: Note updates, tagging, follow-ups. |
| `src/app/error.tsx` | `src/app/error.tsx`<br>`src/packages/error/ErrorBoundary.tsx`<br>`src/packages/error/error-boundary.module.css` | **Fallback Error Page**: Runtime recovery, glassmorphic layout. |

---

## 3. Styling Guidelines (CSS Modules Integration)

- **Import Method**: In each TSX file, import styles locally:
  ```tsx
  import styles from './auth.module.css';
  ```
- **Class Reference**: Apply classes cleanly:
  ```tsx
  className={styles.container}
  ```
- **Design Tokens**: Access root variables defined in `src/styles/variables.css` via `var(--variable-name)` inside CSS Modules. E.g.:
  ```css
  .container {
    background: radial-gradient(circle, hsl(var(--accent-primary)) 0%, transparent 100%);
    padding: var(--space-md);
  }
  ```
- **Interactions**: Separate CSS allows using native pseudo-classes instead of React state heuristics for focus and hover states:
  ```css
  .button:hover:not(:disabled) {
    background: hsl(var(--accent-secondary));
    transform: translateY(-1px);
  }
  ```

---

## 4. Verification Plan

### Automated Verification
- Run `npm run build` inside `frontend/` to guarantee zero bundling or TypeScript compiler warnings/errors.
- Execute all tests via `make test-all` and `make test-phase-6` to ensure no regression in client APIs or SignalR real-time messaging.

### Manual Verification
- Deploy using `make deploy` to verify that the Docker setup builds the Next.js app under the new folder structure.
