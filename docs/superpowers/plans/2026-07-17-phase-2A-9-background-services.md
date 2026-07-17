# Phase 2A.9 — Background Services Worker Registry Compliance

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every recurring `BackgroundService` in the solution registers with `IWorkerStatusRegistry` and calls `RecordTickStart`, `RecordTickComplete`, and `RecordTickFailed` on each cycle. Four scheduler jobs currently miss this: `SyncJob`, `PullJob`, `RetryJob`, and `PurgeJob`.

**Architecture:** `IWorkerStatusRegistry` is a singleton in `MSOSync.App`. The pattern is already established in `HeartbeatWorker`, `ProbeWorker`, `DecommissionWorker`, `ExportJobWorker`, and `ExportCleanupWorker`. This plan brings the four non-compliant scheduler jobs into compliance. `PurgeJob` is a special case: it fires once daily at 02:00 UTC using `Task.Delay` (not `PeriodicTimer`) because the schedule is wall-clock-based. It is exempt from the PeriodicTimer rule but must register with the registry.

**Tech Stack:** C# 13 / .NET 9 / `Microsoft.Extensions.Hosting.BackgroundService` / `MSOSync.Common.Workers.IWorkerStatusRegistry`

## Global Constraints

- No new product features. Scope is strictly compliance.
- Definition of Complete: implementation merged + `dotnet test` exits 0 + no new rule violations + `docs/architecture/` updated.
- RULE-WRK-1: Every recurring `BackgroundService` registers with `IWorkerStatusRegistry`.
- RULE-WRK-2: Every recurring `BackgroundService` uses `PeriodicTimer` for scheduling (except `PurgeJob` — see architecture note above).
- RULE-WRK-3: Every recurring `BackgroundService` calls `RecordTickStart()`, `RecordTickComplete()`, and `RecordTickFailed()`.
- `IWorkerStatusRegistry` is in `MSOSync.Common.Workers` namespace, assembly `MSOSync.Common`.
- `MSOSync.Scheduler` already references `MSOSync.Common` (HeartbeatWorker imports it).
- 2A.8 Configuration plan must be complete before this plan executes (SyncJob and PullJob will have had IConfiguration removed).

---

## File Map

**Modify:**
- `src/MSOSync.Scheduler/SyncJob.cs` — add `IWorkerStatusRegistry`, record tick start/complete/failed
- `src/MSOSync.Scheduler/PullJob.cs` — add `IWorkerStatusRegistry`, record tick start/complete/failed
- `src/MSOSync.Scheduler/RetryJob.cs` — add `IWorkerStatusRegistry`, record tick start/complete/failed
- `src/MSOSync.Scheduler/PurgeJob.cs` — add `IWorkerStatusRegistry`, register (one-shot daily, no tick loop recording needed — just register + log)
- `docs/architecture/background-workers.md` — worker inventory and pattern documentation
- `docs/architecture/audit-backlog-2A.md` — update 2A-009 through 2A-012 to Complete

---

## Task 1: Add IWorkerStatusRegistry to SyncJob

**Files:**
- Modify: `src/MSOSync.Scheduler/SyncJob.cs`

**Interfaces:**
- Consumes: `IOptions<SyncOptions>` from 2A.8 plan Task 1 (already in place)
- Consumes: `IWorkerStatusRegistry` from `MSOSync.Common.Workers`
- Produces: SyncJob registered and tick-tracked in registry

- [ ] **Step 1: Update SyncJob**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory  scopeFactory,
    IOptions<SyncOptions> syncOptions,
    IWorkerStatusRegistry registry,
    ILogger<SyncJob>      logger) : BackgroundService
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
            registry.RecordTickStart(nameof(SyncJob));
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
                var engine       = scope.ServiceProvider.GetRequiredService<SyncEngine>();

                await using var lease = await lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
                if (lease == null)
                {
                    logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
                    registry.RecordTickComplete(nameof(SyncJob));
                    continue;
                }

                await engine.RunAsync(ct);
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

