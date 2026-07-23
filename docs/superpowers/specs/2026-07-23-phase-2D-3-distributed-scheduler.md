# Phase 2D.3 — Distributed Scheduler Design Spec

**Status:** Approved for implementation  
**Date:** 2026-07-23  
**Phase:** 2D — Scalability & Performance  
**Depends on:** Phase 2D.2 — `IDistributedLockService` (SQL provider must be available)  
**Owners:** MSOSync.Scheduler, MSOSync.Common  

---

## 1. Goal

Ensure that in a horizontally-scaled MSOSync deployment with multiple hub instances, background jobs (`SyncJob`, `PullJob`, `PurgeJob`, `RetryJob`) execute on exactly one instance per tick — not simultaneously on all instances. A tick skipped by a losing instance must leave no side effects. A single-instance deployment must behave identically to the current implementation.

---

## 2. Problem Statement

### Current design

All four background jobs use `PeriodicTimer` or `Task.Delay` to fire on a wall-clock schedule and execute directly via scoped services. `SyncJob`, `PurgeJob`, and `RetryJob` each call `IDatabaseLockProvider.TryAcquireAsync` against a named row in `sync_lock` (SQL Server application-level lock), which provides coarse per-job mutual exclusion.

### Why it breaks with more than one instance

The current `DatabaseLockProvider` acquires a lock with a 10-minute stale timeout:

```sql
UPDATE [msosync].[sync_lock]
SET lock_owner = @owner, lock_time = GETUTCDATE()
WHERE lock_name = @lockName
  AND (lock_owner IS NULL OR lock_time < DATEADD(MINUTE, -10, GETUTCDATE()))
```

This creates three failure modes under horizontal scale:

**1. No renewal — stale lock kills active job.** If `SyncJob` takes more than 10 minutes (large datasets, slow DB), a second instance observes `lock_time < 10 minutes ago` and acquires the lock mid-run. Both instances are now executing the same sync pass simultaneously, producing duplicate outgoing batches, conflicting sequence numbers, and potential data corruption.

**2. PullJob has no lock at all.** `PullJob.RunTickAsync` calls `PollAllAsync` directly — there is no `IDatabaseLockProvider.TryAcquireAsync` call in the pull path. With two instances both running `PullJob`, every source node is polled twice per tick, duplicate incoming batches are inserted (caught by the duplicate check but causing unnecessary ACK traffic and lock contention), and `InsertIncomingBatchAsync` races on the same rows.

**3. Lock name collision risk.** `LockNames.SyncEngine = "SYNC_ENGINE"` is a constant string. The `sync_lock` table has no per-tenant or per-instance scoping on lock rows. If a future multi-tenant scenario places two tenants on the same DB, their scheduler locks collide. The new convention `scheduler:{JobName}` provides a separate namespace.

### What "distributed" means here

2D.3 does not introduce a new lock store. It wraps the existing `IDatabaseLockProvider` (SQL) or the `IDistributedLockService` from 2D.2 (Redis when configured) behind a uniform `ISchedulerLock` abstraction, adds a **heartbeat renewal loop** so active jobs never time out, and surfaces per-job lock state via a new `ISchedulerHealthReporter` interface wired into the existing `/health` endpoint.

---

## 3. Architecture

### 3.1 Design choice: per-job distributed lock (Option A)

Two approaches were evaluated:

| Approach | Pros | Cons |
|---|---|---|
| **A — Per-job lock** | No single point of failure; jobs are independent; simple rollout | Each job needs its own guard block |
| **B — Leader election** | One change point; simpler mental model | Leader crash = all jobs stop until re-election; adds leader election complexity |

**Option A is selected.** Each job independently tries to acquire `scheduler:{JobName}` at the start of every tick. The winning instance runs; losers log at Debug and skip. There is no concept of a "leader" — jobs are individually scheduled by whichever instance holds their lock.

### 3.2 Lock name convention

```
scheduler:SyncJob
scheduler:PullJob
scheduler:PurgeJob
scheduler:RetryJob
```

