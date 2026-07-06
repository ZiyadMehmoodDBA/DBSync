# Epic 12B-1 Task 3: Connectivity Engine + Workers

> Task 3 of 7. Master plan: `2026-07-06-epic12b1-node-lifecycle-engine.md`. Spec §5, §4.7 (worker half). Global Constraints apply. Requires Tasks 1–2 (policies, gateway, `FinalizeDecommissionAsync`, `LifecycleOptions`).

**Goal:** Install `ConnectivityEvaluator` as the sole `ConnectivityStatus` writer, strip ProbeWorker to telemetry-only, enforce the heartbeat lifecycle matrix (403/410), and add `DecommissionWorker` + `IDecommissionEvaluator` finalizing drains only through the gateway.

**Files:**
- Create: `src/MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs`
- Create: `src/MSOSync.Scheduler/Workers/DecommissionWorker.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/IDecommissionEvaluator.cs`
- Create: `src/MSOSync.Metadata/Lifecycle/DecommissionEvaluator.cs`
- Modify: `src/MSOSync.Scheduler/Workers/ProbeWorker.cs` (telemetry-only rewrite)
- Modify: `src/MSOSync.Api/Controllers/NodesController.cs` (heartbeat matrix)
- Modify: `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` (register 2 workers)
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs` (register evaluator)
- Test: `tests/MSOSync.MetadataTests/Lifecycle/DecommissionEvaluatorTests.cs`

**Interfaces:**
- Consumes: `IConnectivityPolicy`/`ConnectivityTelemetry` (Task 1), `NodeConnectivityChangedEvent(NodeId, PreviousStatus, NewStatus)` (existing, `src/MSOSync.Scheduler/NodeConnectivityChangedEvent.cs`), `INodeLifecycleService.FinalizeDecommissionAsync` (Task 2), `NodeLifecycleHistoryService.CountOpenBatchesAsync` (Task 2), `LifecycleOptions` (Task 2), `ITopologyService.IsHubAsync`, config keys `Heartbeat:IntervalSeconds` (30) / `Heartbeat:ProbeIntervalSeconds` (60).
- Produces: `IDecommissionEvaluator { Task<DecommissionDecision> EvaluateAsync(SyncNode, CancellationToken); }`, `DecommissionDecision(bool Finalize, DecommissionDecisionReason Reason)`, `DecommissionDecisionReason { DrainCompleted, GraceExpired, OpenBatches }`. Heartbeat status-code contract for Task 7 tests.

---

## Steps

- [ ] **Step 1: Write failing DecommissionEvaluator tests**

```csharp
// tests/MSOSync.MetadataTests/Lifecycle/DecommissionEvaluatorTests.cs
using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class DecommissionEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 06, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoOpenBatches_Finalize_DrainCompleted()
        => DecommissionEvaluator.Decide(openBatches: 0, graceUntil: Now.AddMinutes(30), now: Now)
            .Should().Be(new DecommissionDecision(true, DecommissionDecisionReason.DrainCompleted));

    [Fact]
    public void OpenBatches_GraceExpired_Finalize_GraceExpired()
        => DecommissionEvaluator.Decide(openBatches: 5, graceUntil: Now.AddMinutes(-1), now: Now)
            .Should().Be(new DecommissionDecision(true, DecommissionDecisionReason.GraceExpired));

    [Fact]
    public void OpenBatches_WithinGrace_DoNotFinalize()
        => DecommissionEvaluator.Decide(openBatches: 5, graceUntil: Now.AddMinutes(30), now: Now)
            .Should().Be(new DecommissionDecision(false, DecommissionDecisionReason.OpenBatches));

    [Fact]
    public void NoGraceSet_TreatedAsExpired()   // defensive: Decommissioning row without grace finalizes
        => DecommissionEvaluator.Decide(openBatches: 5, graceUntil: null, now: Now)
            .Should().Be(new DecommissionDecision(true, DecommissionDecisionReason.GraceExpired));

    [Fact]
    public void NoOpenBatches_EvenIfGraceRemains_FinalizesImmediately()
        => DecommissionEvaluator.Decide(openBatches: 0, graceUntil: Now.AddHours(1), now: Now)
            .Finalize.Should().BeTrue();
}
```

- [ ] **Step 2: Run to verify failure**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"; $env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~DecommissionEvaluatorTests" -c Debug
```

Expected: FAIL (types missing).

