# Epic 10A: React Dashboard Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Vite scaffold placeholder in `src/MSOSync.Frontend/` with a production-ready authenticated shell — working login/refresh/logout, 15 placeholder routes, AppLayout with sidebar and topbar, light/dark theme with no flash, and Vite dev proxy wired to the .NET API.

**Architecture:** React Router v7 nested layout routes. `RootInitializer` bootstraps the session (attempts token refresh from localStorage on mount). `AuthGuard` outlet redirects unauthenticated users to `/login`. `AppLayout` renders sidebar + topbar via `<Outlet />`. Axios 401 interceptor uses a single-flight refresh lock. Production build embeds in `MSOSync.App/wwwroot` served by the .NET API.

**Tech Stack:** React 19, TypeScript, Vite 8, react-router-dom 7, TanStack Query 5, Axios, shadcn/ui (CLI-generated), Tailwind CSS 4, Lucide Icons, Zod, React Hook Form, Vitest + Testing Library.

## Global Constraints

- TypeScript strict mode — no `any` types; `noUnusedLocals` and `noUnusedParameters` enforced
- Tailwind 4 via `@tailwindcss/vite` Vite plugin — NOT the PostCSS plugin
- shadcn/ui via CLI only — `npx shadcn@latest add <component>`; do NOT add `shadcn` as a package.json dependency
- `react-router-dom` v7 — do NOT install `@tanstack/react-router`
- Access tokens NEVER written to localStorage or sessionStorage — memory (React Context) only
- localStorage keys (exact): `msosync.refresh_token`, `msosync.user`, `msosync.theme`
- All API calls go through `src/shared/api/client.ts` (the shared Axios instance)
- No Zustand — React Context + TanStack Query only
- Vite dev server port: `5173`; API proxy target: `http://localhost:5000`
- Theme: `html.dark` class set before React mounts in `main.tsx` to prevent flash of light theme (mandatory acceptance criterion)
- `RootInitializer` owns initialization state; `AuthGuard` owns authentication gate — do NOT merge these
- Axios `_retry` flag requires module augmentation (see Task 2 brief); never use `as any`

---

## Task Index

| Task | File | Scope |
|------|------|-------|
| 1 | [task-1](2026-06-29-epic10a-task-1-foundation.md) | Dependency migration + Tailwind + shadcn init + Vitest setup |
| 2 | [task-2](2026-06-29-epic10a-task-2-auth.md) | Auth infrastructure: types, API layer, Axios interceptor, AuthProvider, AuthGuard, RootInitializer, 3 test suites |
| 3 | [task-3](2026-06-29-epic10a-task-3-shell.md) | Router + layouts + AppLayout + theme system + 15 placeholder pages |
| 4 | [task-4](2026-06-29-epic10a-task-4-production.md) | Production: .NET static file serving + MSBuild publish target + full validation |

---

## File Map

### New files (Task 1)
- `src/MSOSync.Frontend/vitest.config.ts`
- `src/MSOSync.Frontend/src/test-setup.ts`
- `src/MSOSync.Frontend/src/components/ui/button.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/components/ui/card.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/components/ui/input.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/components/ui/label.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/components/ui/separator.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/components/ui/avatar.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/components/ui/badge.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/components/ui/skeleton.tsx` (shadcn generated)
- `src/MSOSync.Frontend/src/lib/utils.ts` (shadcn generated)
- `src/MSOSync.Frontend/components.json` (shadcn config)

### Modified files (Task 1)
- `src/MSOSync.Frontend/package.json` — remove MUI/emotion/zustand/recharts/react-force-graph; add Tailwind/Lucide/Zod/etc.
- `src/MSOSync.Frontend/vite.config.ts` — add Tailwind plugin + proxy
- `src/MSOSync.Frontend/src/index.css` — replace with Tailwind @import + dark variant
- `src/MSOSync.Frontend/src/main.tsx` — add theme init before createRoot
- `src/MSOSync.Frontend/src/App.tsx` — replace scaffold with router outlet

### New files (Task 2)
- `src/MSOSync.Frontend/src/shared/types/auth.ts`
- `src/MSOSync.Frontend/src/shared/api/auth.ts`
- `src/MSOSync.Frontend/src/shared/api/client.ts`
- `src/MSOSync.Frontend/src/shared/components/FullscreenLoader.tsx`
- `src/MSOSync.Frontend/src/features/auth/AuthProvider.tsx`
- `src/MSOSync.Frontend/src/features/auth/useAuth.ts`
- `src/MSOSync.Frontend/src/features/auth/AuthGuard.tsx`
- `src/MSOSync.Frontend/src/features/auth/RootInitializer.tsx`
- `src/MSOSync.Frontend/src/features/auth/LoginPage.tsx`
- `src/MSOSync.Frontend/src/features/auth/__tests__/AuthProvider.test.tsx`
- `src/MSOSync.Frontend/src/features/auth/__tests__/AuthGuard.test.tsx`
- `src/MSOSync.Frontend/src/shared/api/__tests__/client.test.ts`

### New files (Task 3)
- `src/MSOSync.Frontend/src/app/router.tsx`
- `src/MSOSync.Frontend/src/app/providers.tsx`
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`
- `src/MSOSync.Frontend/src/app/layouts/AuthLayout.tsx`
- `src/MSOSync.Frontend/src/features/dashboard/DashboardPage.tsx`
- `src/MSOSync.Frontend/src/features/topology/TopologyPage.tsx`
- `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`
- `src/MSOSync.Frontend/src/features/channels/ChannelsPage.tsx`
- `src/MSOSync.Frontend/src/features/triggers/TriggersPage.tsx`
- `src/MSOSync.Frontend/src/features/routers/RoutersPage.tsx`
- `src/MSOSync.Frontend/src/features/events/EventsPage.tsx`
- `src/MSOSync.Frontend/src/features/batches/IncomingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/batches/OutgoingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/batches/BatchErrorsPage.tsx`
- `src/MSOSync.Frontend/src/features/metrics/MetricsPage.tsx`
- `src/MSOSync.Frontend/src/features/users/UsersPage.tsx`
- `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx`
- `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`
- `src/MSOSync.Frontend/src/features/profile/ProfilePage.tsx`

### Modified files (Task 3)
- `src/MSOSync.Frontend/src/main.tsx` — wire RouterProvider + Providers

### Modified files (Task 4)
- `src/MSOSync.App/Program.cs` — add UseDefaultFiles + UseStaticFiles + MapFallbackToFile
- `src/MSOSync.App/MSOSync.App.csproj` — add PublishFrontend MSBuild target

---

## Acceptance Criteria (all mandatory)

1. `npm run dev` starts on `:5173`, proxies `/api` to `:5000`
2. `npm run build` succeeds with TypeScript strict mode, 0 errors
3. Login at `/login` authenticates against the live .NET API
4. After login, all 15 routes are reachable via the sidebar
5. Browser refresh restores the session via refresh token in localStorage
6. Five concurrent 401 responses trigger exactly one refresh request
7. Theme preference survives hard reload with NO flash of wrong theme (mandatory)
8. Direct navigation to `/topology` works under `MapFallbackToFile` (SPA fallback)
9. All 3 Vitest test suites pass: AuthProvider, AuthGuard, Axios interceptor
10. `npm run lint` reports 0 errors
11. `dotnet publish` includes `wwwroot/` assets from the built frontend