These are distinct from existing `LockNames` constants (`SYNC_ENGINE`, `RETRY_ENGINE`, `PURGE_ENGINE`). The old lock names remain in place for the current implementation; the new names are introduced by 2D.3. When 2D.3 jobs are wired in, the old direct `IDatabaseLockProvider` calls inside job bodies are removed and replaced by the `ISchedulerLock` guard.

### 3.3 Component map

```
MSOSync.Scheduler
├── ISchedulerLock                    ← thin wrapper: try-acquire + heartbeat renewal
├── ISchedulerLockFactory             ← creates ISchedulerLock instances by job name
├── SchedulerLockOptions              ← config: TTL, renewal interval, lock prefix
├── SchedulerJobGuard                 ← acquires lock + runs renewal loop as inner Task
├── ISchedulerHealthReporter          ← exposes per-job lock state (Running/Standby)
├── SchedulerHealthReporter           ← in-memory implementation of above
├── SchedulerHealthContributor        ← wires into ISystemHealthContributor
├── SyncJob                           ← updated: uses SchedulerJobGuard
├── PullJob                           ← updated: uses SchedulerJobGuard (was unguarded)
├── PurgeJob                          ← updated: uses SchedulerJobGuard
├── RetryJob                          ← updated: uses SchedulerJobGuard
└── SyncSchedulerExtensions           ← registers new services

MSOSync.Common (no changes)           ← IWorkerStatusRegistry remains unchanged
MSOSync.Persistence (no changes)      ← SyncLock entity and DatabaseLockProvider unchanged
MSOSync.App
└── Health/SchedulerStatusContributor ← exposes /health and /api/v1/system/scheduler-status
```

---

## 4. ISchedulerLock

Defined in `MSOSync.Scheduler`.

```csharp
namespace MSOSync.Scheduler;

/// <summary>
/// Represents an acquired distributed lock for a single job tick.
/// Disposal releases the lock immediately. The renewal loop runs internally
/// and extends the lock TTL every RenewalIntervalSeconds while the job runs.
/// </summary>
public interface ISchedulerLock : IAsyncDisposable
{
    /// <summary>Name of the job this lock was acquired for.</summary>
    string JobName { get; }

    /// <summary>UTC timestamp when the lock was acquired.</summary>
    DateTimeOffset AcquiredAt { get; }

    /// <summary>Identity of the instance holding the lock (MachineName:PID).</summary>
    string Owner { get; }
}
```

### ISchedulerLockFactory

```csharp
namespace MSOSync.Scheduler;

/// <summary>
/// Tries to acquire the distributed scheduler lock for a given job name.
/// Returns null if another instance already holds the lock.
/// </summary>
public interface ISchedulerLockFactory
{
    /// <summary>
    /// Attempts to acquire "scheduler:{jobName}" lock.
    /// Returns a live <see cref="ISchedulerLock"/> (with renewal running) on success,
    /// or null if the lock is held elsewhere.
    /// </summary>
    Task<ISchedulerLock?> TryAcquireAsync(string jobName, CancellationToken ct);
}
```

### SchedulerLockOptions

```csharp
namespace MSOSync.Scheduler;

public sealed class SchedulerLockOptions
{
    public const string Section = "Scheduler:Lock";

    /// <summary>Lock TTL in seconds. Default 120. Must be > 2x RenewalIntervalSeconds.</summary>
    public int TtlSeconds { get; init; } = 120;

    /// <summary>How often to renew the lock while a job is running. Default 10.</summary>
    public int RenewalIntervalSeconds { get; init; } = 10;

    /// <summary>Prefix prepended to every job lock name. Default "scheduler:".</summary>
    public string LockPrefix { get; init; } = "scheduler:";
}
```

---

## 5. Guard Pattern in ExecuteAsync

### 5.1 The SchedulerJobGuard helper

`SchedulerJobGuard` is a static utility class that encapsulates the standard try-acquire / run / release pattern so each job body stays concise.

