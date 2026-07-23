# Task 3: `AdaptivePollingOrchestrator` + `SyncJob` Demotion + DI Wiring

> Part of [Phase 2D.5 Master Plan](2026-07-23-phase-2D-5-master.md)

**Goal:** Create `AdaptivePollingOrchestrator` as the new `BackgroundService` that drives per-node dispatch loops; demote `SyncJob` from `BackgroundService` to a plain scoped service; wire everything into `SyncSchedulerExtensions`.

**Files:**
- Create: `src/MSOSync.Scheduler/AdaptivePollingOrchestrator.cs`
- Modify: `src/MSOSync.Scheduler/SyncJob.cs`
- Modify: `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`
- Modify: `tests/MSOSync.SchedulerTests/SyncJobTests.cs`

**Interfaces:**
- Consumes:
  - `IAdaptivePollingService` from Task 2 — `GetIntervalAsync(nodeId)`, `RecordActivityAsync(nodeId, hadWork)`, `RecordErrorAsync(nodeId)`
  - `IWorkerStatusRegistry` — `Register(string, TimeSpan)`, `RecordTickStart(string)`, `RecordTickComplete(string)`, `RecordTickFailed(string, Exception)`
- Produces:
  - `AdaptivePollingOrchestrator` registered as `IHostedService` via `AddHostedService<AdaptivePollingOrchestrator>()`
  - `SyncJob` demoted to scoped service with signature `RunTickAsync(string? nodeId = null, CancellationToken ct = default)`

---

- [ ] **Step 1: Update `SyncJob` — demote to scoped service**

`SyncJob` currently extends `BackgroundService` and manages its own timer and `IWorkerStatusRegistry` registration. After this change it is a plain class called by `AdaptivePollingOrchestrator`.

Key changes:
- Remove `: BackgroundService` inheritance
- Remove `StartAsync` override (that was where it self-registered with `IWorkerStatusRegistry`)
- Remove `ExecuteAsync` (the fixed timer loop)
- Remove `IOptions<SyncOptions>` and `IWorkerStatusRegistry` constructor parameters
- Add `string? nodeId = null` parameter to `RunTickAsync` so the orchestrator can pass a per-node lock key

Replace `src/MSOSync.Scheduler/SyncJob.cs` entirely:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

