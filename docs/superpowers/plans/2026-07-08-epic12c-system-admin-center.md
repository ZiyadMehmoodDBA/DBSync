# Epic 12C — System Administration Center Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Operations Center — a unified administration experience that organizes the operational capabilities from Epics 11, 12A, 12B-1, and 12B-2 into five cohesive pillars: Overview (NOC dashboard), Jobs (sync_operation registry), Health (IWorkerStatusRegistry), Activity (Correlation timeline), and Administration (consolidated + Feature Flags + Settings + Retention).

**Architecture:** New `sync_operation` orchestration index (M024) alongside metadata extensions for `sync_parameter` (M025). Backend adds `IOperationService`, `IWorkerStatusRegistry`, `ISystemHealthContributor` pattern, `IOverviewQueryService`, and correlation timeline assembly to `AuditController`. Frontend restructures navigation (role-based landing, Operations Center shell, Administration shell) and adds 7 new pages.

**Tech Stack:** C# 13 / .NET 9, ASP.NET Core, EF Core, SignalR, xUnit, React 19, TanStack Query, React Router, Tailwind, shadcn/ui, Recharts (for tick history chart)

## Global Constraints

- Zero build warnings (`--warnaserror` is active on all projects)
- All tests must pass before moving to the next task
- `sync_operation` must never store domain-specific columns (ownership invariant)
- Every worker must call `IWorkerStatusRegistry.Register()` in `StartAsync`
- `POST /api/v1/system/workers/{name}/diagnostics` is read-only — no writes, no retries
- `/api/v1/system/health` is UI-facing rich JSON; `/health/ready` is infrastructure probe
- `OverviewSnapshotCache` TTL = 5 seconds; invalidate on four specific SignalR events
- Migration M024 = sync_operation only; M025 = sync_parameter metadata + seeds
- Never commit `.env` files; never use `git add .` or `git add -A`

---

## Task Index

| # | Task | Plan File |
|---|------|-----------|
| 1 | M024 Migration — sync_operation table + correlation indexes | [task-1](2026-07-08-epic12c-task-1-m024-migration.md) |
| 2 | M025 Migration — SyncParameter metadata columns + seeds | [task-2](2026-07-08-epic12c-task-2-m025-migration.md) |
| 3 | IOperationService + IOperationHandler registry | [task-3](2026-07-08-epic12c-task-3-operation-service.md) |
| 4 | OperationsController + OperationDto | [task-4](2026-07-08-epic12c-task-4-operations-controller.md) |
| 5 | Domain integration — Export + Rollout + Decommission → IOperationService | [task-5](2026-07-08-epic12c-task-5-domain-integration.md) |
| 6 | IWorkerStatusRegistry + WorkerStatusDto | [task-6](2026-07-08-epic12c-task-6-worker-registry.md) |
| 7 | Worker integration — all 6 workers register + report ticks | [task-7](2026-07-08-epic12c-task-7-worker-integration.md) |
| 8 | ISystemHealthContributor pattern + health endpoints + Program.cs wiring | [task-8](2026-07-08-epic12c-task-8-health-backend.md) |
| 9 | IOverviewQueryService + OverviewSnapshotCache + SystemController overview + info | [task-9](2026-07-08-epic12c-task-9-overview-backend.md) |
| 10 | CorrelationTimelineAssembler + AuditController correlation endpoints | [task-10](2026-07-08-epic12c-task-10-correlation-backend.md) |
| 11 | SyncParameter category filter on ParametersController + PARAMETER_UPDATED audit | [task-11](2026-07-08-epic12c-task-11-parameter-admin.md) |
| 12 | Frontend navigation restructure — routes, sidebar, role-based redirect, redirects | [task-12](2026-07-08-epic12c-task-12-frontend-nav.md) |
| 13 | Overview page frontend | [task-13](2026-07-08-epic12c-task-13-overview-frontend.md) |
| 14 | Jobs page frontend + SignalR OperationChanged | [task-14](2026-07-08-epic12c-task-14-jobs-frontend.md) |
| 15 | Health page frontend — WorkerCard + tick history chart | [task-15](2026-07-08-epic12c-task-15-health-frontend.md) |
| 16 | Activity Correlation tab + CorrelationTimeline component | [task-16](2026-07-08-epic12c-task-16-correlation-frontend.md) |
| 17 | Administration pages — Feature Flags, Settings, Retention, License, Diagnostics | [task-17](2026-07-08-epic12c-task-17-admin-frontend.md) |
| 18 | Integration tests — Overview, Operations, Workers, Correlation, Admin, Nav, Perf | [task-18](2026-07-08-epic12c-task-18-integration-tests.md) |

---

## File Map

### New backend files