- [ ] **Step 3: Implement IDecommissionEvaluator**

```csharp
// src/MSOSync.Metadata/Lifecycle/IDecommissionEvaluator.cs
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public enum DecommissionDecisionReason { DrainCompleted, GraceExpired, OpenBatches }

public sealed record DecommissionDecision(bool Finalize, DecommissionDecisionReason Reason);

public interface IDecommissionEvaluator
{
    Task<DecommissionDecision> EvaluateAsync(SyncNode node, CancellationToken ct = default);
}
```

```csharp
// src/MSOSync.Metadata/Lifecycle/DecommissionEvaluator.cs
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Lifecycle;

public sealed class DecommissionEvaluator(AppDbContext db) : IDecommissionEvaluator
{
    public async Task<DecommissionDecision> EvaluateAsync(SyncNode node, CancellationToken ct = default)
    {
        var open = await NodeLifecycleHistoryService.CountOpenBatchesAsync(db, node.NodeId, ct);
        return Decide(open, node.DecommissionGraceUntil, DateTimeOffset.UtcNow);
    }

    /// Pure decision core (unit-tested).
    public static DecommissionDecision Decide(int openBatches, DateTimeOffset? graceUntil, DateTimeOffset now)
    {
        if (openBatches == 0)
            return new(true, DecommissionDecisionReason.DrainCompleted);
        if (graceUntil is null || now >= graceUntil)
            return new(true, DecommissionDecisionReason.GraceExpired);
        return new(false, DecommissionDecisionReason.OpenBatches);
    }
}
```

Register in `MetadataServiceExtensions.AddMetadata`:

```csharp
services.AddScoped<IDecommissionEvaluator, DecommissionEvaluator>();
```

Run Step 1 tests — expected: PASS.

- [ ] **Step 4: ConnectivityEvaluator worker (sole status writer)**

```csharp
// src/MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;

namespace MSOSync.Scheduler.Workers;

/// SOLE writer of ConnectivityStatus + ConnectivityReason (Invariant 3, spec §5.1).
/// Skips a cycle if the previous evaluation is still running (spec §5.1).
public sealed class ConnectivityEvaluator(
    IServiceScopeFactory scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IOptions<LifecycleOptions> lifecycleOptions,
    IConfiguration config,
    ILogger<ConnectivityEvaluator> logger) : BackgroundService
{
    private int _running;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var topology = scope.ServiceProvider.GetRequiredService<ITopologyService>();
            if (!await topology.IsHubAsync(props.NodeId, ct))
            {
                logger.LogInformation("ConnectivityEvaluator disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.ConnectivityEvaluatorIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                logger.LogWarning("ConnectivityEvaluator cycle skipped — previous evaluation still running");
                continue;
            }
            try { await RunCycleAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "ConnectivityEvaluator cycle failed"); }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }

    private async Task RunCycleAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db       = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var policy   = scope.ServiceProvider.GetRequiredService<IConnectivityPolicy>();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        var heartbeatInterval = TimeSpan.FromSeconds(config.GetValue<int>("Heartbeat:IntervalSeconds", 30));
        var probeInterval     = TimeSpan.FromSeconds(config.GetValue<int>("Heartbeat:ProbeIntervalSeconds", 60));
        var now = DateTime.UtcNow;

        var nodes = await db.Nodes.ToListAsync(ct);
        var changes = new List<NodeConnectivityChangedEvent>();

        foreach (var node in nodes)
        {
            var result = policy.Evaluate(new ConnectivityTelemetry(
                node.LifecycleState,
                node.LastHeartbeat,
                node.LastProbeTime,
                LastProbeFailed: node.LastProbeError is not null,
                node.ConsecutiveProbeFailures,
                now, heartbeatInterval, probeInterval));

            if (node.ConnectivityStatus == result.Status && node.ConnectivityReason == result.Reason)
                continue;

            var previous = node.ConnectivityStatus;
            node.ConnectivityStatus = result.Status;
            node.ConnectivityReason = result.Reason;

            if (previous != result.Status)
            {
                db.NodeConnectivityHistories.Add(new SyncNodeConnectivityHistory
                {
                    NodeId = node.NodeId,
                    PreviousStatus = previous,
                    NewStatus = result.Status,
                    Reason = result.Reason,
                    OccurredAt = DateTimeOffset.UtcNow,
                });
                changes.Add(new NodeConnectivityChangedEvent(node.NodeId, previous, result.Status));
            }
        }

        // Prune connectivity history past retention (spec §3.3) — same cycle, cheap delete
        var cutoff = DateTimeOffset.UtcNow.AddDays(-lifecycleOptions.Value.ConnectivityHistoryRetentionDays);
        await db.NodeConnectivityHistories.Where(h => h.OccurredAt < cutoff).ExecuteDeleteAsync(ct);

        await db.SaveChangesAsync(ct);

        // Publish AFTER commit (same discipline as lifecycle events)
        foreach (var evt in changes)
            await mediator.Publish(evt, ct);
    }
}
```

