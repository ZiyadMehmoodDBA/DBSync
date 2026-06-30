# Epic 10B: AG Grid Data Tables — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire all 15 placeholder pages to live .NET read APIs using AG Grid tables, summary cards, and activity feeds — no mutations.

**Architecture:** Shared API layer (`shared/api/*.ts`) calls authenticated Axios `client`; DTO types in `shared/types/`; centralized query key factory in `shared/queryKeys.ts`; feature-local React Query hooks in `features/*/hooks.ts`; AG Grid for all tabular data with two patterns: server-side (app owns page/pageSize) and client-side (AG Grid owns pagination).

**Tech Stack:** React 19, TypeScript strict, TanStack Query 5.101, AG Grid 35.3 community (already installed), Tailwind 4, shadcn/ui, Lucide Icons, Vitest 4 (12 existing tests must stay green).

## Global Constraints

- TypeScript strict mode — no `any` types
- AG Grid community edition only — no enterprise features
- `PagedResult<T>` response → ServerGrid (app owns page/pageSize state, `placeholderData: prev => prev`)
- `IReadOnlyList<T>` / array response → DataGrid (AG Grid owns pagination/sort/filter)
- `DASHBOARD_REFRESH_MS = 30_000` — only Dashboard and Metrics auto-refresh; all others user-driven
- `refetchIntervalInBackground: false` on all polling queries
- `refetchOnWindowFocus: false` on all non-polling queries
- `refetchOnWindowFocus: true` on Dashboard and Metrics polling queries
- `placeholderData: (prev) => prev` on all server-side paginated queries (TanStack Query v5 equivalent of keepPreviousData)
- DTO types mirror backend contracts exactly — no UI-only fields
- Format functions live in `shared/utils/` — never inline in column defs
- Query keys come exclusively from `shared/queryKeys.ts`
- All new files under `src/MSOSync.Frontend/src/`
- `npm run build && npm run lint && npm test` must pass after every task
- Spec at: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

---

## Task Summary

| # | Name | Deliverable |
|---|------|-------------|
| 1 | Shared types + utils + API | TypeScript-only shared layer: types, constants, queryKeys, utils, API functions |
| 2 | Shared React components | DataGrid, ServerGrid, SummaryCard, StatusBadge, ErrorState, EmptyState + AG Grid CSS |
| 3 | Dashboard + Locks + Router | Live dashboard, Locks page, router/sidebar update |
| 4 | Events + Batches | Events, IncomingBatches, OutgoingBatches, BatchErrors server-side pages |
| 5 | Nodes + Config pages | Nodes, Channels, Triggers, Routers client-side pages |
| 6 | Topology + Metrics | Topology summary+groups, Metrics polling pages |
| 7 | Admin + Account | Users, Parameters, Audit, Profile pages |

Brief files:
- `docs/superpowers/plans/2026-06-30-epic10b-task-1-shared-types-api.md`
- `docs/superpowers/plans/2026-06-30-epic10b-task-2-shared-components.md`
- `docs/superpowers/plans/2026-06-30-epic10b-task-3-dashboard-locks.md`
- `docs/superpowers/plans/2026-06-30-epic10b-task-4-events-batches.md`
- `docs/superpowers/plans/2026-06-30-epic10b-task-5-nodes-config.md`
- `docs/superpowers/plans/2026-06-30-epic10b-task-6-topology-metrics.md`
- `docs/superpowers/plans/2026-06-30-epic10b-task-7-admin-account.md`

---

## Existing Codebase Context

**Frontend root:** `src/MSOSync.Frontend/`

**Already installed packages (no npm install needed):**
- `ag-grid-community@^35.3.1`, `ag-grid-react@^35.3.1`
- `@tanstack/react-query@^5.101.0`
- `axios@^1.18.0`
- `lucide-react@^1.22.0`

**Existing shared API files:**
- `src/MSOSync.Frontend/src/shared/api/client.ts` — Axios instance with base URL `/api/v1`, auth interceptor, single-flight 401 refresh. Import as `import client from './client'` in new api files.
- `src/MSOSync.Frontend/src/shared/api/auth.ts` — auth-specific calls (do not modify)

**Existing shared types:**
- `src/MSOSync.Frontend/src/shared/types/auth.ts` — `UserProfile`, `AuthState`, `LoginResponse` (do not modify)

**Existing placeholder pages** (to be replaced task-by-task):
- `features/dashboard/DashboardPage.tsx`, `features/events/EventsPage.tsx`
- `features/batches/IncomingBatchesPage.tsx`, `features/batches/OutgoingBatchesPage.tsx`, `features/batches/BatchErrorsPage.tsx`
- `features/metrics/MetricsPage.tsx`, `features/topology/TopologyPage.tsx`
- `features/nodes/NodesPage.tsx`, `features/channels/ChannelsPage.tsx`
- `features/triggers/TriggersPage.tsx`, `features/routers/RoutersPage.tsx`
- `features/users/UsersPage.tsx`, `features/parameters/ParametersPage.tsx`
- `features/audit/AuditPage.tsx`, `features/profile/ProfilePage.tsx`

**Router:** `src/MSOSync.Frontend/src/app/router.tsx` — already has routes for all pages. Task 3 adds `/locks` route.

**Sidebar:** `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — Task 3 adds Locks nav item.

**QueryClient:** `src/MSOSync.Frontend/src/app/providers.tsx` — `QueryClient` already wraps app with `defaultOptions: { queries: { retry: 1, staleTime: 30_000 } }`.

**Existing tests:** `src/MSOSync.Frontend/src/shared/api/__tests__/client.test.ts` + 2 auth test files = 12 tests total. Must stay green.

---

## .NET API Endpoints Reference

All endpoints require Bearer token (handled by `client.ts` interceptor):

| Method | Path | Response Type |
|--------|------|---------------|
| GET | /dashboard/summary | `DashboardSummaryDto` |
| GET | /dashboard/activity | `PagedResult<ActivityItemDto>` |
| GET | /events | `PagedResult<EventSummaryDto>` |
| GET | /incoming-batches | `PagedResult<IncomingBatchSummaryDto>` |
| GET | /batches | `PagedResult<OutgoingBatchDto>` |
| GET | /batch-errors | `PagedResult<BatchErrorDetailDto>` |
| GET | /nodes | `NodeDto[]` |
| GET | /topology/summary | `TopologySummaryDto` |
| GET | /topology/groups | `TopologyGroupDto[]` |
| GET | /topology/groups/{id}/nodes | `TopologyGroupNodeDto[]` |
| GET | /metrics/summary | `MetricsSummaryDto` |
| GET | /metrics/nodes | `NodeMetricsDto[]` |
| GET | /metrics/channels | `ChannelMetricsDto[]` |
| GET | /metrics/runtime | `RuntimeMetricsDto` |
| GET | /channels | `ChannelDto[]` |
| GET | /triggers | `TriggerDto[]` |
| GET | /routers | `RouterDto[]` |
| GET | /users | `PagedResult<UserSummaryDto>` |
| GET | /parameters | `ParameterDto[]` |
| GET | /parameters/descriptors | `ParameterDescriptorDto[]` |
| GET | /audit | `PagedResult<AuditDto>` |
| GET | /locks | `LockDto[]` |