```
src/MSOSync.Persistence/
  Migrations/
    M024_OperationsFoundation.cs         — sync_operation + correlation indexes
    M025_ParameterMetadata.cs            — sync_parameter columns + seeds
  Entities/
    SyncOperation.cs                     — operation entity
  Configurations/
    SyncOperationConfiguration.cs        — EF Core table mapping

src/MSOSync.Metadata/
  Operations/
    IOperationService.cs                 — write interface
    OperationService.cs                  — implementation
    IOperationHandler.cs                 — per-type cancel/retry interface
    OperationDto.cs                      — query result DTO
    IOperationQueryService.cs            — read interface
    OperationQueryService.cs             — implementation
    Handlers/
      ExportOperationHandler.cs
      RolloutOperationHandler.cs
      DecommissionOperationHandler.cs
  Overview/
    IOverviewQueryService.cs
    OverviewQueryService.cs
    OverviewSnapshotCache.cs
    OverviewDto.cs                       — widget-based DTO (nested records)
  Audit/
    CorrelationTimelineAssembler.cs      — assembles timeline from multiple sources
    CorrelationTimelineDto.cs            — all correlation DTOs

src/MSOSync.App/
  Workers/
    IWorkerStatusRegistry.cs
    WorkerStatusRegistry.cs              — ConcurrentDictionary, thread-safe
    WorkerStatusDto.cs                   — incl. TickRecord
  Health/
    ISystemHealthContributor.cs
    WorkerHealthContributor.cs
    DatabaseHealthContributor.cs
    SignalRHealthContributor.cs
    ApiHealthContributor.cs
    SystemHealthService.cs
    WorkerHealthCheck.cs                 — IHealthCheck for /health/ready
  SignalR/
    OperationChangedPublisher.cs         — MediatR handler → SignalR broadcast
    WorkerStatusChangedPublisher.cs      — MediatR handler → SignalR broadcast

src/MSOSync.Api/Controllers/
  OperationsController.cs                — GET list/detail, POST cancel/retry
  SystemController.cs                   — GET overview, workers, health, info
```

### Modified backend files

```
src/MSOSync.Persistence/AppDbContext.cs              — add DbSet<SyncOperation>
src/MSOSync.Persistence/Entities/SyncParameter.cs   — add metadata columns
src/MSOSync.Metadata/MetadataServiceExtensions.cs   — register new services
src/MSOSync.Metadata/Configuration/RolloutService.cs — add IOperationService
src/MSOSync.App/Export/ExportJobService.cs          — add IOperationService
src/MSOSync.Metadata/Lifecycle/NodeLifecycleService.cs — add IOperationService (decommission)
src/MSOSync.Api/Controllers/AuditController.cs      — add 3 correlation endpoints
src/MSOSync.Api/Controllers/ParametersController.cs — add ?category= filter
src/MSOSync.App/Workers/ExportJobWorker.cs          — register + tick reporting
src/MSOSync.Scheduler/Workers/HeartbeatWorker.cs    — register + tick reporting
src/MSOSync.Scheduler/Workers/ProbeWorker.cs        — register + tick reporting
src/MSOSync.Scheduler/Workers/DecommissionWorker.cs — register + tick reporting
src/MSOSync.App/SignalR/OperationsEventType.cs      — add OperationChanged, WorkerStatusChanged, OverviewRefreshed
src/MSOSync.App/Program.cs                          — AddHealthChecks, register SystemHealthService
```

### New frontend files

```
src/MSOSync.Frontend/src/
  features/
    overview/
      OverviewPage.tsx
      components/
        OverviewHealthBar.tsx            — Zone A: status bar
        OverviewActionCards.tsx          — Zone B: action required
        OverviewQuickActions.tsx         — Zone C: quick action strip
        OverviewActivityFeed.tsx         — Zone D: recent activity
        OverviewSystemInfo.tsx           — Zone E: system info strip
    operations/
      jobs/
        JobsPage.tsx
        components/
          OperationStatusBadge.tsx       — status + result chip
          OperationProgressCell.tsx      — progress bar + message
      health/
        HealthPage.tsx
        components/
          WorkerCard.tsx                 — card + expandable tick history
          WorkerTickChart.tsx            — Recharts bar chart of last 100 ticks
          SystemHealthPanel.tsx          — DB + SignalR panels
    administration/
      feature-flags/
        FeatureFlagsPage.tsx
      settings/
        SettingsPage.tsx                 — replaces ParametersPage (renamed)
      retention/
        RetentionPage.tsx
      license/
        LicensePage.tsx
      diagnostics/
        DiagnosticsPage.tsx
  shared/
    api/
      operations.ts                     — fetch operations list, detail, cancel, retry
      system.ts                          — fetch overview, workers, health, info
    hooks/
      useOverview.ts
      useOperations.ts
      useWorkers.ts
      useCorrelationTimeline.ts
    components/
      CorrelationTimeline.tsx            — phase-grouped timeline with collapse
      CorrelationSummaryCard.tsx         — top card with entity chips
```

### Modified frontend files

```
src/MSOSync.Frontend/src/
  app/
    router.tsx                           — new routes + redirects
    layouts/AppLayout.tsx                — nav restructure, role-based landing
  features/
    audit/AuditPage.tsx                  — add Correlation tab
    dashboard/DashboardPage.tsx          — simplify to Summary view
  shared/
    signalr/eventRouter.ts               — handle OperationChanged, WorkerStatusChanged
    api/audit.ts                         — add correlation API functions
```

---

## Sequence notes

- Tasks 1–2 (migrations) must come before Tasks 3–11 (backend that uses the new tables)
- Tasks 3–5 (operations) must come before Task 14 (Jobs frontend needs API)
- Tasks 6–8 (workers/health) must come before Task 15 (Health frontend)
- Task 9 (overview backend) must come before Task 13 (Overview frontend)
- Task 10 (correlation backend) must come before Task 16 (Correlation frontend)
- Task 11 (parameters) can run parallel to Tasks 3–10
- Task 12 (nav restructure) can run parallel to Tasks 3–11
- Tasks 13–17 (all frontend) must come after their respective backend tasks
- Task 18 (integration tests) must come last
