# Phase 2D.3 — Distributed Scheduler: Master Plan

**Date:** 2026-07-23
**Branch:** `feat/2D.3-distributed-scheduler`
**Depends on:** Phase 2D.2 (`IDistributedLockService` SQL provider must be registered)
**Spec:** `docs/superpowers/specs/2026-07-23-phase-2D-3-distributed-scheduler.md`

---

## Goal

Ensure `SyncJob`, `PullJob`, `PurgeJob`, and `RetryJob` execute on exactly one hub instance per tick in a horizontally-scaled deployment. Uses per-job distributed locks with heartbeat renewal, a health reporter, and a dedicated scheduler-status endpoint.

---

## Dependency Note

- `IDatabaseLockProvider` (existing, `MSOSync.Persistence`) is extended with `RenewAsync` and `ReleaseAsync`.
- `IDistributedLockService` from 2D.2 is an optional future upgrade path; `SchedulerLockFactory` targets `IDatabaseLockProvider` directly in 2D.3.
- No new DB migrations. The existing `sync_lock` table (`lock_name`, `lock_owner`, `lock_time`, `scope`) is reused. Four new rows are seeded at startup.

---

## Task List

| # | File | Description |
|---|------|-------------|
| T1 | `2026-07-23-phase-2D-3-task-1-scheduler-lock.md` | `ISchedulerLock`, `ISchedulerLockFactory`, `SchedulerLockOptions`, `SchedulerLockImpl` (with renewal Task), `SchedulerJobGuard.RunAsync`, unit tests |
| T2 | `2026-07-23-phase-2D-3-task-2-health-reporter.md` | `ISchedulerHealthReporter`, `SchedulerHealthReporter`, `SchedulerHealthContributor` wired into `/health` endpoint |
| T3 | `2026-07-23-phase-2D-3-task-3-migrate-jobs.md` | Extend `IDatabaseLockProvider` with `RenewAsync`/`ReleaseAsync`; migrate `SyncJob`, `PullJob`, `PurgeJob`, `RetryJob`; seed lock rows; update `SyncSchedulerExtensions`; mark old `LockNames` `[Obsolete]`; update existing unit tests |
| T4 | `2026-07-23-phase-2D-3-task-4-endpoint-and-integration-tests.md` | `GET /api/v1/system/scheduler-status` controller action; dual-instance integration tests; endpoint integration tests |

---

## Execution Order

Tasks must be executed in order: T1 → T2 → T3 → T4.

- T3 depends on T1 (uses `SchedulerJobGuard`, `ISchedulerLockFactory`, `ISchedulerHealthReporter`).
- T3 depends on T2 (uses `ISchedulerHealthReporter`).
- T4 depends on T2 (uses `ISchedulerHealthReporter` in endpoint) and T3 (integration tests exercise migrated jobs).

---

## Files Created / Modified

### New files (MSOSync.Scheduler)
- `src/MSOSync.Scheduler/ISchedulerLock.cs`
- `src/MSOSync.Scheduler/ISchedulerLockFactory.cs`
- `src/MSOSync.Scheduler/SchedulerLockOptions.cs`
- `src/MSOSync.Scheduler/Internal/SchedulerLockImpl.cs`
- `src/MSOSync.Scheduler/Internal/SchedulerLockFactory.cs`
- `src/MSOSync.Scheduler/SchedulerJobGuard.cs`
- `src/MSOSync.Scheduler/ISchedulerHealthReporter.cs`
- `src/MSOSync.Scheduler/SchedulerJobStatus.cs`
- `src/MSOSync.Scheduler/SchedulerHealthReporter.cs`

### New files (MSOSync.App)
- `src/MSOSync.App/Health/SchedulerHealthContributor.cs`

### Modified files
- `src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs` — add `RenewAsync`, `ReleaseAsync`
- `src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs` — implement `RenewAsync`, `ReleaseAsync`; add seed method
- `src/MSOSync.Persistence/Lock/LockNames.cs` — mark constants `[Obsolete]`
- `src/MSOSync.Scheduler/SyncJob.cs` — use `SchedulerJobGuard.RunAsync`
- `src/MSOSync.Scheduler/PullJob.cs` — add `SchedulerJobGuard.RunAsync`
- `src/MSOSync.Scheduler/PurgeJob.cs` — use `SchedulerJobGuard.RunAsync`
- `src/MSOSync.Scheduler/RetryJob.cs` — use `SchedulerJobGuard.RunAsync`
- `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` — register new services + options validation
- `src/MSOSync.Api/Controllers/SystemController.cs` — add `scheduler-status` action
- `src/MSOSync.App/Program.cs` — register `SchedulerHealthContributor`
- `appsettings.json` (if present) — add `Scheduler:Lock` section

### Modified test files
- `tests/MSOSync.SchedulerTests/SyncJobTests.cs`
- `tests/MSOSync.SchedulerTests/PurgeJobTests.cs`
- `tests/MSOSync.SchedulerTests/RetryJobTests.cs`
- `tests/MSOSync.SchedulerTests/PullJobTests.cs`

### New test files
- `tests/MSOSync.SchedulerTests/SchedulerLockFactoryTests.cs`
- `tests/MSOSync.SchedulerTests/SchedulerLockImplTests.cs`
- `tests/MSOSync.SchedulerTests/SchedulerJobGuardTests.cs`
- `tests/MSOSync.SchedulerTests/SchedulerHealthReporterTests.cs`
- `tests/MSOSync.IntegrationTests/Scheduler/SchedulerLockIntegrationTests.cs`
- `tests/MSOSync.IntegrationTests/Scheduler/SchedulerStatusEndpointTests.cs`

---

## Lock name convention

```
scheduler:SyncJob
scheduler:PullJob
scheduler:PurgeJob
scheduler:RetryJob
```

Old names (`SYNC_ENGINE`, `RETRY_ENGINE`, `PURGE_ENGINE`) remain in `LockNames` marked `[Obsolete]`.

---

## Verification

After T4: `dotnet test` must pass with zero failures. No new EF migrations added.