- [ ] **Step 2: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/MSOSync.Scheduler/SyncJob.cs
git commit -m "fix(2A.9-2A-009): SyncJob registers with IWorkerStatusRegistry"
```

---

## Task 2: Add IWorkerStatusRegistry to PullJob

**Files:**
- Modify: `src/MSOSync.Scheduler/PullJob.cs`

**Interfaces:**
- Consumes: `IOptions<SyncOptions>` (already in place from 2A.8)
- Consumes: `IWorkerStatusRegistry`
- Produces: PullJob registered and tick-tracked

- [ ] **Step 1: Update PullJob**

Add `IWorkerStatusRegistry registry` to the primary constructor, add `StartAsync` override, wrap tick body:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;
using MSOSync.Engine;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;

namespace MSOSync.Scheduler;

public sealed class PullJob(
    IServiceScopeFactory     scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IOptions<SyncOptions>    syncOptions,
    IWorkerStatusRegistry    registry,
    ILogger<PullJob>         logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        registry.Register(nameof(PullJob), TimeSpan.FromSeconds(syncOptions.Value.PullIntervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var nodeMeta = scope.ServiceProvider.GetRequiredService<INodeMetadataService>();
            var ownNode  = await nodeMeta.GetNodeAsync(props.NodeId, ct);
            if (ownNode?.TransportMode == TransportMode.Push)
            {
                logger.LogInformation("PullJob disabled — node {NodeId} is in Push mode", props.NodeId);
                return;
            }
        }

        var interval    = TimeSpan.FromSeconds(syncOptions.Value.PullIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            registry.RecordTickStart(nameof(PullJob));
            try
            {
                await RunTickAsync(props.NodeId, ct);
                registry.RecordTickComplete(nameof(PullJob));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(PullJob), ex);
                logger.LogError(ex, "PullJob tick failed");
            }
        }
    }

    private async Task RunTickAsync(string localNodeId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var sp = scope.ServiceProvider;

        var channelMeta  = sp.GetRequiredService<IChannelMetadataService>();
        var topology     = sp.GetRequiredService<ITopologyService>();
        var batchQuery   = sp.GetRequiredService<IBatchTransportQueryService>();
        var pullClient   = sp.GetRequiredService<PullClient>();
        var applyService = sp.GetRequiredService<IApplyService>();
        var clock        = sp.GetRequiredService<IClock>();

        var channels = (await channelMeta.GetChannelsAsync(ct))
            .Where(c => c.Enabled)
            .OrderByDescending(c => c.Priority)
            .ToList();

        var sources = await topology.GetSourceNodesAsync(localNodeId, ct);

        foreach (var channel in channels)
        {
            foreach (var source in sources)
            {
                await PollSourceAsync(
                    source, channel.ChannelId, localNodeId,
                    batchQuery, pullClient, applyService, clock, ct);
            }
        }
    }

    private async Task PollSourceAsync(
        SourceNodeInfo              source,
        string                      channelId,
        string                      localNodeId,
        IBatchTransportQueryService batchQuery,
        PullClient                  pullClient,
        IApplyService               applyService,
        IClock                      clock,
        CancellationToken           ct)
    {
        var lastSeq = await batchQuery.GetLastSequenceAsync(source.NodeId, channelId, ct);

        while (true)
        {
            var request  = new PullRequest(localNodeId, channelId, lastSeq);
            var response = await pullClient.PullAsync(source.SyncUrl, request, ct);

            if (response == null)
            {
                logger.LogDebug("PullJob: no batches from {Source} channel {Ch}", source.NodeId, channelId);
                break;
            }

            foreach (var batch in response.Batches)
            {
                var applied = await ProcessBatchAsync(
                    batch, source, localNodeId, lastSeq, batchQuery, pullClient, applyService, clock, ct);
                if (applied)
                    lastSeq = batch.BatchSequence;
            }

            if (!response.MoreAvailable) break;
        }
    }

    private async Task<bool> ProcessBatchAsync(
        BatchPayload               batch,
        SourceNodeInfo             source,
        string                     localNodeId,
        long                       lastSeq,
        IBatchTransportQueryService batchQuery,
        PullClient                 pullClient,
        IApplyService              applyService,
        IClock                     clock,
        CancellationToken          ct)
    {
        if (lastSeq + 1 != batch.BatchSequence)
        {
            logger.LogWarning(
                "PullJob: sequence gap from {Source} channel {Ch}: expected {Exp} got {Got}",
                source.NodeId, batch.ChannelId, lastSeq + 1, batch.BatchSequence);
            await pullClient.PostAckAsync(source.SyncUrl,
                new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                    false, "SEQUENCE_GAP", new DateTimeOffset(clock.UtcNow, TimeSpan.Zero)), ct);
            return false;
        }

        if (await batchQuery.IncomingBatchExistsAsync(source.NodeId, batch.BatchSequence, ct))
        {
            logger.LogDebug("PullJob: duplicate batch source={Source} seq={Seq} — sending idempotent ACK",
                source.NodeId, batch.BatchSequence);
            await pullClient.PostAckAsync(source.SyncUrl,
                new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                    true, null, new DateTimeOffset(clock.UtcNow, TimeSpan.Zero)), ct);
            return true;
        }

        var incoming = new SyncIncomingBatch
        {
            BatchId       = batch.BatchId,
            NodeId        = localNodeId,
            ChannelId     = batch.ChannelId,
            SourceNodeId  = source.NodeId,
            BatchSequence = batch.BatchSequence,
            ReceivedTime  = clock.UtcNow,
            RowCount      = batch.RowCount,
            Status        = IncomingBatchStatus.New
        };

        await batchQuery.InsertIncomingBatchAsync(incoming, ct);
        var result  = await applyService.ApplyAsync(incoming, batch, ct);
        var ackTime = new DateTimeOffset(clock.UtcNow, TimeSpan.Zero);

        await pullClient.PostAckAsync(source.SyncUrl,
            new AckPayload(batch.BatchId, batch.BatchSequence, localNodeId,
                result.Success, result.Success ? null : "APPLY_FAILURE", ackTime), ct);

        return result.Success;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/MSOSync.Scheduler/PullJob.cs
git commit -m "fix(2A.9-2A-010): PullJob registers with IWorkerStatusRegistry"
```