```csharp
namespace MSOSync.Scheduler;

/// <summary>
/// Runs <paramref name="work"/> under a distributed lock for <paramref name="jobName"/>.
/// If the lock cannot be acquired, logs at Debug and returns immediately.
/// Renewal of the lock runs as a background task for the duration of <paramref name="work"/>.
/// </summary>
public static class SchedulerJobGuard
{
    public static async Task RunAsync(
        string               jobName,
        ISchedulerLockFactory lockFactory,
        ISchedulerHealthReporter health,
        ILogger              logger,
        Func<CancellationToken, Task> work,
        CancellationToken    ct)
    {
        await using var schedulerLock = await lockFactory.TryAcquireAsync(jobName, ct);

        if (schedulerLock is null)
        {
            logger.LogDebug("{Job}: lock held by another instance — skipping tick", jobName);
            health.RecordStandby(jobName);
            return;
        }

        health.RecordRunning(jobName, schedulerLock.Owner, schedulerLock.AcquiredAt);
        logger.LogDebug("{Job}: acquired scheduler lock (owner={Owner})", jobName, schedulerLock.Owner);

        try
        {
            await work(ct);
        }
        finally
        {
            health.RecordIdle(jobName);
        }
        // IAsyncDisposable on schedulerLock releases the lock here
    }
}
```

### 5.2 Updated SyncJob

The current `SyncJob.RunTickAsync` calls `IDatabaseLockProvider` directly. After 2D.3, the internal lock call is removed and replaced by the guard:

```csharp
public sealed class SyncJob(
    IServiceScopeFactory    scopeFactory,
    IOptions<SyncOptions>   syncOptions,
    ISchedulerLockFactory   lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry   registry,
    ILogger<SyncJob>        logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            registry.RecordTickStart(nameof(SyncJob));
            try
            {
                await SchedulerJobGuard.RunAsync(
                    nameof(SyncJob),
                    lockFactory,
                    health,
                    logger,
                    async innerCt =>
                    {
                        await using var scope = scopeFactory.CreateAsyncScope();
                        var engine = scope.ServiceProvider.GetRequiredService<SyncEngine>();
                        await engine.RunAsync(innerCt);
                    },
                    ct);

                registry.RecordTickComplete(nameof(SyncJob));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(SyncJob), ex);
                logger.LogError(ex, "SyncJob run failed");
            }
        }
    }
}
```

`PullJob`, `PurgeJob`, and `RetryJob` follow the same shape. The lock guard wraps the work lambda; the `IWorkerStatusRegistry` calls remain outside the guard so skipped ticks still register as completed ticks (not failures).

### 5.3 PullJob specifics

`PullJob` currently has **no lock guard** — it polls all source nodes directly. After 2D.3 the guard wraps `PollAllAsync`:

```csharp
internal async Task RunTickAsync(string localNodeId, CancellationToken ct)
{
    registry.RecordTickStart(nameof(PullJob));
    try
    {
        await SchedulerJobGuard.RunAsync(
            nameof(PullJob),
            lockFactory,
            health,
            logger,
            innerCt => PollAllAsync(localNodeId, innerCt),
            ct);

        registry.RecordTickComplete(nameof(PullJob));
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
        registry.RecordTickFailed(nameof(PullJob), ex);
        logger.LogError(ex, "PullJob tick failed");
    }
}
```

Note: The `IsPullEnabledAsync` check runs before the guard, so Push-mode nodes still exit early without attempting lock acquisition.

---

## 6. Heartbeat Renewal Design

### 6.1 Why renewal is mandatory

The current `DatabaseLockProvider` uses a 10-minute stale timeout. `SyncJob` with a large data set or a slow upstream can easily exceed 10 minutes. Without renewal, the lock expires while the job is still running, and a second instance acquires it. Renewal extends the lock TTL before it can be stolen.

### 6.2 SchedulerLockImpl (internal)

`ISchedulerLockFactory` returns a concrete `SchedulerLockImpl` that starts a renewal `Task` immediately on acquisition:

