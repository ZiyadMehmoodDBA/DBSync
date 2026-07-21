# Phase 2B.1 — Node Operations Orchestration Master Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standalone node drain mode, rolling node maintenance, and rolling upgrades on one durable orchestration engine; absorbs deferred 2A-029.

**Architecture:** New reversible `Draining` lifecycle state (routing exclusion falls out of `NodeSyncPolicy.EligibleExpression` requiring `Active`). Rolling operations = `sync_operation` rows (types `RollingMaintenance`/`RollingUpgrade`) + new `sync_operation_step` table, advanced by a restart-safe `RollingOperationWorker` (`PeriodicTimer` + `IWorkerStatusRegistry`, stateless tick re-reads DB). Maintenance stays an orthogonal flag; rolling ops compose drain → maintenance flag → verify → resume.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / FluentValidation / MediatR / xUnit + FluentAssertions + Moq / React 19 + TypeScript

Spec: `docs/superpowers/specs/2026-07-21-phase-2B-1-orchestration-design.md`

## Global Constraints

- All Phase 2A rules apply (`.github/PULL_REQUEST_TEMPLATE.md`): named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3.
- Binary/package delivery for upgrades out of scope — hub verifies `AgentVersion` only.
- Heartbeat wire contract unchanged: existing `NodeVersion` field persists into new `SyncNode.AgentVersion` column.
- Permission: existing `MANAGE_NODE_LIFECYCLE` (`SystemPermissions.ManageNodeLifecycle`). No new permission.
- All work commits directly to `main`. Test commands per `docs/architecture/test-infrastructure.md`; Docker-dependent integration failures 2A-014/2A-023 are accepted environmental.
- Migration numbering: next is **M033** (`src/MSOSync.Persistence/Migrations/M033_RollingOperations.cs`).

---

## Execution Order

| # | Status | Task file | Deliverable |
|---|---|---|---|
| 1 | ✅ | [Task 1 — Persistence + M033](2026-07-21-phase-2B-1-task-1-persistence.md) | `Draining` enum value, `AgentVersion`/`DrainCompletedAt` columns, `SyncOperationStep` entity+config+DbSet, `OperationType` additions, M033 migration |
| 2 | ✅ | [Task 2 — Drain lifecycle](2026-07-21-phase-2B-1-task-2-drain-lifecycle.md) | State-machine transitions, `StartDrainAsync`/`ResumeFromDrainAsync`, drain/resume endpoints, transition metadata, node-read query service (2A-029 part 1) |
| 3 | ✅ | [Task 3 — Heartbeat](2026-07-21-phase-2B-1-task-3-heartbeat.md) | Heartbeat accepts `Draining` (matrix), persists `NodeVersion` → `AgentVersion` |
| 4 | ✅ | [Task 4 — Rolling service](2026-07-21-phase-2B-1-task-4-rolling-service.md) | `OperationStateException`, policy/step models, `IRollingOperationService` + impl + unit tests |
| 5 | ✅ | [Task 5 — Rolling controller](2026-07-21-phase-2B-1-task-5-rolling-controller.md) | `RollingOperationsController`, DTOs, validator, handler mapping |
| 6 | ✅ | [Task 6 — Worker](2026-07-21-phase-2B-1-task-6-worker.md) | `RollingOperationWorker` (drain detection, step advance, wave gate) + SchedulerTests |
| 7 | ✅ | [Task 7 — 2A-029 remainder](2026-07-21-phase-2B-1-task-7-2a029.md) | Auth membership + outgoing-batch query services; controllers lose `AppDbContext` |
| 8 | ✅ | [Task 8 — Frontend](2026-07-21-phase-2B-1-task-8-frontend.md) | Drain menu/badge, `AgentVersion` column, rolling wizard + detail panel |
| 9 | ✅ | [Task 9 — Integration + docs](2026-07-21-phase-2B-1-task-9-integration-docs.md) | Drain endpoint matrix, rolling API, M033 smoke integration tests; docs updates; final gate |

Tasks 1→6 are sequential (each consumes prior interfaces). Task 7 independent after Task 2. Task 8 after 5+6. Task 9 last.

## Completion Criteria

1. All 9 task files complete, committed to `main`.
2. `dotnet test D:\MSOSync\MSOSync.sln` — all unit assemblies green; only accepted environmental integration failures (2A-014/2A-023).
3. 2A-029 flipped to Complete in `docs/architecture/audit-backlog-2A.md`.
4. `docs/architecture/test-infrastructure.md` + `docs/architecture/service-responsibility-map.md` + `docs/architecture/background-workers.md` updated.
