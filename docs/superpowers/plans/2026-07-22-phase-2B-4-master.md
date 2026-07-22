# Phase 2B.4 — Cluster Health, Recovery Dashboard, Diagnostics Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Three operator-facing analytics modules extending `ClusterController` — Cluster Health Trends, Disaster Recovery Dashboard, and Cluster Diagnostics — with no new DB migrations.

**Architecture:** All three modules add endpoints to the existing `ClusterController` (Primary constructor extended to inject 3 new services alongside `IClusterSummaryQueryService`). `ClusterHealthTrendService` aggregates `SyncNodeConnectivityHistory` rows into time-bucketed connectivity trends. `RecoveryDashboardQueryService` correlates `SyncNodeLifecycleHistory` + `SyncNodeConnectivityHistory` + `SyncOperation` for recovery RTO tracking. `ClusterDiagnosticsQueryService` queries `SyncRuntimeStats`, `SyncLock`, and `SyncOperation` for runtime diagnostics. No new EF migrations — all services use existing entities.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / FluentValidation / xUnit + FluentAssertions / React 19 + TypeScript / Recharts / TanStack Query v5 / lucide-react

Spec: `docs/superpowers/specs/2026-07-22-phase-2B-4-design.md`

## Global Constraints

- All Phase 2A rules (`.github/PULL_REQUEST_TEMPLATE.md`): named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3.
- RULE-CTL-2: no controller injects `AppDbContext` directly.
- No new EF migrations (M034 was last).
- All work commits directly to `main`.
- All new query methods: `AsNoTracking()`, no lazy loading, no `Include()` unless required.
- All timestamps UTC internally; frontend converts for display only.
- `MSOSync.Metadata` must not reference `MSOSync.Batch` or `MSOSync.Routing`.
- No `Task.WhenAll` on queries sharing the same `AppDbContext` instance (EF DbContext not thread-safe).
- `SyncRuntimeStats` may be empty — all diagnostic sub-lists return empty list gracefully (never 500).
- Relay operations use `OperationType == "BatchReplay"` (not "Replay") — verified in `ClusterSummaryQueryService.cs`.
- `SyncNode.LifecycleState` is `NodeLifecycleState` enum (not string) — use `NodeLifecycleState.Recovery` etc.
- `SyncNodeLifecycleHistory.ToState`/`FromState` are `NodeLifecycleState` / `NodeLifecycleState?` enum types.

---

## Execution Order

| # | Status | Task file | Deliverable |
|---|---|---|---|
| 1 | ⬜ | [Task 1 — Cluster Health Trends](2026-07-22-phase-2B-4-task-1-health-trends.md) | `ClusterHealthTrendService`, health-trends endpoint, `HealthTrendsPage.tsx` |
| 2 | ⬜ | [Task 2 — Recovery Dashboard](2026-07-22-phase-2B-4-task-2-recovery.md) | `RecoveryDashboardQueryService`, recovery endpoint, `RecoveryDashboardPage.tsx` |
| 3 | ⬜ | [Task 3 — Cluster Diagnostics](2026-07-22-phase-2B-4-task-3-diagnostics.md) | `ClusterDiagnosticsQueryService`, diagnostics endpoint, `ClusterDiagnosticsPage.tsx` |
| 4 | ⬜ | [Task 4 — Integration tests + docs](2026-07-22-phase-2B-4-task-4-integration-docs.md) | Integration tests for all 3 modules, docs updates |

Tasks 1–3 are independent and can be executed in any order. Task 4 must be last.

## Completion Criteria

1. All 4 task files complete, committed to `main`.
2. `dotnet test D:\MSOSync\MSOSync.sln` — all unit assemblies green; only accepted environmental integration failures.
3. `npm run build` in `src/MSOSync.Frontend` — 0 TypeScript errors.
4. `docs/architecture/service-responsibility-map.md` updated with Phase 2B.4 section.
5. `docs/architecture/test-infrastructure.md` updated with new test counts.