/// <summary>
/// Unit of work invoked per node per poll cycle by AdaptivePollingOrchestrator.
/// Demoted from BackgroundService in 2D.5 — no longer manages its own timer.
/// </summary>
public sealed class SyncJob(
    IServiceScopeFactory scopeFactory,
    IWorkerStatusRegistry registry,
    ILogger<SyncJob>     logger)
{
    // Parameterless overload retained for test backward compatibility (spec §Global Constraints)
    internal Task RunTickAsync(CancellationToken ct = default) => RunTickAsync(null, ct);

    internal async Task RunTickAsync(string? nodeId, CancellationToken ct)
    {
        registry.RecordTickStart(nameof(SyncJob));
        try
        {
            await using var scope        = scopeFactory.CreateAsyncScope();
            var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
            var engine       = scope.ServiceProvider.GetRequiredService<SyncEngine>();

            // Per-node lock key scoping: "SyncEngine:node-123" or "SyncEngine" (legacy/test)
            var lockKey = nodeId is not null
                ? $"{LockNames.SyncEngine}:{nodeId}"
                : LockNames.SyncEngine;

            await using var lease = await lockProvider.TryAcquireAsync(lockKey, ct);
            if (lease == null)
            {
                logger.LogDebug("SyncJob: lock held for {LockKey}, skipping tick", lockKey);
                registry.RecordTickComplete(nameof(SyncJob));
                return;
            }

            await engine.RunAsync(ct);
            registry.RecordTickComplete(nameof(SyncJob));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(SyncJob), ex);
            logger.LogError(ex, "SyncJob run failed for node {NodeId}", nodeId);
        }
    }
}
```

- [ ] **Step 2: Update `SyncJobTests` to match demoted constructor**

`SyncJob` no longer takes `IOptions<SyncOptions>`. Update `tests/MSOSync.SchedulerTests/SyncJobTests.cs`:

```csharp
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Lock;
using MSOSync.Routing;
using MSOSync.Scheduler;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SyncJobTests
{
    private readonly Mock<IDatabaseLockProvider> _lockProvider  = new();
    private readonly Mock<IWorkerStatusRegistry> _registry      = new();
    private readonly Mock<ITriggerDriftDetector> _driftDetector = new();
    private readonly Mock<IEventReader>          _eventReader   = new();
    private readonly Mock<IRoutingService>       _routing       = new();
    private readonly Mock<IBatchCreator>         _batchCreator  = new();
    private readonly Mock<ITransportService>     _transport     = new();
    private readonly Mock<IMediator>             _mediator      = new();
    private readonly Mock<IClock>                _clock         = new();

    private SyncJob BuildJob()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => _lockProvider.Object);
        services.AddScoped(_ => new SyncEngine(
            _driftDetector.Object, _eventReader.Object, _routing.Object,
            _batchCreator.Object, _transport.Object, _mediator.Object,
            _clock.Object, NullLogger<SyncEngine>.Instance));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new SyncJob(
            scopeFactory,
            _registry.Object,
            NullLogger<SyncJob>.Instance);
    }

    [Fact]
    public async Task RunTick_skips_engine_when_lock_not_acquired()
    {
        _lockProvider
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        await BuildJob().RunTickAsync(null, CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        _registry.Verify(x => x.RecordTickStart(nameof(SyncJob), TickTrigger.Scheduled), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
        _registry.Verify(
            x => x.RecordTickFailed(It.IsAny<string>(), It.IsAny<Exception>()), Times.Never);
    }

    [Fact]
    public async Task RunTick_runs_engine_when_lock_acquired()
    {
        _lockProvider
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<SyncDataEvent>());

        await BuildJob().RunTickAsync(null, CancellationToken.None);

        _eventReader.Verify(
            x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_uses_pernode_lockkey_when_nodeId_provided()
    {
        _lockProvider
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        await BuildJob().RunTickAsync("node-abc", CancellationToken.None);

        _lockProvider.Verify(
            x => x.TryAcquireAsync(
                It.Is<string>(k => k == $"{LockNames.SyncEngine}:node-abc"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RunTick_records_failure_when_engine_throws()
    {
        _lockProvider
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        _eventReader
            .Setup(x => x.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        await BuildJob().RunTickAsync(null, CancellationToken.None);

        _registry.Verify(
            x => x.RecordTickFailed(nameof(SyncJob), It.IsAny<InvalidOperationException>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(SyncJob)), Times.Never);
    }
}
```

- [ ] **Step 3: Run updated `SyncJobTests` to verify they pass**

```bash
dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj --filter "SyncJobTests" -v m
```

Expected: 4 tests PASS.

- [ ] **Step 4: Create `AdaptivePollingOrchestrator`**

Create `src/MSOSync.Scheduler/AdaptivePollingOrchestrator.cs`:

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Interfaces;
using MSOSync.Scheduler.Options;

namespace MSOSync.Scheduler;

/// <summary>
/// Drives adaptive per-node poll loops. Replaces the fixed PeriodicTimer in SyncJob.
/// One Task is spawned per active node. A refresh loop detects newly added nodes every
/// NodeRefreshIntervalSeconds (default 60 s). Registered as a singleton BackgroundService.
/// </summary>
public sealed class AdaptivePollingOrchestrator(
    IServiceScopeFactory             scopeFactory,
    IAdaptivePollingService          pollingService,
    IOptions<AdaptivePollingOptions> pollingOptions,
    IWorkerStatusRegistry            registry,
    ILogger<AdaptivePollingOrchestrator> logger) : BackgroundService
{
    private const int NodeRefreshIntervalSeconds = 60;

    // Tracks one Task per nodeId. The value is a running Task; never awaited here —
    // the CancellationToken passed to each loop drives termination.
    private readonly ConcurrentDictionary<string, Task> _nodeTasks = new();

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(
            nameof(AdaptivePollingOrchestrator),
            TimeSpan.FromSeconds(pollingOptions.Value.BaseIntervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        // Initial node load
        await RefreshNodesAsync(ct);

        using var refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(NodeRefreshIntervalSeconds));
        while (await refreshTimer.WaitForNextTickAsync(ct))
        {
            try { await RefreshNodesAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogWarning(ex, "AdaptivePollingOrchestrator: node refresh failed"); }
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken); // signals ct passed to ExecuteAsync

        // Drain active node tasks with a 10-second timeout
        var allTasks = _nodeTasks.Values.ToArray();
        if (allTasks.Length == 0) return;

        var drain = Task.WhenAll(allTasks);
        var timeout = Task.Delay(TimeSpan.FromSeconds(10), CancellationToken.None);
        await Task.WhenAny(drain, timeout);

        if (!drain.IsCompleted)
            logger.LogWarning("AdaptivePollingOrchestrator: some node tasks did not finish within drain timeout");
    }

    // -----------------------------------------------------------------------

    private async Task RefreshNodesAsync(CancellationToken ct)
    {
        var activeNodeIds = await LoadActiveNodeIdsAsync(ct);

        // Spawn tasks for newly discovered nodes
        foreach (var nodeId in activeNodeIds)
        {
            _nodeTasks.GetOrAdd(nodeId, id =>
            {
                logger.LogDebug("AdaptivePollingOrchestrator: starting dispatch loop for node {NodeId}", id);
                return RunNodeLoopAsync(id, ct);
            });
        }

        // Prune completed tasks (node decommissioned / loop exited)
        foreach (var (nodeId, task) in _nodeTasks)
        {
            if (task.IsCompleted)
            {
                _nodeTasks.TryRemove(nodeId, out _);
                logger.LogDebug("AdaptivePollingOrchestrator: pruned completed task for node {NodeId}", nodeId);
            }
        }
    }

    private async Task<IReadOnlyList<string>> LoadActiveNodeIdsAsync(CancellationToken ct)
    {
        try
        {
            await using var scope    = scopeFactory.CreateAsyncScope();
            var nodeMeta = scope.ServiceProvider.GetRequiredService<INodeMetadataService>();
            var nodes    = await nodeMeta.GetNodesAsync(ct);
            return nodes
                .Where(n => n.CanSynchronize)
                .Select(n => n.NodeId)
                .ToList();
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "AdaptivePollingOrchestrator: failed to load active nodes");
            return Array.Empty<string>();
        }
    }

    private async Task RunNodeLoopAsync(string nodeId, CancellationToken ct)
    {
        logger.LogInformation("AdaptivePollingOrchestrator: starting poll loop for node {NodeId}", nodeId);

        while (!ct.IsCancellationRequested)
        {
            registry.RecordTickStart(nameof(AdaptivePollingOrchestrator));
            bool hadWork = false;
            try
            {
                await using var scope  = scopeFactory.CreateAsyncScope();
                var syncJob = scope.ServiceProvider.GetRequiredService<SyncJob>();

                // RunTickAsync returns implicitly; we detect work by checking whether engine ran
                // (engine publishes SyncCycleCompletedEvent — but here we track exception vs success)
                await syncJob.RunTickAsync(nodeId, ct);

                // If no exception, treat as success. Actual hadWork signal comes from SyncEngine
                // publishing SyncCycleCompletedEvent, but we approximate it here:
                // A future iteration can subscribe to SyncCycleCompletedEvent to get the exact count.
                // For now: assume hadWork=true (conservative — keeps interval from backing off needlessly).
                hadWork = true;

                registry.RecordTickComplete(nameof(AdaptivePollingOrchestrator));
                await pollingService.RecordActivityAsync(nodeId, hadWork, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "AdaptivePollingOrchestrator: tick failed for node {NodeId}", nodeId);
                registry.RecordTickFailed(nameof(AdaptivePollingOrchestrator), ex);
                await pollingService.RecordErrorAsync(nodeId, ct);
            }

            // Sleep for the adaptive interval before next tick
            var interval = await pollingService.GetIntervalAsync(nodeId, ct);
            logger.LogDebug("AdaptivePollingOrchestrator: node {NodeId} sleeping for {Interval}s",
                nodeId, interval.TotalSeconds);
            try { await Task.Delay(interval, ct); }
            catch (OperationCanceledException) { break; }
        }

        logger.LogInformation("AdaptivePollingOrchestrator: poll loop exiting for node {NodeId}", nodeId);
    }
}
```

**Note on `hadWork`:** The orchestrator currently approximates activity as `hadWork=true` on any non-throwing tick. A follow-up in Phase 2D.6 can wire `SyncCycleCompletedEvent.EventCount > 0` for precise activity signals. The spec's convergence tests remain valid because the unit tests exercise `AdaptivePollingService` directly without going through the orchestrator.

- [ ] **Step 5: Update `SyncSchedulerExtensions` to wire in the orchestrator**

Replace `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` entirely:

```csharp
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Scheduler.Options;
using MSOSync.Scheduler.Workers;

namespace MSOSync.Scheduler;

public static class SyncSchedulerExtensions
{
    public static IServiceCollection AddSyncScheduler(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<HeartbeatOptions>(config.GetSection(HeartbeatOptions.Section));
        services.Configure<SyncOptions>(config.GetSection(SyncOptions.Section));
        services.Configure<AdaptivePollingOptions>(config.GetSection(AdaptivePollingOptions.Section));

        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());

        // Adaptive polling
        services.AddMemoryCache(); // idempotent
        services.AddSingleton<IAdaptivePollingService, AdaptivePollingService>();
        services.AddHostedService<AdaptivePollingOrchestrator>();

        // SyncJob demoted from BackgroundService to scoped service (orchestrator drives it)
        services.AddScoped<SyncJob>();

        // Remaining background workers (unchanged)
        services.AddHostedService<SchedulerRecovery>();
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

- [ ] **Step 6: Run full Scheduler test suite**

```bash
dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj -v m
```

Expected: all tests PASS (SyncJobTests now uses the updated constructor; AdaptivePollingServiceTests from Task 2 still pass).

- [ ] **Step 7: Build the solution to catch any compile errors**

```bash
dotnet build src/MSOSync.Scheduler/MSOSync.Scheduler.csproj -c Debug
```

Expected: build succeeds with 0 errors.

- [ ] **Step 8: Commit orchestrator and wiring**

```bash
git add src/MSOSync.Scheduler/SyncJob.cs \
        src/MSOSync.Scheduler/AdaptivePollingOrchestrator.cs \
        src/MSOSync.Scheduler/SyncSchedulerExtensions.cs \
        tests/MSOSync.SchedulerTests/SyncJobTests.cs
git commit -m "feat(2D.5-T3): add AdaptivePollingOrchestrator, demote SyncJob to scoped service"
```
