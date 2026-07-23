# Phase 2D.2 — Distributed Lock Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bare `IDatabaseLockProvider` / `DatabaseLockProvider` pair with a provider-agnostic `IDistributedLockService` abstraction that supports SQL (default) and optional Redis backends, adds expiry semantics via M035 migration, and migrates all four existing callers.

**Architecture:** `IDistributedLockService` and `IDistributedLock` live in `MSOSync.Common` (no EF/Redis deps). The SQL implementation is in `MSOSync.Persistence`; the Redis implementation in `MSOSync.Persistence` behind a conditional registration (no Infrastructure project exists yet). M035 adds `lock_expiry datetime2(7) NULL` to `sync_lock` so TTLs are stored and honoured. All four existing callers (`SyncJob`, `RetryJob`, `PurgeJob`, `BatchController`) are updated to inject `IDistributedLockService`.

**Tech Stack:** C# 13 / .NET 9, EF Core 9 (`ExecuteSqlRawAsync`), StackExchange.Redis (Redis path only), xUnit, FluentAssertions, Moq, Testcontainers.MsSql (integration tests)

## Global Constraints

- `IDistributedLockService` and `IDistributedLock` live only in `MSOSync.Common.Locks` — no EF or Redis types
- `SqlDistributedLockService` and `SqlDistributedLock` in `MSOSync.Persistence.Lock`
- `RedisDistributedLockService` and `RedisDistributedLock` in `MSOSync.Persistence.Lock` (same project, conditional registration)
- Redis types (`IConnectionMultiplexer`) referenced only in `MSOSync.Persistence` — guarded by `#if` or conditional project ref if needed, but StackExchange.Redis is simply added to `MSOSync.Persistence.csproj`
- M035 adds ONLY `lock_expiry datetime2(7) NULL` to `sync_lock` — no other schema changes
- `TryAcquireAsync` is non-blocking: one atomic operation, returns `null` immediately if lock held
- All callers pass `$"{Environment.MachineName}:{Environment.ProcessId}"` as `owner`
- `LockNames.cs` is unchanged
- `LockAdminService.DeleteLockAsync` is NOT changed to use `IDistributedLockService`
- xUnit `[Fact]` only — no MSTest or NUnit
- `DistributedLockOptions.SectionName = "DistributedLocks"`
- Migration attribute: `[Migration("M035_DistributedLockExpiry")]`
- `IDatabaseLockProvider`, `DatabaseLockProvider`, `DatabaseLockLease` are DELETED once all callers are migrated (Task 4)

---

## File Map

### New files — MSOSync.Common
| File | Purpose |
|---|---|
| `src/MSOSync.Common/Locks/IDistributedLockService.cs` | Public interface: TryAcquireAsync / RenewAsync / ReleaseAsync / IsHeldAsync |
| `src/MSOSync.Common/Locks/IDistributedLock.cs` | Handle interface: Resource / Owner / ExpiresAt / IAsyncDisposable |
| `src/MSOSync.Common/Locks/DistributedLockOptions.cs` | Options bound to `"DistributedLocks"` config section |
| `src/MSOSync.Common/Locks/LockProviderType.cs` | Enum: Sql, Redis |
| `src/MSOSync.Common/Locks/DistributedLockHelper.cs` | Extension: `TryAcquireWithRetryAsync` for callers that want retry |

### New files — MSOSync.Persistence
| File | Purpose |
|---|---|
| `src/MSOSync.Persistence/Lock/SqlDistributedLockService.cs` | SQL implementation via `ExecuteSqlRawAsync` |
| `src/MSOSync.Persistence/Lock/SqlDistributedLock.cs` | IDistributedLock handle that calls ReleaseAsync on dispose |
| `src/MSOSync.Persistence/Lock/RedisDistributedLockService.cs` | Redis SET NX PX + Lua renew/release |
| `src/MSOSync.Persistence/Lock/RedisDistributedLock.cs` | IDistributedLock handle for Redis path |
| `src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs` | `AddDistributedLocks(IServiceCollection, IConfiguration)` |
| `src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.cs` | Adds `lock_expiry datetime2(7) NULL` |
| `src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.Designer.cs` | EF migration stub |

