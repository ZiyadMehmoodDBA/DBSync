# Epic 5: Event Capture & Batch Creation — Design Spec

**Goal:** Implement the core sync pipeline: T-SQL trigger installation and drift detection, event reading, routing, batch creation, distributed locking, background jobs (SyncJob / RetryJob / PurgeJob / SchedulerRecovery), and the REST API surface for triggers and batch management.

**Architecture:** Interface-based service layer across six modules. SyncEngine orchestrates the pipeline end-to-end; transport is stubbed via `NoOpTransportService` so Epic 6 replaces exactly one registration. All jobs use a SQL Server–based distributed lock from `MSOSync.Persistence`. `IClock` in `MSOSync.Common` makes every time-dependent component unit-testable.

**Tech Stack:** .NET 9 / C# 13 / ASP.NET Core 9, EF Core 9.0.0 (SQL Server), MediatR 12.4.1, IMemoryCache, xUnit 2.9.3, FluentAssertions 6.12.2, Moq 4.20.72, Testcontainers.MsSql 4.4.0

---

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- All routes prefixed `api/v1/`
- DTOs cross module boundaries — EF entities never leave their originating service
- `BatchStatus` persisted as 2-char codes: `NE` (new), `SE` (sent), `OK` (ok), `ER` (error), `RT` (retry)
- `IClock` injected everywhere `DateTime.UtcNow` would otherwise appear
- Node ID embedded as `N'<id>'` literal in generated trigger DDL — never queried at runtime
- Exponential retry backoff: `delay = 2^(retryCount-1) × 5 minutes` (first retry = 5 min, second = 10, third = 20, fourth = 40)
- `DatabaseLockLease` implements `IAsyncDisposable` — always released via `await using`
- No `git add .` or `git add -A`; stage files by name only

---

## 1. Module Layout & Dependency Chain

```
MSOSync.Common          ← IClock, existing exceptions
    ↓
MSOSync.Persistence     ← Lock/ added: IDatabaseLockProvider, DatabaseLockProvider,
    |                              DatabaseLockLease, LockNames
    ↓
MSOSync.Trigger         ← TriggerDDLBuilder, ITriggerInstallationService,
    |                      TriggerInstallationService, ITriggerDriftDetector,
    |                      TriggerDriftDetector, TriggerVerifyResult
    |
MSOSync.Event           ← IEventReader, EventReader, IEventPurger, EventPurger
    |
MSOSync.Routing         ← IRoutingService, RoutingService
    |                      (IMemoryCache, 60 s TTL, MediatR invalidation)
    |
MSOSync.Batch           ← IBatchCreator, BatchCreator, BatchCompressor,
    |                      IBatchStateMachine, BatchStateMachine,
    |                      RetryEvaluator, BatchPurger, BatchStatus enum,
    |                      OutgoingBatchDto
    ↓
MSOSync.Engine          ← ITransportService, NoOpTransportService, SyncEngine
    ↓
MSOSync.Scheduler       ← SyncJob, RetryJob, PurgeJob, SchedulerRecovery
    ↓
MSOSync.Api             ← TriggersController (rebuild/verify added),
    |                      BatchController (new)
    ↓
MSOSync.App             ← DI wiring via extension methods
```

No circular dependencies. Each module exposes a `services.AddX(config)` extension method.

---

## 2. New Component: `IClock`

```csharp
// src/MSOSync.Common/IClock.cs
namespace MSOSync.Common;

public interface IClock
{
    DateTime UtcNow { get; }
}
```

```csharp
// src/MSOSync.Common/SystemClock.cs
namespace MSOSync.Common;

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
```

Registered as singleton: `services.AddSingleton<IClock, SystemClock>()`. Tests inject `FakeClock` (mutable `UtcNow`).

---

## 3. Lock Provider (MSOSync.Persistence/Lock/)