NOTE: `SyncNode.RowVersion` is a concurrency token — a race with a lifecycle command can throw `DbUpdateConcurrencyException` here. Wrap `SaveChangesAsync` in a catch that logs and returns (the next 30s cycle re-evaluates; connectivity writes are idempotent):

```csharp
try { await db.SaveChangesAsync(ct); }
catch (DbUpdateConcurrencyException)
{
    logger.LogDebug("ConnectivityEvaluator lost a concurrency race; next cycle re-evaluates");
    return;   // do not publish events for uncommitted changes
}
```

(Move the publish loop after this try/catch — events fire only on successful commit.)

- [ ] **Step 5: Rewrite ProbeWorker to telemetry-only**

Modify `src/MSOSync.Scheduler/Workers/ProbeWorker.cs`. Keep: hub-only guard, `PeriodicTimer` on `Heartbeat:ProbeIntervalSeconds`, HTTP ping to `{child.SyncUrl}/api/v1/sync/ping`, latency measurement. Change:

1. Selection query →
   ```csharp
   var probeStates = new[] { NodeLifecycleState.Active, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning };
   var query = db.Nodes.AsNoTracking()
       .Where(n => n.UpstreamNodeId == localNodeId && probeStates.Contains(n.LifecycleState));
   if (!lifecycleOptions.Value.MaintenanceContinueProbing)
       query = query.Where(n => !n.MaintenanceMode);
   ```
   (inject `IOptions<LifecycleOptions>` into the worker constructor).
2. **Delete** every write to `ConnectivityStatus` and every `NodeConnectivityChangedEvent` publish, and the latency→status mapping (`<500ms=Reachable` etc.) — the evaluator owns status now.
3. Per-probe result → telemetry-only `ExecuteUpdateAsync`:
   - Success:
     ```csharp
     await db.Nodes.Where(n => n.NodeId == node.NodeId).ExecuteUpdateAsync(s => s
         .SetProperty(n => n.LastProbeTime, now)
         .SetProperty(n => n.LastProbeLatencyMs, latencyMs)
         .SetProperty(n => n.LastProbeError, (string?)null)
         .SetProperty(n => n.ConsecutiveProbeFailures, 0), ct);
     ```
   - Failure:
     ```csharp
     await db.Nodes.Where(n => n.NodeId == node.NodeId).ExecuteUpdateAsync(s => s
         .SetProperty(n => n.LastProbeTime, now)
         .SetProperty(n => n.LastProbeLatencyMs, (int?)null)
         .SetProperty(n => n.LastProbeError, errorMessage.Length > 512 ? errorMessage[..512] : errorMessage)
         .SetProperty(n => n.ConsecutiveProbeFailures, n => n.ConsecutiveProbeFailures + 1), ct);
     ```
   (`ExecuteUpdateAsync` bypasses the RowVersion token — intended: telemetry writes must never conflict with lifecycle commands.)
4. Remove the now-unused `IMediator`/event usings if nothing else needs them.

- [ ] **Step 6: Heartbeat endpoint lifecycle matrix**

In `src/MSOSync.Api/Controllers/NodesController.cs`, `Heartbeat` action — replace the single `Disabled` check (left by Task 1) with the full matrix (spec §5.3), BEFORE `RecordHeartbeatAsync`:

```csharp
switch (node.LifecycleState)
{
    case NodeLifecycleState.Active:
    case NodeLifecycleState.Recovery:
    case NodeLifecycleState.Decommissioning:
        break;   // accepted — draining/recovering nodes still report telemetry
    case NodeLifecycleState.PendingRegistration:
    case NodeLifecycleState.PendingApproval:
        return Forbid();                    // 403 — activation is the readiness proof
    case NodeLifecycleState.Disabled:
        return Forbid();                    // 403
    case NodeLifecycleState.Decommissioned:
    case NodeLifecycleState.Rejected:
        return StatusCode(StatusCodes.Status410Gone);   // agent should stop
    default:
        return Forbid();
}

await nodeService.RecordHeartbeatAsync(nodeId, clock.UtcNow, ct);
return NoContent();
```