---

## Task 3: Add IWorkerStatusRegistry to RetryJob

**Files:**
- Modify: `src/MSOSync.Scheduler/RetryJob.cs`

**Interfaces:**
- Consumes: `IWorkerStatusRegistry`
- Note: `RetryJob` has hardcoded `TimeSpan.FromMinutes(5)`. This is acceptable — RetryJob's interval is not a tuneable operational parameter (it always retries every 5 minutes). Keep it as-is but document it.

- [ ] **Step 1: Update RetryJob**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common.Workers;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class RetryJob(
    IServiceScopeFactory  scopeFactory,
    IWorkerStatusRegistry registry,
    ILogger<RetryJob>     logger) : BackgroundService
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
            registry.RecordTickStart(nameof(RetryJob));
            try
            {
                await using var scope        = scopeFactory.CreateAsyncScope();
                var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
                var processor    = scope.ServiceProvider.GetRequiredService<RetryProcessor>();

                await using var lease = await lockProvider.TryAcquireAsync(LockNames.RetryEngine, ct);
                if (lease == null)
                {
                    logger.LogDebug("RetryJob: lock held, skipping");
                    registry.RecordTickComplete(nameof(RetryJob));
                    continue;
                }

                var count = await processor.ProcessAsync(ct);
                if (count > 0) logger.LogInformation("RetryJob queued {Count} batches for retry", count);
                registry.RecordTickComplete(nameof(RetryJob));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(RetryJob), ex);
                logger.LogError(ex, "RetryJob failed");
            }
        }
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add src/MSOSync.Scheduler/RetryJob.cs
git commit -m "fix(2A.9-2A-011): RetryJob registers with IWorkerStatusRegistry"
```

---

## Task 4: Add IWorkerStatusRegistry to PurgeJob

**Files:**
- Modify: `src/MSOSync.Scheduler/PurgeJob.cs`

**Interfaces:**
- Consumes: `IWorkerStatusRegistry`
- Note: `PurgeJob` fires once per day at 02:00 UTC. It uses `Task.Delay` to calculate time-until-next-fire. `PeriodicTimer` cannot reproduce this wall-clock schedule, so `Task.Delay` is the correct and intentional pattern here. The worker is exempt from RULE-WRK-2 (PeriodicTimer). Register with the registry using a 24-hour `expectedInterval`. Record tick on each daily run.

- [ ] **Step 1: Update PurgeJob**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Event;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class PurgeJob(
    IServiceScopeFactory  scopeFactory,
    IClock                clock,
    IWorkerStatusRegistry registry,
    ILogger<PurgeJob>     logger) : BackgroundService
{
    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        // Fires once daily at 02:00 UTC — Task.Delay is intentional (wall-clock schedule).
        // Exempt from PeriodicTimer rule; registered with 24h expectedInterval.
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

    private async Task RunPurgeAsync(CancellationToken ct)
    {
        await using var scope        = scopeFactory.CreateAsyncScope();
        var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
        var eventPurger  = scope.ServiceProvider.GetRequiredService<IEventPurger>();
        var batchPurger  = scope.ServiceProvider.GetRequiredService<BatchPurger>();

        await using var lease = await lockProvider.TryAcquireAsync(LockNames.PurgeEngine, ct);
        if (lease == null) { logger.LogDebug("PurgeJob: lock held, skipping"); return; }

        var events  = await eventPurger.PurgeAsync(ct);
        var batches = await batchPurger.PurgeAsync(ct);
        logger.LogInformation("PurgeJob: deleted {Events} events, {Batches} batches", events, batches);
    }

    private TimeSpan TimeUntilNextFire()
    {
        var now  = clock.UtcNow;
        var next = now.Date.AddHours(2);
        if (next <= now) next = next.AddDays(1);
        return next - now;
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug 2>&1 | Select-String " error " | Select-Object -First 10
```

Expected: 0 errors.