### `LockNames`
```csharp
public static class LockNames
{
    public const string SyncEngine  = "SYNC_ENGINE";
    public const string RetryEngine = "RETRY_ENGINE";
    public const string PurgeEngine = "PURGE_ENGINE";
}
```

### `IDatabaseLockProvider`
```csharp
public interface IDatabaseLockProvider
{
    Task<DatabaseLockLease?> TryAcquireAsync(string lockName, CancellationToken ct = default);
}
```

Returns `null` if lock held by another owner; returns `DatabaseLockLease` (IAsyncDisposable) on success.

### `DatabaseLockProvider`
Executes against `msosync.sync_lock`:
```sql
UPDATE msosync.sync_lock
SET    lock_owner = @owner, lock_time = GETUTCDATE()
WHERE  lock_name = @lockName
  AND  (lock_owner IS NULL OR lock_time < DATEADD(MINUTE, -10, GETUTCDATE()))
```
`rowsAffected == 1` → acquired. Owner = `$"{Environment.MachineName}:{Environment.ProcessId}"`.

### `DatabaseLockLease`
`IAsyncDisposable`. On dispose:
```sql
UPDATE msosync.sync_lock SET lock_owner = NULL
WHERE  lock_name = @lockName AND lock_owner = @owner
```

---

## 4. MSOSync.Trigger

### `TriggerDDLBuilder`
Generates `CREATE OR ALTER TRIGGER` DDL from `SyncTrigger` metadata. Key invariants:
- Node ID embedded as `N'<nodeId>'` literal (passed in at build time, not queried at runtime)
- `FOR JSON PATH, WITHOUT_ARRAY_WRAPPER` on INSERTED/DELETED
- `CURRENT_TRANSACTION_ID()` for `transaction_id`
- Writes to `msosync.sync_data_event`
- Fires `AFTER INSERT, UPDATE, DELETE` (only active event types included based on `SyncOnInsert/Update/Delete` flags)

### `ITriggerInstallationService` / `TriggerInstallationService`
- `InstallAsync(SyncTrigger trigger, string nodeId, CancellationToken ct)` — builds DDL, executes via `AppDbContext.Database.ExecuteSqlRawAsync`, bumps `trigger_version`, writes `SyncTriggerHist`
- `DropAsync(string triggerId, CancellationToken ct)` — drops SQL Server trigger if exists
- `RebuildAsync(string triggerId, CancellationToken ct)` — drop + install in sequence

### `ITriggerDriftDetector` / `TriggerDriftDetector`
- `DetectAllAsync(CancellationToken ct)` — queries `sys.triggers` vs `sync_trigger` for this node; emits log and metrics for DRIFT/MISSING
- `VerifyAsync(string triggerId, CancellationToken ct)` → `TriggerVerifyResult`

### `TriggerVerifyResult`
```csharp
public sealed record TriggerVerifyResult(
    string TriggerId,
    string NodeId,
    TriggerDriftStatus Status,      // VALID | DRIFT | MISSING
    int? InstalledVersion,
    int MetadataVersion,
    string? Message);
```

### DI: `AddTriggerEngine(IServiceCollection, IConfiguration)`
Registers `TriggerDDLBuilder` (singleton), `ITriggerInstallationService`, `ITriggerDriftDetector` (scoped).

---

## 5. MSOSync.Event

### `IEventReader` / `EventReader`
```csharp
Task<IReadOnlyList<SyncDataEvent>> ReadAsync(int batchSize, CancellationToken ct);
```
Query: `SELECT TOP @batchSize ... FROM msosync.sync_data_event WHERE is_processed = 0 ORDER BY event_id ASC`

### `IEventPurger` / `EventPurger`
```csharp
Task<int> PurgeAsync(CancellationToken ct);
```
Deletes `is_processed = 1` rows older than `retention_days` parameter. Returns deleted count.

### DI: `AddEventServices(IServiceCollection)`

---

## 6. MSOSync.Routing

