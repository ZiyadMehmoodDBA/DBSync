# Phase 2B.2 — Batch Replay Engine Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Durable, cancellable batch replay operations covering FailedDelivery (re-queue Error batches) and MissedData (re-create batches for events a node missed while offline/draining).

**Architecture:** Two new tables (`sync_replay_request`, `sync_replay_item`) + M034 migration. `IReplayOperationService` (in `MSOSync.Metadata`) handles create/cancel; all advance logic lives inline in `ReplayWorker.RunTickAsync` (in `MSOSync.Scheduler`) which can reach `IBatchCreator` and `IRoutingService` — no separate `IReplayWorkerService` abstraction needed. `ReplayController` at `/api/v1/operations/replay`. React `ReplayWizard` + `ReplayDetailPanel` on the Jobs page.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / FluentValidation / MediatR / xUnit + FluentAssertions + Moq / React 19 + TypeScript

Spec: `docs/superpowers/specs/2026-07-21-phase-2B-2-replay-design.md`

## Global Constraints

- All Phase 2A rules apply (`.github/PULL_REQUEST_TEMPLATE.md`): named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3.
- RULE-CTL-2: `ReplayController` must not inject `AppDbContext` directly.
- Worker interval from `IOptions<ReplayOptions>` — no hardcoded values.
- Migration numbering: **M034** (`src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs`).
- `ReplayWorker` goes in `src/MSOSync.Scheduler/Workers/` (matches `RollingOperationWorker` — spec says `MSOSync.App` but actual codebase pattern is Scheduler).
- `ReplayWorker` registered in `SyncSchedulerExtensions.cs` (no `AppServiceExtensions.cs` exists).
- Worker tests in `MSOSync.SchedulerTests` (has `InternalsVisibleTo` from Scheduler csproj).
- All work commits directly to `main`.

---

## Execution Order

| # | Status | Task file | Deliverable |
|---|---|---|---|
| 1 | ✅ | [Task 1 — Persistence + M034](2026-07-22-phase-2B-2-task-1-persistence.md) | `SyncReplayRequest`/`SyncReplayItem` entities + configs + DbSets, M034 migration, enum additions |
| 2 | ✅ | [Task 2 — Metadata services](2026-07-22-phase-2B-2-task-2-services.md) | `ReplayOptions`, `IReplayOperationService` + impl, `IReplayOperationQueryService` + impl, register in extensions |
| 3 | ✅ | [Task 3 — Worker](2026-07-22-phase-2B-2-task-3-worker.md) | `ReplayWorker` (FailedDelivery + MissedData advance), SchedulerTests |
| 4 | ✅ | [Task 4 — API controller](2026-07-22-phase-2B-2-task-4-controller.md) | `ReplayController`, DTOs, validator, AppTests |
| 5 | ✅ | [Task 5 — Frontend](2026-07-22-phase-2B-2-task-5-frontend.md) | `ReplayWizard`, `ReplayDetailPanel`, types/api/hooks, JobsPage wiring |
| 6 | ✅ | [Task 6 — Integration + docs](2026-07-22-phase-2B-2-task-6-integration-docs.md) | Integration tests (API + M034 migration), docs updates, final gate |

Tasks 1→3 are sequential. Task 4 after 2. Task 5 after 4. Task 6 last.

## Completion Criteria

1. All 6 task files complete, committed to `main`.
2. `dotnet test D:\MSOSync\MSOSync.sln` — all unit assemblies green; only accepted environmental integration failures (2A-014/2A-023).
3. `docs/architecture/background-workers.md` updated with `ReplayWorker` row.
4. `docs/architecture/service-responsibility-map.md` updated with new replay services.
5. `docs/architecture/test-infrastructure.md` updated with new test counts.