- [ ] **Step 3: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add src/MSOSync.Scheduler/PurgeJob.cs
git commit -m "fix(2A.9-2A-012): PurgeJob registers with IWorkerStatusRegistry"
```

---

## Task 5: Write Worker Inventory Document and Update Audit Backlog

**Files:**
- Create: `docs/architecture/background-workers.md`
- Modify: `docs/architecture/audit-backlog-2A.md`

- [ ] **Step 1: Create background-workers.md**

Create `docs/architecture/background-workers.md`:

```markdown
# Background Worker Inventory

All recurring background workers in MSOSync follow the standard pattern:
register with `IWorkerStatusRegistry`, call `RecordTickStart` at the top
of each cycle, `RecordTickComplete` on success, and `RecordTickFailed`
on exception. Workers use `PeriodicTimer` for scheduling unless a
wall-clock schedule is required (see PurgeJob exception below).

## Standard Pattern

```csharp
public override async Task StartAsync(CancellationToken cancellationToken)
{
    registry.Register(nameof(WorkerName), interval);
    await base.StartAsync(cancellationToken);
}

protected override async Task ExecuteAsync(CancellationToken ct)
{
    using var timer = new PeriodicTimer(interval);
    while (await timer.WaitForNextTickAsync(ct))
    {
        registry.RecordTickStart(nameof(WorkerName));
        try
        {
            await DoWorkAsync(ct);
            registry.RecordTickComplete(nameof(WorkerName));
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            registry.RecordTickFailed(nameof(WorkerName), ex);
        }
    }
}
```

## Worker Inventory

| Worker | Project | Interval | Registry | PeriodicTimer | Notes |
|---|---|---|---|---|---|
| `HeartbeatWorker` | MSOSync.Scheduler | 30s (HeartbeatOptions) | ✅ | ✅ | |
| `ProbeWorker` | MSOSync.Scheduler | 60s (HeartbeatOptions) | ✅ | ✅ | Hub-only |
| `ConnectivityEvaluator` | MSOSync.Scheduler | LifecycleOptions | ✅ | ✅ | Hub-only, skip-on-overlap |
| `DecommissionWorker` | MSOSync.Scheduler | LifecycleOptions | ✅ | ✅ | Hub-only |
| `SyncJob` | MSOSync.Scheduler | 30s (SyncOptions) | ✅ | ✅ | Lock-guarded |
| `PullJob` | MSOSync.Scheduler | 10s (SyncOptions) | ✅ | ✅ | Push-mode disable |
| `RetryJob` | MSOSync.Scheduler | 5min (fixed) | ✅ | ✅ | Fixed cadence acceptable |
| `PurgeJob` | MSOSync.Scheduler | 24h (daily 02:00 UTC) | ✅ | ❌ (Task.Delay) | Wall-clock schedule — PeriodicTimer exempt |
| `ExportJobWorker` | MSOSync.App | 5s (fixed) | ✅ | ❌ (Task.Delay) | Job-polling loop — PeriodicTimer exempt |
| `ExportCleanupWorker` | MSOSync.App | 1h (fixed) | ✅ | ❌ (Task.Delay) | Cleanup loop — PeriodicTimer exempt |
| `AdminBootstrapper` | MSOSync.App | One-shot | N/A | N/A | One-shot startup task — registry exempt |

## PeriodicTimer Exemptions

- **PurgeJob**: Fires at exactly 02:00 UTC daily. `PeriodicTimer` measures elapsed time from start, not wall-clock time. `Task.Delay(TimeUntilNextFire())` is the correct pattern.
- **ExportJobWorker**: Polls for pending export jobs every 5 seconds in a tight loop. The delay is between iterations, not a fixed schedule.
- **ExportCleanupWorker**: Runs expiry logic hourly in a loop. Same rationale as ExportJobWorker.

## AdminBootstrapper

`AdminBootstrapper` runs once at startup to seed the default admin user. It is not a recurring worker and does not register with `IWorkerStatusRegistry`.
```

- [ ] **Step 2: Update audit-backlog-2A.md to mark 2A.9 findings Complete**

In `docs/architecture/audit-backlog-2A.md`, update rows 2A-009 through 2A-012 from "Not Started" to "Complete".

- [ ] **Step 3: Run full test suite**

```
dotnet test D:\MSOSync\MSOSync.sln
```

Expected: 0 failures.

- [ ] **Step 4: Commit**

```
git add docs/architecture/background-workers.md
git add docs/architecture/audit-backlog-2A.md
git commit -m "docs(2A.9): worker inventory document, mark 2A-009 through 2A-012 Complete"
```

---

## Completion Criteria

2A.9 is **Complete** when:
1. Every worker in the inventory table above shows ✅ for Registry (except one-shot `AdminBootstrapper`).
2. `dotnet test D:\MSOSync\MSOSync.sln` exits 0.
3. `docs/architecture/background-workers.md` committed with complete inventory.
4. `docs/architecture/audit-backlog-2A.md` has 2A-009 through 2A-012 marked Complete.