### `IRoutingService` / `RoutingService`
```csharp
Task<IReadOnlyList<string>> ResolveAsync(string triggerId, CancellationToken ct);
```
Resolves `triggerId → [targetNodeId, ...]` via `sync_trigger_router` + `sync_router` join.

**Cache:** `IMemoryCache`, key `routing:trigger:{triggerId}`, 60-second absolute expiration.

**Invalidation:** Three MediatR notification handlers registered in this module:
- `TriggerMetadataChangedEvent` → `cache.Remove("routing:trigger:{triggerId}")`
- `RouterMetadataChangedEvent` → `cache.Clear()` (router change affects all routes)
- `ChannelMetadataChangedEvent` → `cache.Clear()`

### DI: `AddRoutingServices(IServiceCollection)`

---

## 7. MSOSync.Batch

### `BatchStatus` enum
```csharp
public enum BatchStatus { New, Sent, Ok, Error, Retry }
```
EF value converter: `New`→`"NE"`, `Sent`→`"SE"`, `Ok`→`"OK"`, `Error`→`"ER"`, `Retry`→`"RT"`.

### `IBatchStateMachine` / `BatchStateMachine`
```csharp
Task<bool> TransitionAsync(long batchId, BatchStatus from, BatchStatus to, CancellationToken ct);
```
Executes: `UPDATE sync_outgoing_batch SET status = @to WHERE batch_id = @id AND status = @from`
Returns `true` if `rowsAffected == 1`.

**Valid transitions:**
| From | To |
|------|-----|
| New | Sent |
| Sent | Ok |
| Sent | Error |
| Error | Retry |
| Retry | Sent |
| Retry | Error |

### `IBatchCreator` / `BatchCreator`
```csharp
Task<IReadOnlyList<SyncOutgoingBatch>> CreateBatchesAsync(
    IReadOnlyList<SyncDataEvent> events,
    IReadOnlyDictionary<long, IReadOnlyList<string>> routes,
    CancellationToken ct);
```
Grouping: `channel_id → target_node_id → transaction_id`. Transaction boundaries never split. Respects `max_batch_to_send` (row count) and `max_data_size` (cumulative `row_data` bytes) from `SyncChannel`. All inserts (`sync_outgoing_batch`, `sync_data_event_batch`) and `is_processed=1` updates run in one DB transaction.

### `BatchCompressor`
```csharp
byte[] Compress(byte[] data);
byte[] Decompress(byte[] data);
```
`GZipStream` wrapping `MemoryStream`. Used by transport layer (Epic 6); defined here so batch payload type is self-contained.

### `RetryEvaluator`
```csharp
Task<int> EvaluateAsync(CancellationToken ct);
```
Finds `sync_outgoing_batch WHERE status = 'ER' AND retry_count < max_retries AND next_retry_time <= @now`. Transitions `Error → Retry` via `BatchStateMachine`. Sets `next_retry_time = now + 2^(retryCount-1) × 5min`. Returns count of batches queued for retry.

### `BatchPurger`
```csharp
Task<int> PurgeAsync(CancellationToken ct);
```
Deletes `sync_outgoing_batch` in terminal states (`Ok`, `Ok` / `Partial`) older than `retention_days`. Never purges `Error` or `Retry` states automatically.

### `OutgoingBatchDto`
```csharp
public sealed record OutgoingBatchDto(
    long BatchId,
    BatchStatus Status,
    string TargetNodeId,
    string ChannelId,
    DateTime CreateTime,
    DateTime? SentTime,
    DateTime? AckTime,
    int RetryCount,
    int EventCount,
    string? ErrorMessage);
```

### DI: `AddBatchPipeline(IServiceCollection, IConfiguration)`

---

## 8. MSOSync.Engine

### `ITransportService`
```csharp
public interface ITransportService
{
    Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct);
}
```

