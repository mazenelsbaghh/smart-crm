# Feature Specification: Frontend Clean Code, Modular Packaging & CSS Separation

## 1. Goal & Context

Refactor the Next.js frontend codebase to conform to strict Clean Code and modular architecture principles. The goal is to:
- Divide component files into dedicated feature packages (`packages/`), general components (`components/`), and reusable shared components (`components/shared/` or `components/`).
- Fully separate layout styling from React TSX code by migrating from inline styles to Next.js Vanilla CSS Modules (`.module.css`).
- Ensure the Docker deployment files and Next.js compilation work flawlessly in this new modular directory structure.

---

## 2. User Stories

### US1: Feature Packages & Shared Components Reorganization
- **As a Developer**, I want the codebase to be modularly structured so that feature-specific logic is isolated from global reusable components.
- **Requirements**:
  - Reorganize all frontend business features under `frontend/src/packages/`:
    - `packages/auth/`: Login and Register forms, state logic, and auth styles.
    - `packages/dashboard/`: KPI cards, quick actions widget.
    - `packages/inbox/`: Three-panel chat interface (ConversationList, ChatArea, smart replies).
    - `packages/crm/`: Customer contact listing, Deals Kanban board pipeline.
  - Extract reusable components into `frontend/src/components/` and `frontend/src/components/shared/`:
    - `CustomerDetail.tsx` (sidebar profile inspector drawer).
    - App Layout shells (Sidebar navigation, Header context selector, Mobile header).
  - Convert files under the Next.js App Router (`frontend/src/app/`) into thin router entrypoints that simply import and render components from the feature packages.

### US2: Separation of CSS (CSS Modules)
- **As a Designer & Developer**, I want CSS rules to be decoupled from component logic to improve readability, maintainability, and compile-time optimization.
- **Requirements**:
  - Eliminate all inline React style objects (e.g., `const styles: { [key: string]: React.CSSProperties } = { ... }`).
  - Move these style declarations into Vanilla CSS Modules (`.module.css`) placed alongside their respective React files.
  - Reference classes dynamically via `className={styles.containerClassName}`.
  - Support high-fidelity visuals (glassmorphism, borders, dark theme glows) using CSS variables imported from `src/styles/variables.css`.
  - Handle hover effects, focus rings, disabled states, and responsive styles cleanly using native CSS media queries and pseudo-classes.

### US3: Multi-Stage Docker Build Alignment
- **As an Operations Engineer**, I want the Docker stack to compile the updated Next.js application without issues.
- **Requirements**:
  - Verify that `frontend/Dockerfile` correctly copies and compiles all files in the new directory structure.
  - Ensure zero TypeScript compilation warnings/errors and zero PostCSS bundling warnings during `npm run build`.

---

## 3. Directory Layout Target

```text
frontend/src/
├── app/                      # Next.js App Router Entrypoints (Thin wrappers)
│   ├── page.tsx              # Imports and renders <Login />
│   ├── error.tsx             # Imports and renders <ErrorBoundary />
│   ├── register/
│   │   └── page.tsx          # Imports and renders <Register />
│   └── (dashboard)/
│       ├── layout.tsx        # Imports and renders <DashboardLayout />
│       ├── dashboard/
│       │   └── page.tsx      # Imports and renders <Dashboard />
│       ├── inbox/
│       │   └── page.tsx      # Imports and renders <Inbox />
│       └── crm/
│           ├── page.tsx      # Imports and renders <CustomerRegistry />
│           └── pipeline/
│               └── page.tsx  # Imports and renders <DealsPipeline />
│
├── packages/                 # Isolated Feature Packages / Modules
│   ├── auth/
│   │   ├── Login.tsx
│   │   ├── Register.tsx
│   │   └── auth.module.css
│   ├── dashboard/
│   │   ├── Dashboard.tsx
│   │   └── dashboard.module.css
│   ├── inbox/
│   │   ├── Inbox.tsx
│   │   └── inbox.module.css
│   └── crm/
│       ├── CustomerList.tsx
│       ├── PipelineBoard.tsx
│       └── crm.module.css
│
├── components/               # UI components
│   ├── layout/
│   │   ├── Sidebar.tsx
│   │   ├── Header.tsx
│   │   └── layout.module.css
│   └── shared/               # Reusable shared components
│       ├── CustomerDetail.tsx
│       ├── error-boundary.module.css
│       └── customer-detail.module.css
```

---

## 4. Acceptance Criteria
- [ ] No inline style objects (`style={styles.foo}`) in any React component.
- [ ] All components reside in their correct feature packages or shared folders.
- [ ] Thin routing wrapper pages in `src/app/` only import and mount package components.
- [ ] All page transitions, WebSocket events, and API calls function identically to before.
- [ ] The command `npm run build` runs successfully with zero TypeScript or styling warnings.
- [ ] The full verification suite (`make test-all` and `make test-phase-6`) runs and passes completely.
