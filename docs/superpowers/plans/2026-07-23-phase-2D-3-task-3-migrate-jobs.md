# Task 3 — Extend IDatabaseLockProvider + Migrate Jobs + Seed Rows + Wire Extensions

**Phase:** 2D.3
**File:** `2026-07-23-phase-2D-3-task-3-migrate-jobs.md`
**Depends on:** Task 1 (SchedulerJobGuard, ISchedulerLockFactory) and Task 2 (ISchedulerHealthReporter)

---

## Overview

1. Extend `IDatabaseLockProvider` with `RenewAsync` and `ReleaseAsync`.
2. Implement those methods in `DatabaseLockProvider`.
3. Add a startup seed that inserts the four `scheduler:*` lock rows idempotently.
4. Update `SyncJob`, `PullJob`, `PurgeJob`, `RetryJob` to use `SchedulerJobGuard.RunAsync`.
5. Mark old `LockNames` constants `[Obsolete]`.
6. Update `SyncSchedulerExtensions` to register new services and validate `SchedulerLockOptions`.
7. Add `Scheduler:Lock` section to `appsettings.json`.
8. Update all four existing job unit test classes to use mocked `ISchedulerLockFactory` + `ISchedulerHealthReporter` instead of `IDatabaseLockProvider`.

---

## Step 1 — Extend `IDatabaseLockProvider`

**File:** `src/MSOSync.Persistence/Lock/IDatabaseLockProvider.cs`

Current content:
```csharp
public interface IDatabaseLockProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct = default);
}
```

- [ ] Replace with:

```csharp
namespace MSOSync.Persistence.Lock;

public interface IDatabaseLockProvider
{
    /// <summary>
    /// Attempts to acquire the named lock row. Returns a disposable lease on success,
    /// or null if the row is held by another owner within the stale timeout window.
    /// </summary>
    Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct = default);

    /// <summary>
    /// Resets <c>lock_time</c> to GETUTCDATE() for the given owner row,
    /// extending the stale timeout window. No-op if the row is not owned by <paramref name="owner"/>.
    /// Added in Phase 2D.3 for scheduler heartbeat renewal.
    /// </summary>
    Task RenewAsync(string lockName, string owner, CancellationToken ct = default);

    /// <summary>
    /// Clears <c>lock_owner</c> and <c>lock_time</c> for the given owner row,
    /// making it immediately available to the next caller.
    /// Does not accept a CancellationToken — release must complete even at shutdown.
    /// Added in Phase 2D.3 for scheduler lock release.
    /// </summary>
    Task ReleaseAsync(string lockName, string owner);
}
```

---

## Step 2 — Implement `RenewAsync` and `ReleaseAsync` in `DatabaseLockProvider`

**File:** `src/MSOSync.Persistence/Lock/DatabaseLockProvider.cs`

Current full content of the class:
```csharp
public sealed class DatabaseLockProvider(AppDbContext db) : IDatabaseLockProvider
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct = default)
    {
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE() " +
            "WHERE lock_name = {1} " +
            "  AND (lock_owner IS NULL OR lock_time < DATEADD(MINUTE, -10, GETUTCDATE()))",
            new object[] { owner, lockName },
            ct);

        return rows == 1 ? new DatabaseLockLease(db, Schema, lockName, owner) : null;
    }
}
```

- [ ] Add `RenewAsync` and `ReleaseAsync` methods and a static seed helper. Replace the file with:

```csharp
using Microsoft.EntityFrameworkCore;

namespace MSOSync.Persistence.Lock;

public sealed class DatabaseLockProvider(AppDbContext db) : IDatabaseLockProvider
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task<IAsyncDisposable?> TryAcquireAsync(string lockName, CancellationToken ct = default)
    {
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE() " +
            "WHERE lock_name = {1} " +
            "  AND (lock_owner IS NULL OR lock_time < DATEADD(MINUTE, -10, GETUTCDATE()))",
            new object[] { owner, lockName },
            ct);

        return rows == 1 ? new DatabaseLockLease(db, Schema, lockName, owner) : null;
    }

    /// <summary>
    /// Resets lock_time to GETUTCDATE() for the row owned by <paramref name="owner"/>,
    /// preventing stale-timeout expiry while the job is still running.
    /// </summary>
    public async Task RenewAsync(string lockName, string owner, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_time = GETUTCDATE() " +
            "WHERE lock_name = {0} AND lock_owner = {1}",
            new object[] { lockName, owner },
            ct);
    }

    /// <summary>
    /// Releases the lock by clearing lock_owner and lock_time,
    /// making the row immediately acquirable by the next instance.
    /// Uses CancellationToken.None so release always completes even at shutdown.
    /// </summary>
    public async Task ReleaseAsync(string lockName, string owner)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL " +
            "WHERE lock_name = {0} AND lock_owner = {1}",
            new object[] { lockName, owner },
            CancellationToken.None);
    }

    /// <summary>
    /// Seeds the four scheduler lock rows into sync_lock if they do not already exist.
    /// Called once at startup from <see cref="SchedulerLockSeeder"/>.
    /// No schema changes — rows are data-only inserts.
    /// </summary>
    public static async Task SeedSchedulerLocksAsync(AppDbContext db, CancellationToken ct = default)
    {
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

        var jobs = new[]
        {
            "scheduler:SyncJob",
            "scheduler:PullJob",
            "scheduler:PurgeJob",
            "scheduler:RetryJob"
        };

        foreach (var lockName in jobs)
        {
            await db.Database.ExecuteSqlRawAsync(
                $"IF NOT EXISTS (SELECT 1 FROM [{schema}].[sync_lock] WHERE lock_name = {{0}}) " +
                $"INSERT INTO [{schema}].[sync_lock] (lock_name, lock_owner, lock_time, scope) " +
                "VALUES ({0}, NULL, NULL, 0)",
                new object[] { lockName },
                ct);
        }
    }
}
```

---

## Step 3 — Create `SchedulerLockSeeder` startup service

**File:** `src/MSOSync.Scheduler/Internal/SchedulerLockSeeder.cs`

This runs once at host startup to ensure the four lock rows exist before any job fires.

