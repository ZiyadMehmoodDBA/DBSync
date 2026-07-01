# Epic 11C: SignalR Observability — Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add real-time push notifications to the MSOSync operator console using SignalR — live cache invalidation on node health and lifecycle events, toast notifications with deduplication, and a resilient hybrid model where SignalR is the primary freshness mechanism with 5-minute polling as a fallback.

**Architecture:** SignalR is a cache invalidation bus — REST APIs remain the source of truth. No data is transported over SignalR; only event signals trigger targeted React Query refetches. Reconnect triggers a full `invalidateQueries()` sweep to catch missed events.

**Tech Stack:** `Microsoft.AspNetCore.SignalR` (built-in .NET 9), `@microsoft/signalr` ^8.x, MediatR 12.4.1 (existing), Sonner (existing), React Query 5 (existing).

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`
- TypeScript strict, no `any`
- Relative imports in all `shared/signalr/` code — no `@/` aliases
- Hub requires `[Authorize(Policy = "ViewerOrAbove")]`
- JWT accepted via query string (`access_token` param) for `/hubs/*` paths ONLY — never globally for REST
- `Clients.Group("operators")` — never `Clients.All`
- `accessTokenFactory` on frontend — token never hard-captured in URL
- Single hub message channel: `"OperationsEvent"` — never per-event-type method names
- `OperationsEventType` enum serialized as strings via `JsonStringEnumConverter`
- Reconnect delays: `[0, 2_000, 5_000, 10_000, 30_000]` — never framework defaults
- Deduplication window: 30s bucket keyed by `${type}:${nodeId}:${currentStatus}:${bucket}`
- `routeToCache` returns `Promise<void>` to preserve future ordering semantics
- On reconnect: `queryClient.invalidateQueries()` with NO filter (full sweep)

---

## File Map

| File | Task | Action |
|---|---|---|
| `src/MSOSync.Scheduler/NodeConnectivityChangedEvent.cs` | 1 | Create |
| `src/MSOSync.Scheduler/Workers/ProbeWorker.cs` | 1 | Modify (inject IPublisher, publish on status change) |
| `src/MSOSync.App/Hubs/OperationsHub.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/OperationsEventType.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/OperationsEvent.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/NodeOperationsPublisher.cs` | 1 | Create |
| `src/MSOSync.App/SignalR/SyncOperationsPublisher.cs` | 1 | Create |
| `src/MSOSync.App/Program.cs` | 1 | Modify (AddSignalR, AddMediatR for App assembly, MapHub, JWT OnMessageReceived wiring) |
| `src/MSOSync.Security/SecurityServiceExtensions.cs` | 1 | Modify (add OnMessageReceived to JwtBearerEvents) |
| `tests/MSOSync.AppTests/MSOSync.AppTests.csproj` | 1 | Create |
| `tests/MSOSync.AppTests/SignalR/NodeOperationsPublisherTests.cs` | 1 | Create |
| `tests/MSOSync.AppTests/SignalR/SyncOperationsPublisherTests.cs` | 1 | Create |
| `src/MSOSync.Frontend/package.json` | 2 | Modify (add @microsoft/signalr) |
| `src/MSOSync.Frontend/src/shared/signalr/types.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/context.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx` | 2 | Create |
| `src/MSOSync.Frontend/src/app/providers.tsx` | 2 | Modify (insert SignalRProvider) |
| `src/MSOSync.Frontend/src/shared/signalr/types.test.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/useSignalR.test.ts` | 2 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/notifications.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/eventRouter.test.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/shared/signalr/notifications.test.ts` | 3 | Create |
| `src/MSOSync.Frontend/src/features/dashboard/hooks.ts` | 3 | Modify (refetchInterval 30s→300s, staleTime 60s) |
| `src/MSOSync.Frontend/src/features/metrics/hooks.ts` | 3 | Modify (refetchInterval 30s→300s, staleTime 60s for live hooks) |
| `src/MSOSync.Frontend/src/features/events/hooks.ts` | 3 | Modify (refetchOnWindowFocus: true) |
| `src/MSOSync.Frontend/src/features/incoming-batches/hooks.ts` | 3 | Modify (refetchOnWindowFocus: true) |
| `src/MSOSync.Frontend/src/features/outgoing-batches/hooks.ts` | 3 | Modify (refetchOnWindowFocus: true) |
| `src/MSOSync.Frontend/src/features/batch-errors/hooks.ts` | 3 | Modify (refetchOnWindowFocus: true) |
| `src/MSOSync.Frontend/src/features/users/hooks.ts` | 3 | Modify (staleTime: Infinity) |
| `src/MSOSync.Frontend/src/features/channels/hooks.ts` | 3 | Modify (staleTime: Infinity) |
| `src/MSOSync.Frontend/src/features/triggers/hooks.ts` | 3 | Modify (staleTime: Infinity) |
| `src/MSOSync.Frontend/src/features/routers/hooks.ts` | 3 | Modify (staleTime: Infinity) |
| `src/MSOSync.Frontend/src/features/parameters/hooks.ts` | 3 | Modify (staleTime: Infinity) |
| `src/MSOSync.Frontend/src/shared/constants/query.ts` | 3 | Modify (DASHBOARD_REFRESH_MS 30s→300s) |
| `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` | 4 | Modify (add SignalRIndicator in topbar) |

---

## Tasks

### Task 1: Backend Hub + Publishers + JWT + Tests
**Brief:** `docs/superpowers/plans/2026-07-01-epic11c-task-1-backend-hub-publishers.md`

Creates `NodeConnectivityChangedEvent`, updates `ProbeWorker` to publish it, creates `OperationsHub`, `OperationsEvent`, `OperationsEventType`, `NodeOperationsPublisher`, `SyncOperationsPublisher`, adds JWT `OnMessageReceived` for `/hubs/*`, wires SignalR DI + hub mapping in Program.cs, creates `MSOSync.AppTests` project with unit tests.

**Deliverable:** `dotnet build --warnaserror` clean; unit tests in `MSOSync.AppTests` pass.

### Task 2: Frontend Connection Layer + Provider + Types
**Brief:** `docs/superpowers/plans/2026-07-01-epic11c-task-2-frontend-connection-layer.md`

Installs `@microsoft/signalr`, creates `types.ts`, `context.ts`, `useSignalR.ts`, `SignalRProvider.tsx`, inserts provider into `providers.tsx`, writes `types.test.ts` (RECONNECT_DELAYS contract) and `useSignalR.test.ts` (reconnect recovery test).

**Deliverable:** `npx tsc -b --noEmit` clean; Vitest suite passes.

### Task 3: Event Router + Notifications + Polling Policy
**Brief:** `docs/superpowers/plans/2026-07-01-epic11c-task-3-event-router-notifications.md`

Creates `eventRouter.ts` (routeToCache, three invalidation groups), `notifications.ts` (routeToToast with dedup), updates `SignalRProvider.tsx` to call both on each event, adjusts polling policies across all hooks, writes `eventRouter.test.ts` and `notifications.test.ts`.

**Deliverable:** `npm run test` all pass; `npm run build` exit 0.

### Task 4: UI Integration + Connection Indicator + Manual Acceptance
**Brief:** `docs/superpowers/plans/2026-07-01-epic11c-task-4-ui-integration.md`

Adds `SignalRIndicator` to `AppLayout.tsx` topbar (hidden when connected, amber when reconnecting, red when offline). Starts dev server, walks through manual acceptance checklist.

**Deliverable:** All manual acceptance items verified in browser; final commit.