```csharp
internal sealed class SchedulerLockImpl : ISchedulerLock
{
    private readonly IDatabaseLockProvider _lockProvider; // or IDistributedLockService from 2D.2
    private readonly SchedulerLockOptions  _options;
    private readonly ILogger               _logger;
    private readonly CancellationTokenSource _renewalCts = new();
    private readonly Task _renewalTask;

    public string          JobName    { get; }
    public DateTimeOffset  AcquiredAt { get; } = DateTimeOffset.UtcNow;
    public string          Owner      { get; } =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    internal SchedulerLockImpl(
        string                  jobName,
        IDatabaseLockProvider   lockProvider,
        SchedulerLockOptions    options,
        ILogger                 logger)
    {
        JobName       = jobName;
        _lockProvider = lockProvider;
        _options      = options;
        _logger       = logger;
        _renewalTask  = RunRenewalLoopAsync(_renewalCts.Token);
    }

    private async Task RunRenewalLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.RenewalIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                await RenewAsync(ct);
                _logger.LogDebug(
                    "SchedulerLock: renewed {JobName} lock (owner={Owner})",
                    JobName, Owner);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "SchedulerLock: renewal failed for {JobName} — lock may expire", JobName);
            }
        }
    }

    private Task RenewAsync(CancellationToken ct)
    {
        // Resets lock_time to GETUTCDATE() for our owner row,
        // extending the stale timeout window.
        var lockName = $"{_options.LockPrefix}{JobName}";
        return _lockProvider.RenewAsync(lockName, Owner, ct);
    }

    public async ValueTask DisposeAsync()
    {
        // 1. Stop renewal loop
        await _renewalCts.CancelAsync();
        try { await _renewalTask; } catch { /* swallow */ }
        _renewalCts.Dispose();

        // 2. Release the lock immediately
        var lockName = $"{_options.LockPrefix}{JobName}";
        await _lockProvider.ReleaseAsync(lockName, Owner);
    }
}
```

### 6.3 IDatabaseLockProvider extension (RenewAsync / ReleaseAsync)

2D.3 adds two methods to `IDatabaseLockProvider`:

```csharp
public interface IDatabaseLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct = default);

    // NEW in 2D.3
    Task RenewAsync(string lockName, string owner, CancellationToken ct = default);
    Task ReleaseAsync(string lockName, string owner);
}
```

`DatabaseLockProvider.RenewAsync` implementation:

```csharp
public async Task RenewAsync(string lockName, string owner, CancellationToken ct = default)
{
    await db.Database.ExecuteSqlRawAsync(
        $"UPDATE [{Schema}].[sync_lock] " +
        "SET lock_time = GETUTCDATE() " +
        "WHERE lock_name = {0} AND lock_owner = {1}",
        new object[] { lockName, owner },
        ct);
}
```

`DatabaseLockProvider.ReleaseAsync` implementation:

```csharp
public async Task ReleaseAsync(string lockName, string owner)
{
    await db.Database.ExecuteSqlRawAsync(
        $"UPDATE [{Schema}].[sync_lock] " +
        "SET lock_owner = NULL, lock_time = NULL " +
        "WHERE lock_name = {0} AND lock_owner = {1}",
        new object[] { lockName, owner },
        CancellationToken.None);
}
```

Note: No new DB migration is required. New lock name rows (`scheduler:SyncJob`, etc.) are **inserted** into the existing `sync_lock` table via a seed script or on first use. The existing rows (`SYNC_ENGINE`, `RETRY_ENGINE`, `PURGE_ENGINE`) are left in place until the old direct lock calls in job bodies are removed; at that point they become unused and can be cleaned up.

### 6.4 Seed rows

The following `sync_lock` rows must exist before 2D.3 jobs run. Insert via startup seed or migration data script (no schema change):

```sql
INSERT INTO [msosync].[sync_lock] (lock_name, lock_owner, lock_time, scope)
SELECT 'scheduler:SyncJob',  NULL, NULL, 0 WHERE NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'scheduler:SyncJob');

INSERT INTO [msosync].[sync_lock] (lock_name, lock_owner, lock_time, scope)
SELECT 'scheduler:PullJob',  NULL, NULL, 0 WHERE NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'scheduler:PullJob');

INSERT INTO [msosync].[sync_lock] (lock_name, lock_owner, lock_time, scope)
SELECT 'scheduler:PurgeJob', NULL, NULL, 0 WHERE NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'scheduler:PurgeJob');

INSERT INTO [msosync].[sync_lock] (lock_name, lock_owner, lock_time, scope)
SELECT 'scheduler:RetryJob', NULL, NULL, 0 WHERE NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'scheduler:RetryJob');
```