No lifecycle write anywhere in this action (Invariant 6). Maintenance mode: accepted normally (no check).

- [ ] **Step 7: DecommissionWorker**

```csharp
// src/MSOSync.Scheduler/Workers/DecommissionWorker.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;

namespace MSOSync.Scheduler.Workers;

/// Finalizes drains ONLY through NodeLifecycleService.FinalizeDecommissionAsync — no side door
/// (spec §4.7, §5.5). Never writes lifecycle state directly.
public sealed class DecommissionWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<NodeProperties> nodeProps,
    IOptions<LifecycleOptions> lifecycleOptions,
    ILogger<DecommissionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;
        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var topology = scope.ServiceProvider.GetRequiredService<ITopologyService>();
            if (!await topology.IsHubAsync(props.NodeId, ct))
            {
                logger.LogInformation("DecommissionWorker disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        var interval = TimeSpan.FromSeconds(lifecycleOptions.Value.DecommissionWorkerIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            try { await RunTickAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "DecommissionWorker tick failed"); }
        }
    }

    private async Task RunTickAsync(CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db        = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var evaluator = scope.ServiceProvider.GetRequiredService<IDecommissionEvaluator>();
        var lifecycle = scope.ServiceProvider.GetRequiredService<INodeLifecycleService>();

        var draining = await db.Nodes.AsNoTracking()
            .Where(n => n.LifecycleState == NodeLifecycleState.Decommissioning)
            .ToListAsync(ct);

        foreach (var node in draining)
        {
            var decision = await evaluator.EvaluateAsync(node, ct);
            if (!decision.Finalize)
            {
                logger.LogDebug("Node {NodeId} still draining ({Reason})", node.NodeId, decision.Reason);
                continue;
            }

            var trigger = decision.Reason == DecommissionDecisionReason.GraceExpired
                ? LifecycleTrigger.Timeout
                : LifecycleTrigger.System;
            try
            {
                await lifecycle.FinalizeDecommissionAsync(
                    node.NodeId, trigger, decision.Reason.ToString(), ct);
                logger.LogInformation("Node {NodeId} decommission finalized ({Reason})", node.NodeId, decision.Reason);
            }
            catch (Exception ex) when (ex is ConcurrencyException or InvalidLifecycleTransitionException)
            {
                // Operator force-completed (or other command won the race) — next tick reconciles.
                logger.LogDebug(ex, "Node {NodeId} finalize lost a race; skipping", node.NodeId);
            }
        }
    }
}
```

- [ ] **Step 8: Register workers**

`src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`:

```csharp
services.AddHostedService<ConnectivityEvaluator>();
services.AddHostedService<DecommissionWorker>();
```

(Placed where `AddHostedService<NodeStatusWorker>()` was removed in Task 1. If `MSOSync.Scheduler` does not already reference `MSOSync.Metadata`, add the ProjectReference — `NodeStatusWorker` used `MSOSync.Metadata.Nodes`, so the reference exists.)

- [ ] **Step 9: Build + tests**

```pwsh
dotnet build MSOSync.sln -c Debug --warnaserror
dotnet test tests/MSOSync.MetadataTests -c Debug --no-build
```

Expected: zero warnings, all green (DecommissionEvaluator + Lifecycle suites included).

- [ ] **Step 10: Commit**

```pwsh
git add src/MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs src/MSOSync.Scheduler/Workers/DecommissionWorker.cs src/MSOSync.Scheduler/Workers/ProbeWorker.cs src/MSOSync.Scheduler/SyncSchedulerExtensions.cs
git add src/MSOSync.Metadata/Lifecycle/IDecommissionEvaluator.cs src/MSOSync.Metadata/Lifecycle/DecommissionEvaluator.cs src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.Api/Controllers/NodesController.cs
git add tests/MSOSync.MetadataTests/Lifecycle/DecommissionEvaluatorTests.cs
git commit -m "feat(12B-1): ConnectivityEvaluator sole status writer, telemetry-only ProbeWorker, heartbeat lifecycle matrix, DecommissionWorker drain finalization"
```
