# Epic 5: Event Capture & Batch Creation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the full sync pipeline: distributed locking, T-SQL trigger DDL generation/installation/drift detection, event reading, route resolution, batch creation, background jobs, and the batch management REST API.

**Architecture:** Interface-based modules (Trigger → Event → Routing → Batch → Engine → Scheduler). `SyncEngine` orchestrates; `NoOpTransportService` stubs transport so Epic 6 replaces one registration. `IClock` in Common makes all time-dependent code testable.

**Tech Stack:** C# 13 / .NET 9, EF Core 9.0.0, MediatR 12.4.1, IMemoryCache, xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72, Testcontainers.MsSql 4.4.0, SQLite (unit tests)

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- All API routes prefixed `api/v1/`
- EF entities never leave their originating service — DTOs cross module boundaries
- `BatchStatus` enum: `New=0, Sent=1, Ok=2, Error=3, Retry=4` — stored as `tinyint`; `SyncOutgoingBatch.Status` stays `byte`; cast via `(byte)status`
- `IClock` injected everywhere `DateTime.UtcNow` would otherwise appear; `FakeClock` in tests
- Node ID embedded as `N'<id>'` literal in generated trigger DDL — never queried at runtime
- Exponential retry backoff: `delay = 2^(retryCount-1) × 5 minutes`
- `DatabaseLockLease` implements `IAsyncDisposable` — always released via `await using`
- Stage files by name only — no `git add .` or `git add -A`
- Spec: `docs/superpowers/specs/2026-06-22-epic5-event-capture-design.md`

---

## Task Summary

| # | Task | Key Files | Brief |
|---|------|-----------|-------|
| 1 | IClock + DatabaseLockProvider | `Common/IClock.cs`, `Persistence/Lock/*.cs` | [task1](2026-06-22-epic5-task1-iclock-lockprovider.md) |
| 2 | MSOSync.Trigger | `Trigger/SqlServerTriggerBuilder.cs`, `TriggerInstallationService.cs`, `TriggerDriftDetector.cs` | [task2](2026-06-22-epic5-task2-trigger.md) |
| 3 | MSOSync.Event | `Event/EventReader.cs`, `EventPurger.cs` | [task3](2026-06-22-epic5-task3-event.md) |
| 4 | MSOSync.Routing | `Routing/RoutingService.cs`, `RouteCacheState.cs` | [task4](2026-06-22-epic5-task4-routing.md) |
| 5 | MSOSync.Batch | `Batch/BatchStateMachine.cs`, `BatchCreator.cs`, `RetryProcessor.cs`, `BatchPurger.cs`, `GzipBatchCompressor.cs` | [task5](2026-06-22-epic5-task5-batch.md) |
| 6 | MSOSync.Engine | `Engine/SyncEngine.cs`, `NoOpTransportService.cs`, `SyncCycleCompletedEvent.cs` | [task6](2026-06-22-epic5-task6-engine.md) |
| 7 | MSOSync.Scheduler | `Scheduler/SyncJob.cs`, `RetryJob.cs`, `PurgeJob.cs`, `SchedulerRecovery.cs` | [task7](2026-06-22-epic5-task7-scheduler.md) |
| 8 | REST API + Program.cs wiring | `Api/Controllers/BatchController.cs`, `TriggersController.cs` (modify), `App/Program.cs` (modify) | [task8](2026-06-22-epic5-task8-api-wiring.md) |
| 9 | Unit tests — MSOSync.EngineTests | New test project: 6 test classes, FakeClock | [task9](2026-06-22-epic5-task9-unit-tests.md) |
| 10 | Integration tests — EngineCollection | `IntegrationTests/Engine/EngineFixture.cs`, `EngineTests.cs` | [task10](2026-06-22-epic5-task10-integration-tests.md) |

---

## Dependency Chain

```
Common (Task 1)
  ↓
Persistence/Lock (Task 1)
  ↓
Trigger (Task 2) — Event (Task 3) — Routing (Task 4)
  ↓
Batch (Task 5)
  ↓
Engine (Task 6)
  ↓
Scheduler (Task 7)
  ↓
API + Wiring (Task 8)
  ↓
Tests (Tasks 9, 10)
```

Tasks 2, 3, 4 are independent once Task 1 is done. Task 5 depends on all of 2–4. Tasks 9 and 10 can be dispatched simultaneously after Task 8.

---

## Verification (after Task 10)

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.EngineTests -c Debug
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~Engine" -c Debug
```

Expected: build clean, all tests green.