### `NoOpTransportService`
```csharp
public sealed class NoOpTransportService(ILogger<NoOpTransportService> logger) : ITransportService
{
    public Task SendBatchAsync(SyncOutgoingBatch batch, CancellationToken ct)
    {
        logger.LogTrace("Transport not implemented. Batch {BatchId} skipped.", batch.BatchId);
        return Task.CompletedTask;
    }
}
```

### `SyncEngine`
```csharp
public sealed class SyncEngine(
    ITriggerDriftDetector driftDetector,
    IEventReader eventReader,
    IRoutingService routingService,
    IBatchCreator batchCreator,
    ITransportService transport,
    ILogger<SyncEngine> logger)
{
    public async Task RunAsync(CancellationToken ct);
}
```
Orchestration order (strict):
1. `driftDetector.DetectAllAsync(ct)` — log drift, never block pipeline
2. `eventReader.ReadAsync(batchSize, ct)` — read unprocessed events
3. For each event: `routingService.ResolveAsync(triggerId, ct)` — build routes map
4. `batchCreator.CreateBatchesAsync(events, routes, ct)` — write batches
5. For each batch: `transport.SendBatchAsync(batch, ct)` — no-op this epic

### DI: `AddSyncEngine(IServiceCollection, IConfiguration)`
Registers `SyncEngine` (scoped), `ITransportService → NoOpTransportService` (scoped).

---

## 9. MSOSync.Scheduler

### `SyncJob : BackgroundService`
`PeriodicTimer` with interval from `sync.interval.seconds` parameter (default 30s). On each tick:
```csharp
await using var lease = await _lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
if (lease == null) return; // another instance is running
await _syncEngine.RunAsync(ct);
```

### `RetryJob : BackgroundService`
`PeriodicTimer` every 5 minutes. Acquires `LockNames.RetryEngine`. Calls `RetryEvaluator.EvaluateAsync(ct)`.

### `PurgeJob : BackgroundService`
`PeriodicTimer` daily at 02:00 UTC (`IClock` to determine next fire time). Acquires `LockNames.PurgeEngine`. Calls `EventPurger.PurgeAsync(ct)` + `BatchPurger.PurgeAsync(ct)`.

### `SchedulerRecovery : IHostedService`
Runs once during `StartAsync` before background workers begin. Actions:
1. `SENT → RETRY` — batches that left the node but were never ACKed (restart scenario)
2. `RETRY` with `next_retry_time <= now` — requeue overdue retries
3. `NEW` — untouched; `SyncJob` picks up on first tick
4. Publishes `SchedulerRecoveryEvent` (MediatR) → audit entry

### DI: `AddSyncScheduler(IServiceCollection, IConfiguration)`
Registers `IHostedService` for `SchedulerRecovery` (runs first), then `SyncJob`, `RetryJob`, `PurgeJob` as `BackgroundService`.

---

## 10. REST API

### `TriggersController` additions

```
POST /api/v1/triggers/{triggerId}/rebuild   [Authorize("OperatorOrAbove")]
  → ITriggerInstallationService.RebuildAsync  → 200 + TriggerDto

POST /api/v1/triggers/{triggerId}/verify    [Authorize]
  → ITriggerDriftDetector.VerifyAsync  → 200 + TriggerVerifyResult
    { triggerId, nodeId, status, installedVersion, metadataVersion, message }
```

### New `BatchController`

```
GET  /api/v1/batches                        [Authorize]
  query: status?, nodeId?, channelId?, page=1, pageSize=20, sortBy=createTime, sortDirection=desc
  → { data: [OutgoingBatchDto], total, page, pageSize, totalPages }

GET  /api/v1/batches/{batchId:long}         [Authorize]
  → OutgoingBatchDto | 404

POST /api/v1/batches/{batchId:long}/retry   [Authorize("OperatorOrAbove")]
  → BatchStateMachine Error→Retry  → 200 | 404 | 409

POST /api/v1/batches/retry-all              [Authorize("OperatorOrAbove")]
  → { count: N, timestamp: "...", requestedBy: "username" }
```