### Modified files
| File | Change |
|---|---|
| `src/MSOSync.Persistence/Entities/SyncLock.cs` | Add `LockExpiry` property |
| `src/MSOSync.Persistence/Configurations/SyncLockConfiguration.cs` | Map `lock_expiry` column |
| `src/MSOSync.Persistence/PersistenceServiceExtensions.cs` | Replace `AddScoped<IDatabaseLockProvider>` with `AddDistributedLocks(configuration)` |
| `src/MSOSync.Persistence/MSOSync.Persistence.csproj` | Add StackExchange.Redis package reference |
| `src/MSOSync.App/appsettings.json` | Add `"DistributedLocks"` section |
| `src/MSOSync.Metadata/Locks/LockDto.cs` | Add `LockExpiry` property |
| `src/MSOSync.Metadata/Locks/LockAdminService.cs` | Project `LockExpiry` in `GetLocksAsync` |
| `src/MSOSync.Scheduler/SyncJob.cs` | Inject `IDistributedLockService` + `IOptions<DistributedLockOptions>` |
| `src/MSOSync.Scheduler/RetryJob.cs` | Same |
| `src/MSOSync.Scheduler/PurgeJob.cs` | Same |
| `src/MSOSync.Api/Controllers/BatchController.cs` | Same |

### Deleted files
| File | Reason |
|---|---|
| `src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs` | Replaced by IDistributedLockService |
| `src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs` | Replaced by SqlDistributedLockService |
| `src/MSOSync.Persistence/Lock/DatabaseLockLease.cs` | Replaced by SqlDistributedLock |

### Test files
| File | Purpose |
|---|---|
| `tests/MSOSync.Tests/Lock/SqlDistributedLockServiceTests.cs` | Unit tests — SQL service (uses real LocalDB via Testcontainers) |
| `tests/MSOSync.Tests/Lock/RedisDistributedLockServiceTests.cs` | Unit tests — Redis service (mocked IConnectionMultiplexer) |
| `tests/MSOSync.IntegrationTests/Lock/SqlDistributedLockIntegrationTests.cs` | Integration: two callers contend on same DB row |
| `tests/MSOSync.SchedulerTests/SyncJobTests.cs` | MODIFIED: mock swap IDatabaseLockProvider → IDistributedLockService |
| `tests/MSOSync.SchedulerTests/RetryJobTests.cs` | MODIFIED: same |
| `tests/MSOSync.SchedulerTests/PurgeJobTests.cs` | MODIFIED: same |

---

## Tasks

| Task | Deliverable | Test |
|---|---|---|
| [Task 1](2026-07-23-phase-2D-2-task-1-interfaces.md) | Common interfaces + options + helper | Unit tests in MSOSync.Tests |
| [Task 2](2026-07-23-phase-2D-2-task-2-sql-impl.md) | SqlDistributedLockService + M035 + DI extension | Unit + integration tests in MSOSync.Tests / MSOSync.IntegrationTests |
| [Task 3](2026-07-23-phase-2D-2-task-3-redis-impl.md) | RedisDistributedLockService | Unit tests with mocked IConnectionMultiplexer |
| [Task 4](2026-07-23-phase-2D-2-task-4-migrate-callers.md) | Migrate SyncJob/RetryJob/PurgeJob/BatchController + update LockDto + delete old files | Existing scheduler tests pass with new mocks |

---

## Execution Order

Tasks must run in sequence: 1 → 2 → 3 → 4.
- Task 2 depends on Task 1 (needs `IDistributedLockService` / `IDistributedLock`).
- Task 3 depends on Task 1 (same reason).
- Task 4 depends on Tasks 1 + 2 (needs the registered service and the options class).