These are idempotent and run on application startup via `IHostedService` or as part of `AddSyncScheduler`.

---

## 7. ISchedulerHealthReporter

### 7.1 Interface

```csharp
namespace MSOSync.Scheduler;

public enum SchedulerJobMode { Idle, Running, Standby }

public sealed record SchedulerJobStatus(
    string           JobName,
    SchedulerJobMode Mode,
    string?          LockOwner,
    DateTimeOffset?  LockedSince,
    DateTimeOffset   LastUpdated);

/// <summary>
/// Tracks per-job lock state for this instance.
/// "Running" = this instance holds the lock and the job is executing.
/// "Standby" = another instance holds the lock; this instance skipped the tick.
/// "Idle" = lock not held by anyone (between ticks).
/// </summary>
public interface ISchedulerHealthReporter
{
    void RecordRunning(string jobName, string owner, DateTimeOffset acquiredAt);
    void RecordStandby(string jobName);
    void RecordIdle(string jobName);
    SchedulerJobStatus[] GetAll();
    SchedulerJobStatus GetOne(string jobName);
}
```

### 7.2 SchedulerHealthReporter (implementation)

Registered as a singleton in `MSOSync.Scheduler`. Uses `ConcurrentDictionary<string, SchedulerJobStatus>` — no external state, no persistence.

```csharp
namespace MSOSync.Scheduler;

public sealed class SchedulerHealthReporter : ISchedulerHealthReporter
{
    private readonly ConcurrentDictionary<string, SchedulerJobStatus> _statuses = new();

    public void RecordRunning(string jobName, string owner, DateTimeOffset acquiredAt)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Running, owner, acquiredAt, DateTimeOffset.UtcNow);

    public void RecordStandby(string jobName)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Standby, null, null, DateTimeOffset.UtcNow);

    public void RecordIdle(string jobName)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Idle, null, null, DateTimeOffset.UtcNow);

    public SchedulerJobStatus[] GetAll()
        => _statuses.Values.ToArray();

    public SchedulerJobStatus GetOne(string jobName)
        => _statuses.GetValueOrDefault(jobName)
           ?? new SchedulerJobStatus(jobName, SchedulerJobMode.Idle, null, null, DateTimeOffset.UtcNow);
}
```

### 7.3 Health endpoint exposure

`SchedulerHealthContributor` implements `ISystemHealthContributor` (already used by `SystemHealthService`):

```csharp
namespace MSOSync.App.Health;

public sealed class SchedulerHealthContributor(ISchedulerHealthReporter reporter)
    : ISystemHealthContributor
{
    public Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        var statuses = reporter.GetAll();
        var standbyJobs = statuses.Where(s => s.Mode == SchedulerJobMode.Standby).ToArray();

        var data = statuses.ToDictionary(
            s => s.JobName,
            s => (object)new { mode = s.Mode.ToString(), lockedSince = s.LockedSince, owner = s.LockOwner });

        // An instance where all scheduler jobs are on Standby is healthy — it means
        // another instance is the active scheduler. An instance with no statuses
        // registered at all is also healthy (first startup before first tick).
        var result = new HealthContribution(
            Name:    "Scheduler",
            Status:  HealthStatus.Healthy,
            Message: standbyJobs.Length == statuses.Length && statuses.Length > 0
                ? "This instance is scheduler standby — all jobs running on peer"
                : $"{statuses.Count(s => s.Mode == SchedulerJobMode.Running)} job(s) active on this instance",
            Data:    data);

        return Task.FromResult(result);
    }
}
```

### 7.4 Dedicated scheduler-status endpoint

A new controller action exposes the per-job detail without going through the health aggregator:

```
GET /api/v1/system/scheduler-status
```

Response shape:

```json
{
  "instanceId": "HOSTNAME:12345",
  "jobs": [
    {
      "jobName": "SyncJob",
      "mode": "Running",
      "lockOwner": "HOSTNAME:12345",
      "lockedSince": "2026-07-23T08:00:00Z",
      "lastUpdated": "2026-07-23T08:00:01Z"
    },
    {
      "jobName": "PullJob",
      "mode": "Standby",
      "lockOwner": null,
      "lockedSince": null,
      "lastUpdated": "2026-07-23T08:00:00Z"
    }
  ]
}
```