- [ ] Create the file:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Inserts the four "scheduler:*" rows into sync_lock at startup if they do not exist.
/// No schema change — data seed only.
/// </summary>
internal sealed class SchedulerLockSeeder(
    IServiceScopeFactory    scopeFactory,
    ILogger<SchedulerLockSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        try
        {
            await DatabaseLockProvider.SeedSchedulerLocksAsync(db, cancellationToken);
            logger.LogInformation("SchedulerLockSeeder: scheduler lock rows seeded");
        }
        catch (Exception ex)
        {
            // Non-fatal at startup — rows may already exist or DB may not be ready yet.
            // Jobs will fail to acquire locks on first tick if rows are missing.
            logger.LogWarning(ex,
                "SchedulerLockSeeder: failed to seed scheduler lock rows — jobs may not distribute correctly");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

---

## Step 4 — Mark old `LockNames` constants `[Obsolete]`

**File:** `src/MSOSync.Persistence/Lock/LockNames.cs`

Current content:
```csharp
public static class LockNames
{
    public const string SyncEngine  = "SYNC_ENGINE";
    public const string RetryEngine = "RETRY_ENGINE";
    public const string PurgeEngine = "PURGE_ENGINE";
}
```

- [ ] Replace with:

```csharp
namespace MSOSync.Persistence.Lock;

public static class LockNames
{
    /// <summary>Legacy lock name for SyncJob. Superseded by "scheduler:SyncJob" in Phase 2D.3.</summary>
    [Obsolete("Use SchedulerJobGuard with ISchedulerLockFactory. Legacy lock name kept for burn-in period. Remove in 2D.3.1.")]
    public const string SyncEngine  = "SYNC_ENGINE";

    /// <summary>Legacy lock name for RetryJob. Superseded by "scheduler:RetryJob" in Phase 2D.3.</summary>
    [Obsolete("Use SchedulerJobGuard with ISchedulerLockFactory. Legacy lock name kept for burn-in period. Remove in 2D.3.1.")]
    public const string RetryEngine = "RETRY_ENGINE";

    /// <summary>Legacy lock name for PurgeJob. Superseded by "scheduler:PurgeJob" in Phase 2D.3.</summary>
    [Obsolete("Use SchedulerJobGuard with ISchedulerLockFactory. Legacy lock name kept for burn-in period. Remove in 2D.3.1.")]
    public const string PurgeEngine = "PURGE_ENGINE";
}
```

---

## Step 5 — Migrate `SyncJob`

**File:** `src/MSOSync.Scheduler/SyncJob.cs`

Key changes:
- Add `ISchedulerLockFactory lockFactory` and `ISchedulerHealthReporter health` constructor parameters.
- Remove the inline `IDatabaseLockProvider.TryAcquireAsync` call from `RunTickAsync`.
- Wrap engine work in `SchedulerJobGuard.RunAsync`.
- Move `RecordTickComplete` outside the guard so skipped ticks still complete cleanly.
- Remove `using MSOSync.Persistence.Lock;` import.

- [ ] Replace the file with:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Workers;
using MSOSync.Engine;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory     scopeFactory,
    IOptions<SyncOptions>    syncOptions,
    ISchedulerLockFactory    lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry    registry,
    ILogger<SyncJob>         logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(nameof(SyncJob), TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunTickAsync(ct);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
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
                    await using var scope  = scopeFactory.CreateAsyncScope();
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
```

---

## Step 6 — Migrate `PullJob`

**File:** `src/MSOSync.Scheduler/PullJob.cs`

Key changes:
- Add `ISchedulerLockFactory lockFactory` and `ISchedulerHealthReporter health` constructor parameters.
- Wrap `PollAllAsync` in `SchedulerJobGuard.RunAsync` inside `RunTickAsync`.
- `IsPullEnabledAsync` check remains before the guard (mode check, not work).
- No lock import needed (PullJob had none).

- [ ] Edit the constructor and `RunTickAsync` method. Replace the constructor block and `RunTickAsync`:

Constructor — change from:
```csharp
public sealed class PullJob(
    IServiceScopeFactory     scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IOptions<SyncOptions>    syncOptions,
    IWorkerStatusRegistry    registry,
    ILogger<PullJob>         logger) : BackgroundService
```

To:
```csharp
public sealed class PullJob(
    IServiceScopeFactory     scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IOptions<SyncOptions>    syncOptions,
    ISchedulerLockFactory    lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry    registry,
    ILogger<PullJob>         logger) : BackgroundService
```

`RunTickAsync` — change from:
```csharp
internal async Task RunTickAsync(string localNodeId, CancellationToken ct)
{
    registry.RecordTickStart(nameof(PullJob));
    try
    {
        await PollAllAsync(localNodeId, ct);
        registry.RecordTickComplete(nameof(PullJob));
    }
    catch (Exception ex) when (!ct.IsCancellationRequested)
    {
        registry.RecordTickFailed(nameof(PullJob), ex);
        logger.LogError(ex, "PullJob tick failed");
    }
}
```

To:
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

- [ ] Apply the constructor change to `src/MSOSync.Scheduler/PullJob.cs`.
- [ ] Apply the `RunTickAsync` change to `src/MSOSync.Scheduler/PullJob.cs`.

---

## Step 7 — Migrate `PurgeJob`

**File:** `src/MSOSync.Scheduler/PurgeJob.cs`

Key changes:
- Add `ISchedulerLockFactory lockFactory` and `ISchedulerHealthReporter health` constructor parameters.
- Remove `using MSOSync.Persistence.Lock;`.
- In `RunPurgeAsync`, remove the inline `lockProvider.TryAcquireAsync(LockNames.PurgeEngine)` guard.
- Wrap `eventPurger` + `batchPurger` calls in `SchedulerJobGuard.RunAsync`.
- `registry.RecordTickStart/Complete/Failed` stay in the outer `ExecuteAsync` loop (unchanged).

- [ ] Replace `PurgeJob.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Event;

namespace MSOSync.Scheduler;

public sealed class PurgeJob(
    IServiceScopeFactory     scopeFactory,
    IClock                   clock,
    ISchedulerLockFactory    lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry    registry,
    ILogger<PurgeJob>        logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Task.Delay used intentionally — PurgeJob targets wall-clock 02:00 UTC, not a fixed interval
        registry.Register(nameof(PurgeJob), TimeSpan.FromHours(24));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = TimeUntilNextFire();
            logger.LogDebug("PurgeJob sleeping {Delay} until next 02:00 UTC", delay);

            try { await Task.Delay(delay, ct); }
            catch (OperationCanceledException) { break; }

            registry.RecordTickStart(nameof(PurgeJob));
            try
            {
                await RunPurgeAsync(ct);
                registry.RecordTickComplete(nameof(PurgeJob));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(PurgeJob), ex);
                logger.LogError(ex, "PurgeJob failed");
            }
        }
    }

    internal async Task RunPurgeAsync(CancellationToken ct)
    {
        await SchedulerJobGuard.RunAsync(
            nameof(PurgeJob),
            lockFactory,
            health,
            logger,
            async innerCt =>
            {
                await using var scope       = scopeFactory.CreateAsyncScope();
                var eventPurger  = scope.ServiceProvider.GetRequiredService<IEventPurger>();
                var batchPurger  = scope.ServiceProvider.GetRequiredService<BatchPurger>();

                var events  = await eventPurger.PurgeAsync(innerCt);
                var batches = await batchPurger.PurgeAsync(innerCt);
                logger.LogInformation(
                    "PurgeJob: deleted {Events} events, {Batches} batches", events, batches);
            },
            ct);
    }

    internal TimeSpan TimeUntilNextFire()
    {
        var now  = clock.UtcNow;
        var next = now.Date.AddHours(2);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
```

---

## Step 8 — Migrate `RetryJob`

**File:** `src/MSOSync.Scheduler/RetryJob.cs`

Key changes:
- Add `ISchedulerLockFactory lockFactory` and `ISchedulerHealthReporter health` constructor parameters.
- Remove `using MSOSync.Persistence.Lock;`.
- Remove inline `lockProvider.TryAcquireAsync(LockNames.RetryEngine)`.
- Wrap `processor.ProcessAsync` in `SchedulerJobGuard.RunAsync`.

- [ ] Replace `RetryJob.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common.Workers;

namespace MSOSync.Scheduler;

public sealed class RetryJob(
    IServiceScopeFactory     scopeFactory,
    ISchedulerLockFactory    lockFactory,
    ISchedulerHealthReporter health,
    IWorkerStatusRegistry    registry,
    ILogger<RetryJob>        logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // 5-minute fixed interval — retry cadence is not a tuneable operational parameter
        registry.Register(nameof(RetryJob), TimeSpan.FromMinutes(5));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));

        while (await timer.WaitForNextTickAsync(ct))
        {
            await RunTickAsync(ct);
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        registry.RecordTickStart(nameof(RetryJob));
        try
        {
            await SchedulerJobGuard.RunAsync(
                nameof(RetryJob),
                lockFactory,
                health,
                logger,
                async innerCt =>
                {
                    await using var scope     = scopeFactory.CreateAsyncScope();
                    var processor = scope.ServiceProvider.GetRequiredService<RetryProcessor>();
                    var count     = await processor.ProcessAsync(innerCt);
                    if (count > 0)
                        logger.LogInformation("RetryJob queued {Count} batches for retry", count);
                },
                ct);

            registry.RecordTickComplete(nameof(RetryJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(RetryJob), ex);
            logger.LogError(ex, "RetryJob failed");
        }
    }
}
```

---

## Step 9 — Update `SyncSchedulerExtensions`

**File:** `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`

- [ ] Replace with:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Scheduler.Internal;
using MediatR;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration config)
    {
        // Existing options
        services.Configure<HeartbeatOptions>(config.GetSection(HeartbeatOptions.Section));
        services.Configure<SyncOptions>(config.GetSection(SyncOptions.Section));

        // NEW in 2D.3: Distributed scheduler lock support
        services.AddOptions<SchedulerLockOptions>()
            .BindConfiguration(SchedulerLockOptions.Section)
            .Validate(
                o => o.TtlSeconds >= o.RenewalIntervalSeconds * 3,
                "Scheduler:Lock:TtlSeconds must be at least 3x RenewalIntervalSeconds")
            .ValidateOnStart();

        services.AddSingleton<ISchedulerHealthReporter, SchedulerHealthReporter>();
        services.AddSingleton<ISchedulerLockFactory, SchedulerLockFactory>();

        // Seed scheduler lock rows at startup
        services.AddHostedService<SchedulerLockSeeder>();

        // Existing MediatR + hosted services (unchanged)
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
}
```

Note: `SchedulerLockFactory` is internal but registered via `AddSingleton<ISchedulerLockFactory, SchedulerLockFactory>()` — this works because registration is in the same assembly (`MSOSync.Scheduler`).

---

## Step 10 — Add `Scheduler:Lock` config section to `appsettings.json`

**File:** `src/MSOSync.App/appsettings.json` (or project root `appsettings.json`)

- [ ] Find the existing `appsettings.json` for the App project and add:

```json
"Scheduler": {
  "Lock": {
    "TtlSeconds": 120,
    "RenewalIntervalSeconds": 10,
    "LockPrefix": "scheduler:"
  }
}
```

Locate the file with:
```
Glob: src/MSOSync.App/appsettings.json
```
If not present, check project root. Add the `Scheduler` block at the same level as other top-level sections (e.g., next to `"Sync"`, `"Heartbeat"`).

---

## Step 11 — Update `SyncJobTests`

**File:** `tests/MSOSync.SchedulerTests/SyncJobTests.cs`

The test currently mocks `IDatabaseLockProvider` and resolves it from a service scope. After migration, `SyncJob` no longer uses `IDatabaseLockProvider` — it uses `ISchedulerLockFactory` and `ISchedulerHealthReporter`.

- [ ] Replace the file with:

```csharp
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Scheduler;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SyncJobTests
{
    private readonly Mock<ISchedulerLockFactory>    _lockFactory   = new();
    private readonly Mock<ISchedulerHealthReporter> _health        = new();
    private readonly Mock<IWorkerStatusRegistry>    _registry      = new();
    private readonly Mock<ITriggerDriftDetector>    _driftDetector = new();
    private readonly Mock<IEventReader>             _eventReader   = new();
    private readonly Mock<IRoutingService>          _routing       = new();
    private readonly Mock<IBatchCreator>            _batchCreator  = new();
    private readonly Mock<ITransportService>        _transport     = new();
    private readonly Mock<IMediator>                _mediator      = new();
    private readonly Mock<IClock>                   _clock         = new();

    private SyncJob BuildJob()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new SyncEngine(
            _driftDetector.Object, _eventReader.Object, _routing.Object,
            _batchCreator.Object, _transport.Object, _mediator.Object,
            _clock.Object, NullLogger<SyncEngine>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new SyncJob(
            scopeFactory,
            Options.Create(new SyncOptions()),
            _lockFactory.Object,
            _health.Object,
            _registry.Object,
            NullLogger<SyncJob>.Instance);
    }

    [Fact]
    public async Task RunTick_skips_engine_when_lock_not_acquired()
    {
        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(SyncJob), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISchedulerLock?)null);

        await BuildJob().RunTickAsync(CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(x => x.RecordTickStart(nameof(SyncJob), TickTrigger.Scheduled), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
        _health.Verify(x => x.RecordStandby(nameof(SyncJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_runs_engine_when_lock_acquired()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns(nameof(SyncJob));
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(SyncJob), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SyncDataEvent>());

        await BuildJob().RunTickAsync(CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
        _health.Verify(
            x => x.RecordRunning(nameof(SyncJob), "HOST:1", It.IsAny<DateTimeOffset>()),
            Times.Once);
        _health.Verify(x => x.RecordIdle(nameof(SyncJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_records_failure_when_engine_throws()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns(nameof(SyncJob));
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(SyncJob), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await BuildJob().RunTickAsync(CancellationToken.None);

        _registry.Verify(
            x => x.RecordTickFailed(nameof(SyncJob), It.IsAny<InvalidOperationException>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Never);
    }
}
```

---

## Step 12 — Update `PurgeJobTests`

**File:** `tests/MSOSync.SchedulerTests/PurgeJobTests.cs`

- [ ] Replace with:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Event;
using MSOSync.Persistence;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class PurgeJobTests
{
    private readonly Mock<ISchedulerLockFactory>    _lockFactory  = new();
    private readonly Mock<ISchedulerHealthReporter> _health       = new();
    private readonly Mock<IWorkerStatusRegistry>    _registry     = new();
    private readonly Mock<IEventPurger>             _eventPurger  = new();
    private readonly Mock<IClock>                   _clock        = new();

    private PurgeJob BuildJob()
    {
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

        var services = new ServiceCollection();
        services.AddScoped(_ => _eventPurger.Object);
        services.AddScoped(_ => new BatchPurger(
            new AppDbContext(dbOptions), _clock.Object, NullLogger<BatchPurger>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new PurgeJob(
            scopeFactory, _clock.Object,
            _lockFactory.Object, _health.Object,
            _registry.Object, NullLogger<PurgeJob>.Instance);
    }

    [Fact]
    public async Task RunPurge_skips_purgers_when_lock_not_acquired()
    {
        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(PurgeJob), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISchedulerLock?)null);

        await BuildJob().RunPurgeAsync(CancellationToken.None);

        _eventPurger.Verify(x => x.PurgeAsync(It.IsAny<CancellationToken>()), Times.Never);
        _health.Verify(x => x.RecordStandby(nameof(PurgeJob)), Times.Once);
    }

    [Fact]
    public async Task RunPurge_calls_purgers_when_lock_acquired()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns(nameof(PurgeJob));
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync(nameof(PurgeJob), It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);
        _eventPurger
            .Setup(x => x.PurgeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);

        await BuildJob().RunPurgeAsync(CancellationToken.None);

        _eventPurger.Verify(x => x.PurgeAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void TimeUntilNextFire_targets_today_when_before_0200_utc()
    {
        _clock.Setup(x => x.UtcNow).Returns(new DateTime(2026, 7, 21, 0, 30, 0, DateTimeKind.Utc));

        var delay = BuildJob().TimeUntilNextFire();

        delay.Should().Be(TimeSpan.FromMinutes(90));
    }

    [Fact]
    public void TimeUntilNextFire_targets_tomorrow_when_after_0200_utc()
    {
        _clock.Setup(x => x.UtcNow).Returns(new DateTime(2026, 7, 21, 3, 0, 0, DateTimeKind.Utc));

        var delay = BuildJob().TimeUntilNextFire();

        delay.Should().Be(TimeSpan.FromHours(23));
    }
}
```

---

## Step 13 — Update `RetryJobTests`

**File:** `tests/MSOSync.SchedulerTests/RetryJobTests.cs`

- [ ] Read the existing file then update constructor and lock setup to use `ISchedulerLockFactory` + `ISchedulerHealthReporter`.
- [ ] Replace `_lockProvider.Setup(x => x.TryAcquireAsync(LockNames.RetryEngine, ...))` with `_lockFactory.Setup(x => x.TryAcquireAsync(nameof(RetryJob), ...))`.
- [ ] Remove `Mock<IDatabaseLockProvider>` field; add `Mock<ISchedulerLockFactory> _lockFactory` and `Mock<ISchedulerHealthReporter> _health`.
- [ ] Update `BuildJob()` to pass `_lockFactory.Object`, `_health.Object` to `RetryJob` constructor.
- [ ] Remove `services.AddScoped(_ => _lockProvider.Object)` from scope setup.
- [ ] Verify lock-not-acquired test asserts `_health.Verify(x => x.RecordStandby(nameof(RetryJob)), Times.Once)`.

---

## Step 14 — Update `PullJobTests`

**File:** `tests/MSOSync.SchedulerTests/PullJobTests.cs`

- [ ] Read the existing file first to see current mock setup.
- [ ] `PullJob` previously had no lock — tests for `RunTickAsync` don't mock a lock provider. After migration, the guard wraps `PollAllAsync`. Add `Mock<ISchedulerLockFactory> _lockFactory` and `Mock<ISchedulerHealthReporter> _health`.
- [ ] In tests that expect work to execute: set up `_lockFactory` to return a valid `ISchedulerLock` mock.
- [ ] In "standby" tests (new): set up factory to return `null`; assert PollAllAsync-equivalent work is not called.
- [ ] Update `BuildJob()` to include `_lockFactory.Object`, `_health.Object` in `PullJob` constructor.

---

## Step 15 — Verify Build and Tests

- [ ] `dotnet build src/MSOSync.Scheduler/MSOSync.Scheduler.csproj` — 0 errors; Obsolete warnings for `LockNames` are expected (CS0618 in test files that still reference them until step 13-14 complete).
- [ ] `dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj` — 0 errors.
- [ ] `dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj` — all tests pass.
- [ ] `dotnet build MSOSync.sln` — 0 errors.

---

## Acceptance Criteria

- `IDatabaseLockProvider` has three methods: `TryAcquireAsync`, `RenewAsync`, `ReleaseAsync`.
- `DatabaseLockProvider` implements all three; `SeedSchedulerLocksAsync` is a public static method.
- `SchedulerLockSeeder` is an `internal IHostedService` in `MSOSync.Scheduler.Internal`.
- All four job classes no longer import or use `IDatabaseLockProvider` or `LockNames` directly.
- Old `LockNames` constants carry `[Obsolete]` attributes.
- `SyncSchedulerExtensions.AddSyncScheduler` registers `ISchedulerHealthReporter`, `ISchedulerLockFactory`, `SchedulerLockSeeder`, and validates `SchedulerLockOptions`.
- All existing scheduler unit tests pass with updated mocks.
- `appsettings.json` contains `Scheduler:Lock` block.
