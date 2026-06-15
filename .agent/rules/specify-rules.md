# nader gorge Development Guidelines

Auto-generated from all feature plans. Last updated: 2026-06-09

## Active Technologies
- C# (.NET 8), TypeScript 5.x (strict mode) + Next.js 14 (App Router), .NET Web API, Entity Framework Core, MediatR, React Query (TanStack Query v5), Zustand, Shadcn/UI, Tailwind CSS, Framer Motion, BullMQ (003-phase1-foundation-mvp)
- PostgreSQL 16, Redis 7 (003-phase1-foundation-mvp)
- TypeScript 5+ (Playwright Scripts), C# (.NET 8 for Backdoor Endpoints) + `@playwright/test` (004-e2e-testing-all)
- Separate `nadergorge_e2e` database instance on PostgreSQL. (004-e2e-testing-all)
- TypeScript / React (Next.js 14+) + Tailwind CSS, next/image (006-papyrus-package-ui)
- N/A (Frontend cosmetic update) (006-papyrus-package-ui)
- C# (.NET 8.0/9.0), TypeScript, Node.js. + Entity Framework Core, React Query, Zustand, BullMQ/ioredis, Tailwind CSS. (007-phase2-academic-ops)
- PostgreSQL, Redis. (007-phase2-academic-ops)
- TypeScript 5, C# .NET 8 + Next.js App Router, Shadcn/UI Component Library, Lucide Icons, ASP.NET Identity Framework. (009-admin-academic-cms)
- Existing PostgreSQL with EF Core. (009-admin-academic-cms)
- TypeScript 5.x (strict mode) + Next.js 15 (App Router), React 19, Framer Motion (animations), Lucide React (icons), Tailwind CSS (styling) (010-admin-shared-components)
- N/A — frontend-only refactoring, no database changes (010-admin-shared-components)
- TypeScript (Next.js 15+ Frontend) / C# (.NET 8 Backend) + React Hook Form, Zod, Axios, MediatR, Entity Framework Core (012-student-auth-redesign)
- PostgreSQL (existing user & profile tables) (012-student-auth-redesign)
- TypeScript (Next.js 15) + YouTube IFrame API, `shadow-dom`, `MutationObserver` (013-video-url-protection)
- N/A (no backend changes) (013-video-url-protection)
- C# .NET 8 (backend), TypeScript 5.x / Next.js 14 (frontend) + Entity Framework Core, React Query, Framer Motion, Zustand (014-registration-codes-hierarchy)
- PostgreSQL (Supabase), Redis (014-registration-codes-hierarchy)
- TypeScript (Frontend), C# / .NET 8 (Backend) + Playwright (for Chrome/Safari/Firefox automation) (015-e2e-testing)
- N/A for tests (uses existing PostgreSQL via API setup) (015-e2e-testing)
- C# .NET 8 (Backend), TypeScript/Next.js (Frontend) + Entity Framework Core, MediatR, FluentValidation, Zod, Framer Motion (016-registration-form-updates)
- PostgreSQL (Docker-managed, NOT Supabase) (016-registration-form-updates)
- Next.js 14, React 18, TypeScript, C# 12, .NET 8 + Tailwind CSS, React Query, Entity Framework Core, Lucide Reac (017-package-profile-management)
- C# 12 / .NET 8, TypeScript 5.x + EF Core, MediatR, Next.js, React, Tailwind CSS (020-lesson-content-management)
- PostgreSQL (Existing Tables: `Lesson`, `LessonVideo`, `LessonResource`, `Homework`) (020-lesson-content-management)
- TypeScript / C# 12 (.NET 8) + Next.js + React Query + Tailwind / ASP.NET Core API + EF Core + MediatR (021-inline-lesson-exams)
- PostgreSQL via EF Core (021-inline-lesson-exams)
- C# (.NET 8), TypeScript (Next.js 14) + EF Core, React Query, Zustand (023-exam-dashboard-timers)
- C# (.NET 8.0) Backend, TypeScript (Next.js 14) Frontend + `react-quill`, Entity Framework Core (024-exam-editor-enhancements)
- Next.js 15 (React 19), TypeScrip + `framer-motion`, `@remix-run/react` (for routing/params if applicable), `lucide-react` (027-video-carousel-navigation)
- N/A (State is managed locally + URL params) (027-video-carousel-navigation)
- Next.js 15 (React 19), TypeScrip + `framer-motion`, `zustand` (028-lesson-focus-mode)
- N/A (Client-only state via Zustand memory, resets on navigation) (028-lesson-focus-mode)
- TypeScript / C# .NET 8 + Next.js App Router, React (framer-motion, lucide-react), Zustand, React Query (029-lesson-progression-stepper)
- PostgreSQL via Prisma (Backend) (029-lesson-progression-stepper)
- C# 12 (.NET 8.0) | TypeScript (React 18) + EF Core 8, Tailwind CSS, Framer Motion (030-exam-ui-refinements)
- PostgreSQL (AppDbContext) (030-exam-ui-refinements)
- C# 12 (.NET 8.0) & TypeScript (Next.js 14) + Entity Framework Core, React Hook Form, Framer Motion (031-unify-assessment-builder)
- PostgreSQL (Ef Core Migrations) (031-unify-assessment-builder)
- TypeScript (Next.js 14+), C# (.NET 8.0) + React, Tailwind CSS, Framer Motion, ASP.NET Core (032-assessment-ui-fixes)
- PostgreSQL (Entity Framework Core) (032-assessment-ui-fixes)
- TypeScript 5.x / Next.js 16.2.1 / React 19 + Next.js App Router, Axios, Zustand (112-surface-login-access-contract)
- N/A (Stateless cookie/Zustand validation) (112-surface-login-access-contract)

- Markdown (documentation-only phase — no application code) + N/A (no code dependencies) (001-phase0-discovery-blueprint)

## Project Structure

```text
src/
tests/
```

## Commands

# Add commands for Markdown (documentation-only phase — no application code)

## Code Style

Markdown (documentation-only phase — no application code): Follow standard conventions

## Recent Changes
- 112-surface-login-access-contract: Added TypeScript 5.x / Next.js 16.2.1 / React 19 + Next.js App Router, Axios, Zustand
- 056-extra-watch-request: Added [if applicable, e.g., PostgreSQL, CoreData, files or N/A]
- 032-assessment-ui-fixes: Added TypeScript (Next.js 14+), C# (.NET 8.0) + React, Tailwind CSS, Framer Motion, ASP.NET Core


<!-- MANUAL ADDITIONS START -->
<!-- MANUAL ADDITIONS END -->