This endpoint is served from `MSOSync.Api` and injected with `ISchedulerHealthReporter` via DI. Authorization: `[Authorize(Roles = "Admin")]`.

---

## 8. Job Migration Guide

### 8.1 Jobs to update

| Job | Current lock mechanism | 2D.3 change |
|---|---|---|
| `SyncJob` | `IDatabaseLockProvider.TryAcquireAsync(LockNames.SyncEngine)` inside `RunTickAsync` | Remove direct lock call; wrap work in `SchedulerJobGuard.RunAsync` |
| `PullJob` | No lock | Add `SchedulerJobGuard.RunAsync` wrapping `PollAllAsync` |
| `PurgeJob` | `IDatabaseLockProvider.TryAcquireAsync(LockNames.PurgeEngine)` inside `RunPurgeAsync` | Remove direct lock call; wrap work in `SchedulerJobGuard.RunAsync` |
| `RetryJob` | `IDatabaseLockProvider.TryAcquireAsync(LockNames.RetryEngine)` inside `RunTickAsync` | Remove direct lock call; wrap work in `SchedulerJobGuard.RunAsync` |

### 8.2 Constructor changes per job

Each job adds two new constructor parameters:
- `ISchedulerLockFactory lockFactory`
- `ISchedulerHealthReporter health`

These are injected from DI — no manual instantiation.

### 8.3 LockNames cleanup

After all four jobs are migrated, `LockNames.SyncEngine`, `LockNames.RetryEngine`, and `LockNames.PurgeEngine` are unused by any job. They are **not deleted yet** — they are marked `[Obsolete]` in 2D.3 and removed in a follow-up cleanup task (2D.3.1) after a burn-in period confirming the new lock names work correctly.

### 8.4 Removed imports per job

After migration, each job removes its `using MSOSync.Persistence.Lock;` import (unless used elsewhere in the file).

---

## 9. Wiring (SyncSchedulerExtensions)

```csharp
public static IServiceCollection AddSyncScheduler(
    this IServiceCollection services,
    IConfiguration config)
{
    // Existing registrations unchanged
    services.Configure<HeartbeatOptions>(config.GetSection(HeartbeatOptions.Section));
    services.Configure<SyncOptions>(config.GetSection(SyncOptions.Section));

    // NEW in 2D.3
    services.Configure<SchedulerLockOptions>(config.GetSection(SchedulerLockOptions.Section));
    services.AddSingleton<ISchedulerHealthReporter, SchedulerHealthReporter>();
    services.AddSingleton<ISchedulerLockFactory, SchedulerLockFactory>();

    // Existing hosted services (unchanged)
    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());
    services.AddHostedService<SchedulerRecovery>();
    services.AddHostedService<SyncJob>();
    services.AddHostedService<RetryJob>();
    services.AddHostedService<PurgeJob>();
    services.AddHostedService<PullJob>();
    services.AddHostedService<HeartbeatWorker>();
    services.AddHostedService<ProbeWorker>();
    services.AddHostedService<ConnectivityEvaluator>();
    services.AddHostedService<DecommissionWorker>();
    services.AddHostedService<RollingOperationWorker>();
    services.Configure<MSOSync.Metadata.Options.ReplayOptions>(
        config.GetSection(MSOSync.Metadata.Options.ReplayOptions.Section));
    services.AddHostedService<ReplayWorker>();

    return services;
}
```

`SchedulerHealthContributor` is registered in `MSOSync.App` where `ISystemHealthContributor` registrations live:

```csharp
// In Program.cs or an App-layer extension
services.AddSingleton<ISystemHealthContributor, SchedulerHealthContributor>();
```

---

## 10. Configuration

New section in `appsettings.json`:

```json
{
  "Scheduler": {
    "Lock": {
      "TtlSeconds": 120,
      "RenewalIntervalSeconds": 10,
      "LockPrefix": "scheduler:"
    }
  }
}
```

**Invariant:** `TtlSeconds` must be at least `3 × RenewalIntervalSeconds`. Validated at startup via `IOptions` validation:

```csharp
services.AddOptions<SchedulerLockOptions>()
    .BindConfiguration(SchedulerLockOptions.Section)
    .Validate(o => o.TtlSeconds >= o.RenewalIntervalSeconds * 3,
        "Scheduler:Lock:TtlSeconds must be at least 3x RenewalIntervalSeconds")
    .ValidateOnStart();
```

---

## 11. Graceful Degradation

When only one instance is running:
- `TryAcquireAsync` always succeeds (lock row is null or expired).
- The renewal loop runs but is a no-op (just keeps resetting lock_time to UtcNow).
- `ISchedulerHealthReporter` shows all jobs as `Running`.
- Behavior is identical to current implementation — no performance penalty.

When the active instance crashes mid-job:
- The renewal loop stops.
- After `TtlSeconds` (default 120s), the lock expires and the next instance acquires it on the next tick.
- The gap is bounded: `TtlSeconds + max(jobInterval)`. For `SyncJob` with a 30s interval, worst case gap is ~150s.
- This is acceptable for a CE deployment. Enterprise HA with sub-second failover is Phase 2D.4+ scope.

---

## 12. Testing

### 12.1 Unit tests

**SchedulerLockFactory_TryAcquireAsync_Returns_Null_When_Lock_Held**
- Arrange: stub `IDatabaseLockProvider.TryAcquireAsync` to return null.
- Act: call `SchedulerLockFactory.TryAcquireAsync("SyncJob", ct)`.
- Assert: result is null; no renewal task started.

**SchedulerLockFactory_TryAcquireAsync_Returns_Lock_When_Free**
- Arrange: stub provider returns a non-null disposable.
- Act: call `TryAcquireAsync`.
- Assert: returns `ISchedulerLock` with correct `JobName`, `Owner`, `AcquiredAt`.

**SchedulerLockImpl_Renewal_Calls_RenewAsync_At_Interval**
- Arrange: fake `IDatabaseLockProvider` that records `RenewAsync` calls; `RenewalIntervalSeconds = 1`.
- Act: acquire lock, wait 2.5s.
- Assert: `RenewAsync` was called at least twice; no exceptions.

**SchedulerLockImpl_Dispose_Cancels_Renewal_And_Releases**
- Arrange: acquire lock; verify renewal is running.
- Act: call `DisposeAsync`.
- Assert: renewal task completed; `ReleaseAsync` was called exactly once.

**SchedulerJobGuard_Skips_Work_When_Lock_Null**
- Arrange: `ISchedulerLockFactory` returns null.
- Act: `SchedulerJobGuard.RunAsync`.
- Assert: work lambda was never invoked; `health.RecordStandby` was called.

**SchedulerJobGuard_Runs_Work_When_Lock_Acquired**
- Arrange: factory returns a valid lock.
- Act: `SchedulerJobGuard.RunAsync` with a flag-setting lambda.
- Assert: flag was set; `health.RecordRunning` called before work; `health.RecordIdle` called after.

**SchedulerHealthReporter_Returns_Correct_Mode_Per_Job**
- Arrange/Act: call `RecordRunning`, `RecordStandby`, `RecordIdle` for different jobs.
- Assert: `GetAll()` returns correct `SchedulerJobMode` per job.

### 12.2 Integration tests — dual-instance simulation

These tests use two `ISchedulerLockFactory` instances sharing the same SQL `AppDbContext` (pointing to the test database), simulating two hub instances.

**Only_One_Instance_Runs_SyncJob_Per_Tick**
- Arrange: two `SchedulerLockFactory` instances (`A`, `B`) against the same `sync_lock` table; seed `scheduler:SyncJob` row.
- Act: both call `TryAcquireAsync("SyncJob", ct)` concurrently.
- Assert: exactly one returns non-null; the other returns null. Both verify with `health.GetOne("SyncJob").Mode`.

**Second_Instance_Picks_Up_Lock_After_First_Releases**
- Arrange: instance A acquires `scheduler:SyncJob`; instance B attempts and gets null.
- Act: instance A disposes its lock.
- Act: instance B retries `TryAcquireAsync`.
- Assert: instance B acquires the lock on the retry.