### New DTOs (in `MSOSync.Api.Dtos.Batches/`)
- `BatchListRequest` — query params record; validated: `pageSize` 1–100, `sortBy` in allowed set
- `RetryAllResponse` — `{ int Count, DateTime Timestamp, string RequestedBy }`

### Validators (`MSOSync.Api.Validators/`)
- `BatchListRequestValidator` — pageSize 1–100, sortBy in `{"createTime","batchId","status"}`

---

## 11. Testing Strategy

### Unit Tests — `MSOSync.EngineTests` (new project)

SQLite in-memory (same `TestAppDbContext` pattern as `MSOSync.MetadataTests`) + Moq + `FakeClock`.

| Test class | Key cases |
|---|---|
| `TriggerDDLBuilderTests` | DDL contains table name, `FOR JSON PATH`, `CURRENT_TRANSACTION_ID()`, embedded node ID literal, correct schema; respects Insert/Update/Delete flags |
| `BatchCreatorTests` | Groups by channel/node/transaction; transaction boundary never split even at `max_batch_to_send` limit; `max_data_size` enforced |
| `BatchStateMachineTests` | All valid transitions return `true`; invalid transitions return `false`; concurrent `Sent→Error` + `Sent→Ok` — exactly one succeeds |
| `RetryEvaluatorTests` | Finds eligible ERROR batches; respects `next_retry_time` via `FakeClock`; exponential delay formula correct; `max_retries` ceiling respected |
| `RoutingServiceTests` | Cache hit returns without DB call; cache miss queries DB; MediatR event clears cache; TTL expiration causes cache miss after 61 seconds (FakeClock) |
| `SyncEngineTests` | Orchestration order verified (drift → read → route → create → transport); transport called once per batch |

### Integration Tests — `MSOSync.IntegrationTests` (new `EngineCollection`)

`EngineFixture` : `WebApplicationFactory<Program>`, Testcontainers SQL Server, full DI stack.

| Test | Validates |
|---|---|
| Install trigger on real table, INSERT row → verify `sync_data_event` row created | DDL generation + trigger installation + JSON payload + transaction_id |
| Run `SyncEngine.RunAsync()` → verify `sync_outgoing_batch` created (status=NEW) | Full pipeline through batch creation |
| `GET /api/v1/batches` → batch appears in list | BatchController + OutgoingBatchDto mapping |
| `POST /api/v1/batches/{id}/retry` on ERROR batch → 200, status=RETRY | BatchStateMachine + controller |
| `POST /api/v1/batches/retry-all` → `{ count > 0 }` | RetryEvaluator + controller |
| Two concurrent `SyncJob` ticks → exactly one acquires lock | `DatabaseLockProvider` / `DatabaseLockLease` |
| Seed SENT batch → restart → verify SENT→RETRY | `SchedulerRecovery` restart case |
| Seed RETRY batch with overdue `next_retry_time` → verify requeued | `SchedulerRecovery` overdue-retry case |

---

## 12. Program.cs Wiring (additions)

```csharp
builder.Services.AddSingleton<IClock, SystemClock>();      // MSOSync.Common
builder.Services.AddTriggerEngine(builder.Configuration);  // MSOSync.Trigger
builder.Services.AddEventServices();                       // MSOSync.Event
builder.Services.AddRoutingServices();                     // MSOSync.Routing
builder.Services.AddBatchPipeline(builder.Configuration);  // MSOSync.Batch
builder.Services.AddSyncEngine(builder.Configuration);     // MSOSync.Engine
builder.Services.AddSyncScheduler(builder.Configuration);  // MSOSync.Scheduler
```

`BatchController` and trigger additions auto-discovered via existing `AddApplicationPart(typeof(AuthController).Assembly)`.