**Lock_Survives_Beyond_Default_Stale_Timeout_With_Renewal**
- Arrange: `TtlSeconds = 30`, `RenewalIntervalSeconds = 5`. Acquire lock.
- Act: wait 35 seconds (which would expire the old 10-minute timeout only in the database sense; here we test our custom TTL by stubbing time or using the real DB with a short TTL).
- Assert: lock is still held by the acquiring instance (renewal kept it alive); a second `TryAcquireAsync` returns null.

**Lock_Expires_After_Crash_Without_Renewal**
- Arrange: acquire lock; manually cancel the `SchedulerLockImpl._renewalCts` to simulate a crashed renewal loop without calling `DisposeAsync`.
- Act: wait `TtlSeconds + 1` seconds.
- Act: second instance calls `TryAcquireAsync`.
- Assert: second instance acquires the lock (stale timeout fires correctly).

### 12.3 Scheduler-status endpoint tests

**GET /api/v1/system/scheduler-status — Returns Job Modes**
- Arrange: inject `ISchedulerHealthReporter` with known state.
- Act: GET the endpoint.
- Assert: response body contains correct `mode` and `jobName` for each registered job.

**GET /health — Reflects Standby State Without Degraded**
- Arrange: set all four jobs to Standby.
- Act: GET `/health`.
- Assert: overall health is `Healthy` (not Degraded); message contains "standby".

---

## 13. Global Constraints

1. **No new DB migrations.** The `sync_lock` table schema (`lock_name`, `lock_owner`, `lock_time`, `scope`) is unchanged. New lock rows are inserted as seed data at startup — no `ALTER TABLE` statements.

2. **Graceful single-instance behavior.** With one hub instance, all locks are always acquired and behavior is identical to current. No configuration change required to switch from single-instance to multi-instance.

3. **Cancellation-aware throughout.** The `CancellationToken` passed to `SchedulerJobGuard.RunAsync` is threaded into the work lambda and into the renewal loop. Cancellation (application shutdown) stops both the job work and the renewal task cleanly.

4. **Lock prefix convention is stable.** All scheduler lock names use `scheduler:{JobName}` (no tenant scoping at this layer). Multi-tenant lock isolation, if required, is a 2D.4+ concern.

5. **Worker registry untouched.** `IWorkerStatusRegistry` calls (`RecordTickStart`, `RecordTickComplete`, `RecordTickFailed`) remain in each job's outer tick loop, not inside the guard. This means skipped ticks (Standby) still register as `RecordTickComplete`, preventing false health warnings from `WorkerStatusRegistry`.

6. **`ISchedulerLock` in `MSOSync.Scheduler` only.** The interface, factory, and implementation do not leak into `MSOSync.Common` or `MSOSync.Persistence`. Health reporting integration touches `MSOSync.App` only through `ISchedulerHealthReporter` (defined in `MSOSync.Scheduler`), which `MSOSync.App` takes as a DI dependency.

7. **2D.2 compatibility.** `ISchedulerLockFactory` is implemented against `IDatabaseLockProvider` for the SQL provider. When `IDistributedLockService` from 2D.2 (Redis) becomes available, `SchedulerLockFactory` accepts it as an optional dependency and prefers it over the database provider. This is a one-line DI change; job code does not change.

8. **No attribute-based guard.** The guard is explicit code in each job's `RunTickAsync` / `RunPurgeAsync`. This is preferred over a `[DistributedJob]` attribute approach because it keeps lock lifecycle visible in the call stack, works with the existing `IWorkerStatusRegistry` pattern, and does not require source generators or middleware.

9. **Renewal failure is non-fatal.** If a `RenewAsync` call fails (transient DB error), the renewal loop logs a warning and retries on the next interval. It does not cancel the job. If renewals fail continuously until the TTL expires, the lock is stolen by the next instance — which is the safe fallback (job work is re-run, not dropped).

10. **Observability.** All lock acquisition, renewal, and release events are logged at `Debug` level. Renewal failures log at `Warning`. Lock contention (null result from `TryAcquireAsync`) logs at `Debug` per tick — not `Information`, to avoid flooding logs in steady-state multi-instance operation.
