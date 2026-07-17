# Phase 2A.8 — Configuration Typed Options

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all raw `IConfiguration.GetValue()` calls in MSOSync.Scheduler workers with strongly typed `IOptions<T>` classes, matching the pattern already used by `LifecycleOptions` and `NodeProperties`.

**Architecture:** Create two new options classes (`HeartbeatOptions`, `SyncOptions`) in `MSOSync.Scheduler`. Register both via `Configure<T>()` in `SyncSchedulerExtensions`. Update five workers (`HeartbeatWorker`, `ProbeWorker`, `ConnectivityEvaluator`, `PullJob`, `SyncJob`) to inject the appropriate options and remove their `IConfiguration` dependency. Add the `Sync` section to `appsettings.json` since it currently has no entry there.

**Tech Stack:** C# 13 / .NET 9 / `Microsoft.Extensions.Options` / `Microsoft.Extensions.Configuration`

## Global Constraints

- No new product features during Phase 2A. Scope is strictly stabilization.
- Definition of Complete: implementation merged + `dotnet test` exits 0 + no new rule violations + `docs/architecture/` updated.
- RULE-CFG-1: All configuration in workers accessed via `IOptions<T>`. No `IConfiguration.GetValue()`.
- RULE-CFG-2: Required configuration validated at startup. Missing required config causes fail-fast at boot.
- RULE-CFG-3: No hardcoded configuration values in service code. All tunables in `appsettings.json`.
- Do not change `appsettings.Development.json` — it must not contain plaintext credentials.
- Do not change configuration key names in `appsettings.json` — only the C# access pattern changes.
- `LifecycleOptions` already exists at `src/MSOSync.Metadata/Lifecycle/LifecycleOptions.cs` — do not duplicate it.
- Existing pattern to follow: `IOptions<NodeProperties>` in `ProbeWorker`, `IOptions<LifecycleOptions>` in `ConnectivityEvaluator`.

---

## File Map

**Create:**
- `src/MSOSync.Scheduler/HeartbeatOptions.cs` — typed options for `Heartbeat:*` config keys
- `src/MSOSync.Scheduler/SyncOptions.cs` — typed options for `Sync:*` config keys
- `tests/MSOSync.SchedulerTests/HeartbeatOptionsTests.cs` — unit tests for defaults
- `tests/MSOSync.SchedulerTests/SyncOptionsTests.cs` — unit tests for defaults

**Modify:**
- `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` — register `HeartbeatOptions` and `SyncOptions`
- `src/MSOSync.Scheduler/Workers/HeartbeatWorker.cs` — inject `IOptions<HeartbeatOptions>`, remove `IConfiguration`
- `src/MSOSync.Scheduler/Workers/ProbeWorker.cs` — inject `IOptions<HeartbeatOptions>`, remove `IConfiguration`
- `src/MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs` — inject `IOptions<HeartbeatOptions>`, remove `IConfiguration`
- `src/MSOSync.Scheduler/PullJob.cs` — inject `IOptions<SyncOptions>`, remove `IConfiguration`
- `src/MSOSync.Scheduler/SyncJob.cs` — inject `IOptions<SyncOptions>`, remove `IConfiguration`
- `src/MSOSync.App/appsettings.json` — add `Sync` section with defaults

---

## Task 1: Create HeartbeatOptions and SyncOptions

**Files:**
- Create: `src/MSOSync.Scheduler/HeartbeatOptions.cs`
- Create: `src/MSOSync.Scheduler/SyncOptions.cs`
- Test: `tests/MSOSync.SchedulerTests/HeartbeatOptionsTests.cs`
- Test: `tests/MSOSync.SchedulerTests/SyncOptionsTests.cs`

**Interfaces:**
- Produces: `HeartbeatOptions` with `Section = "Heartbeat"`, `IntervalSeconds`, `ProbeIntervalSeconds`
- Produces: `SyncOptions` with `Section = "Sync"`, `IntervalSeconds`, `PullIntervalSeconds`

First, check if a SchedulerTests project exists:

```
ls D:\MSOSync\tests\
```

If `MSOSync.SchedulerTests` does not exist, use whichever test project exists for scheduler-related tests. Look in `tests/` directory — likely `MSOSync.AppTests` or `MSOSync.IntegrationTests`.

- [ ] **Step 1: Write failing tests for HeartbeatOptions defaults**

If no SchedulerTests project exists, add the test file to the nearest existing test project. Locate test projects:
```
ls D:\MSOSync\tests\
```

Create `tests/MSOSync.AppTests/HeartbeatOptionsTests.cs` (or equivalent path):

```csharp
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.AppTests;

public sealed class HeartbeatOptionsTests
{
    [Fact]
    public void Default_IntervalSeconds_Is30()
    {
        var opts = new HeartbeatOptions();
        Assert.Equal(30, opts.IntervalSeconds);
    }

    [Fact]
    public void Default_ProbeIntervalSeconds_Is60()
    {
        var opts = new HeartbeatOptions();
        Assert.Equal(60, opts.ProbeIntervalSeconds);
    }

    [Fact]
    public void Section_Constant_Is_Heartbeat()
    {
        Assert.Equal("Heartbeat", HeartbeatOptions.Section);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test D:\MSOSync\MSOSync.sln --filter "HeartbeatOptionsTests" -v n
```

Expected: FAIL with "The type or namespace name 'HeartbeatOptions' could not be found"

- [ ] **Step 3: Create HeartbeatOptions**

Create `src/MSOSync.Scheduler/HeartbeatOptions.cs`:

```csharp
namespace MSOSync.Scheduler;

public sealed class HeartbeatOptions
{
    public const string Section = "Heartbeat";
    public int IntervalSeconds { get; init; } = 30;
    public int ProbeIntervalSeconds { get; init; } = 60;
}
```

- [ ] **Step 4: Write failing tests for SyncOptions defaults**

Create `tests/MSOSync.AppTests/SyncOptionsTests.cs` (same project as HeartbeatOptionsTests):

```csharp
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.AppTests;

public sealed class SyncOptionsTests
{
    [Fact]
    public void Default_IntervalSeconds_Is30()
    {
        var opts = new SyncOptions();
        Assert.Equal(30, opts.IntervalSeconds);
    }

    [Fact]
    public void Default_PullIntervalSeconds_Is10()
    {
        var opts = new SyncOptions();
        Assert.Equal(10, opts.PullIntervalSeconds);
    }

    [Fact]
    public void Section_Constant_Is_Sync()
    {
        Assert.Equal("Sync", SyncOptions.Section);
    }
}
```

- [ ] **Step 5: Create SyncOptions**

Create `src/MSOSync.Scheduler/SyncOptions.cs`:

```csharp
namespace MSOSync.Scheduler;

public sealed class SyncOptions
{
    public const string Section = "Sync";
    public int IntervalSeconds { get; init; } = 30;
    public int PullIntervalSeconds { get; init; } = 10;
}
```

- [ ] **Step 6: Run tests to verify they pass**

```
dotnet test D:\MSOSync\MSOSync.sln --filter "HeartbeatOptionsTests|SyncOptionsTests" -v n
```

Expected: All 6 tests PASS.

- [ ] **Step 7: Register options in SyncSchedulerExtensions**

Modify `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`:

```csharp
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<SchedulerRecovery>());
        services.AddHostedService<SchedulerRecovery>();
        services.AddHostedService<SyncJob>();
        services.AddHostedService<RetryJob>();
        services.AddHostedService<PurgeJob>();
        services.AddHostedService<PullJob>();
        services.AddHostedService<HeartbeatWorker>();
        services.AddHostedService<ProbeWorker>();
        // NodeStatusWorker deleted in Epic 12B-1 — lifecycle handled by NodeLifecycleState
        services.AddHostedService<ConnectivityEvaluator>();
        services.AddHostedService<DecommissionWorker>();
        return services;
    }
}
```

- [ ] **Step 8: Add Sync section to appsettings.json**

`appsettings.json` currently has no `Sync` section. Add it so defaults are explicit and documented:

Modify `src/MSOSync.App/appsettings.json` — add the `Sync` block after the `Heartbeat` block:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "AllowedHosts": "*",
  "Node": {
    "NodeId": "",
    "GroupId": "",
    "SyncUrl": ""
  },
  "Jwt": {
    "Issuer": "msosync",
    "Audience": "msosync-dashboard",
    "AccessExpiryMinutes": 60,
    "RefreshExpiryDays": 7
  },
  "Heartbeat": {
    "IntervalSeconds": 30,
    "ProbeIntervalSeconds": 60,
    "StatusCheckIntervalSeconds": 60,
    "MissedThreshold": 3
  },
  "Sync": {
    "IntervalSeconds": 30,
    "PullIntervalSeconds": 10
  },
  "Export": {
    "ImmediateThreshold": 50000,
    "BasePath": "exports",
    "RetentionHours": 24,
    "MaxConcurrentJobs": 1
  },
  "Pagination": {
    "CursorHmacKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
  }
}
```

- [ ] **Step 9: Build to confirm no errors**

```
dotnet build D:\MSOSync\MSOSync.sln -c Debug --no-restore 2>&1 | Select-String "error|warning" | Select-Object -First 20
```

Expected: 0 errors. Warnings about options not yet used are fine — workers haven't been updated yet.

- [ ] **Step 10: Commit**

```
git add src/MSOSync.Scheduler/HeartbeatOptions.cs
git add src/MSOSync.Scheduler/SyncOptions.cs
git add src/MSOSync.Scheduler/SyncSchedulerExtensions.cs
git add src/MSOSync.App/appsettings.json
git add tests/MSOSync.AppTests/HeartbeatOptionsTests.cs
git add tests/MSOSync.AppTests/SyncOptionsTests.cs
git commit -m "feat(2A.8): add HeartbeatOptions and SyncOptions typed configuration classes"
```

---

## Task 2: Migrate HeartbeatWorker to HeartbeatOptions

**Files:**
- Modify: `src/MSOSync.Scheduler/Workers/HeartbeatWorker.cs`

**Interfaces:**
- Consumes: `HeartbeatOptions` from Task 1

Current state (lines to remove):
```csharp
private readonly IConfiguration _config;
// constructor param: IConfiguration config
// usage: _config.GetValue<int>("Heartbeat:IntervalSeconds", 30)
```

- [ ] **Step 1: Update HeartbeatWorker constructor and usages**

Replace entire `HeartbeatWorker.cs` content:

```csharp
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Transport;

namespace MSOSync.Scheduler.Workers;

public sealed class HeartbeatWorker : BackgroundService
{
    private static readonly Meter          Meter = new("MSOSync.Heartbeat", "1.0.0");
    private static readonly Counter<long>  Sent  = Meter.CreateCounter<long>(
        "msosync_heartbeat_sent_total", description: "Total heartbeat POST requests sent");

    private readonly IServiceScopeFactory      _scopeFactory;
    private readonly IOptions<NodeProperties>  _nodeProps;
    private readonly IOptions<HeartbeatOptions> _heartbeatOptions;
    private readonly ILogger<HeartbeatWorker>  _logger;
    private readonly IWorkerStatusRegistry     _registry;
    private readonly DateTime                  _startTime = DateTime.UtcNow;

    public HeartbeatWorker(
        IServiceScopeFactory      scopeFactory,
        IOptions<NodeProperties>  nodeProps,
        IOptions<HeartbeatOptions> heartbeatOptions,
        ILogger<HeartbeatWorker>  logger,
        IWorkerStatusRegistry     registry)
    {
        _scopeFactory      = scopeFactory;
        _nodeProps         = nodeProps;
        _heartbeatOptions  = heartbeatOptions;
        _logger            = logger;
        _registry          = registry;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var intervalSeconds = _heartbeatOptions.Value.IntervalSeconds;
        if (intervalSeconds <= 0) intervalSeconds = 30;
        _registry.Register(nameof(HeartbeatWorker), TimeSpan.FromSeconds(intervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props    = _nodeProps.Value;
        var interval = TimeSpan.FromSeconds(_heartbeatOptions.Value.IntervalSeconds);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            _registry.RecordTickStart(nameof(HeartbeatWorker));
            try
            {
                await using var scope      = _scopeFactory.CreateAsyncScope();
                var httpClient = scope.ServiceProvider.GetRequiredService<INodeHttpClient>();

                var request = new MSOSync.Metadata.Dtos.HeartbeatRequest(
                    NodeId:        props.NodeId,
                    NodeVersion:   typeof(HeartbeatWorker).Assembly.GetName().Version?.ToString(),
                    UptimeSeconds: (long)(DateTime.UtcNow - _startTime).TotalSeconds,
                    DatabaseType:  "SqlServer",
                    TransportMode: null);

                await httpClient.PostAsync<MSOSync.Metadata.Dtos.HeartbeatRequest, object>(
                    $"{props.SyncUrl}/api/v1/nodes/{props.NodeId}/heartbeat",
                    request,
                    props.NodeId,
                    props.NodeToken,
                    ct);

                Sent.Add(1);
                _registry.RecordTickComplete(nameof(HeartbeatWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                _registry.RecordTickFailed(nameof(HeartbeatWorker), ex);
                _logger.LogWarning(ex, "HeartbeatWorker: heartbeat send failed");
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
git add src/MSOSync.Scheduler/Workers/HeartbeatWorker.cs
git commit -m "fix(2A.8-2A-004): HeartbeatWorker uses IOptions<HeartbeatOptions>"
```

---

## Task 3: Migrate ProbeWorker to HeartbeatOptions

**Files:**
- Modify: `src/MSOSync.Scheduler/Workers/ProbeWorker.cs`

**Interfaces:**
- Consumes: `HeartbeatOptions` from Task 1

Current raw config calls in ProbeWorker (primary constructor param `IConfiguration config`, lines ~36 and ~45):
```csharp
config.GetValue<int>("Heartbeat:ProbeIntervalSeconds", 60)
```

- [ ] **Step 1: Update ProbeWorker**

Replace the constructor parameter list and all raw config usages. The primary constructor currently has `IConfiguration config` — remove it, add `IOptions<HeartbeatOptions> heartbeatOptions`:

```csharp
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Topology;
using MSOSync.Transport;

namespace MSOSync.Scheduler.Workers;

/// Telemetry-only probe worker — writes LastProbeTime/Latency/Error/ConsecutiveProbeFailures via
/// ExecuteUpdateAsync (bypasses RowVersion token). Does NOT write ConnectivityStatus or publish
/// NodeConnectivityChangedEvent — that is owned by ConnectivityEvaluator (Invariant 3, spec §5.1).
public sealed class ProbeWorker(
    IServiceScopeFactory       scopeFactory,
    IOptions<NodeProperties>   nodeProps,
    IOptions<LifecycleOptions> lifecycleOptions,
    IOptions<HeartbeatOptions> heartbeatOptions,
    ILogger<ProbeWorker>       logger,
    IWorkerStatusRegistry      registry) : BackgroundService
{
    private static readonly Meter         Meter   = new("MSOSync.Probe", "1.0.0");
    private static readonly Counter<long> Success = Meter.CreateCounter<long>("msosync_probe_success_total");
    private static readonly Counter<long> Failure = Meter.CreateCounter<long>("msosync_probe_failure_total");

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var intervalSeconds = heartbeatOptions.Value.ProbeIntervalSeconds;
        registry.Register(nameof(ProbeWorker), TimeSpan.FromSeconds(intervalSeconds));
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props    = nodeProps.Value;
        var interval = TimeSpan.FromSeconds(heartbeatOptions.Value.ProbeIntervalSeconds);

        await using (var scope = scopeFactory.CreateAsyncScope())
        {
            var topology = scope.ServiceProvider.GetRequiredService<ITopologyService>();
            if (!await topology.IsHubAsync(props.NodeId, ct))
            {
                logger.LogInformation("ProbeWorker disabled — node {NodeId} is not a hub", props.NodeId);
                return;
            }
        }

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            registry.RecordTickStart(nameof(ProbeWorker));
            try
            {
                await RunProbeTickAsync(props.NodeId, ct);
                registry.RecordTickComplete(nameof(ProbeWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(ProbeWorker), ex);
                logger.LogError(ex, "ProbeWorker tick failed");
            }
        }
    }

    private async Task RunProbeTickAsync(string localNodeId, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db         = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var httpClient = scope.ServiceProvider.GetRequiredService<INodeHttpClient>();

        var probeStates = new[] { NodeLifecycleState.Active, NodeLifecycleState.Recovery, NodeLifecycleState.Decommissioning };
        var query = db.Nodes.AsNoTracking()
            .Where(n => n.UpstreamNodeId == localNodeId && probeStates.Contains(n.LifecycleState));
        if (!lifecycleOptions.Value.MaintenanceContinueProbing)
            query = query.Where(n => !n.MaintenanceMode);

        var children = await query.ToListAsync(ct);

        foreach (var child in children)
        {
            var sw  = Stopwatch.StartNew();
            var now = DateTime.UtcNow;

            try
            {
                await httpClient.PostAsync<object, object>(
                    $"{child.SyncUrl}/api/v1/sync/ping", new { }, child.NodeId, string.Empty, ct);
                sw.Stop();
                var latencyMs = (int)sw.ElapsedMilliseconds;

                await db.Nodes.Where(n => n.NodeId == child.NodeId).ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.LastProbeTime, now)
                    .SetProperty(n => n.LastProbeLatencyMs, latencyMs)
                    .SetProperty(n => n.LastProbeError, (string?)null)
                    .SetProperty(n => n.ConsecutiveProbeFailures, 0), ct);

                Success.Add(1);
                logger.LogDebug("ProbeWorker: {NodeId} reachable ({Ms}ms)", child.NodeId, latencyMs);
            }
            catch (Exception ex)
            {
                sw.Stop();
                var errorMessage = ex.Message;
                var trimmed = errorMessage.Length > 512 ? errorMessage[..512] : errorMessage;

                await db.Nodes.Where(n => n.NodeId == child.NodeId).ExecuteUpdateAsync(s => s
                    .SetProperty(n => n.LastProbeTime, now)
                    .SetProperty(n => n.LastProbeLatencyMs, (int?)null)
                    .SetProperty(n => n.LastProbeError, trimmed)
                    .SetProperty(n => n.ConsecutiveProbeFailures, n => n.ConsecutiveProbeFailures + 1), ct);

                Failure.Add(1);
                logger.LogDebug("ProbeWorker: {NodeId} probe failed — {Error}", child.NodeId, trimmed);
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
git add src/MSOSync.Scheduler/Workers/ProbeWorker.cs
git commit -m "fix(2A.8-2A-005): ProbeWorker uses IOptions<HeartbeatOptions>"
```

---

## Task 4: Migrate ConnectivityEvaluator to HeartbeatOptions

**Files:**
- Modify: `src/MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs`

**Interfaces:**
- Consumes: `HeartbeatOptions` from Task 1

Current raw config calls (lines ~63-64):
```csharp
var heartbeatInterval = TimeSpan.FromSeconds(config.GetValue<int>("Heartbeat:IntervalSeconds", 30));
var probeInterval     = TimeSpan.FromSeconds(config.GetValue<int>("Heartbeat:ProbeIntervalSeconds", 60));
```

- [ ] **Step 1: Update ConnectivityEvaluator**

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
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
    IServiceScopeFactory       scopeFactory,
    IOptions<NodeProperties>   nodeProps,
    IOptions<LifecycleOptions> lifecycleOptions,
    IOptions<HeartbeatOptions> heartbeatOptions,
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

        var heartbeatInterval = TimeSpan.FromSeconds(heartbeatOptions.Value.IntervalSeconds);
        var probeInterval     = TimeSpan.FromSeconds(heartbeatOptions.Value.ProbeIntervalSeconds);
        var now = DateTime.UtcNow;

        // Exclude terminal states — Decommissioned and Rejected nodes never send heartbeats
        // or receive probes; skipping them avoids unnecessary DB work (Task 7 minor fix).
        var nodes = await db.Nodes
            .Where(n => n.LifecycleState != NodeLifecycleState.Decommissioned
                     && n.LifecycleState != NodeLifecycleState.Rejected)
            .ToListAsync(ct);
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

        // RowVersion is a concurrency token — a race with a lifecycle command can throw here.
        // Connectivity writes are idempotent; the next cycle re-evaluates.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            logger.LogDebug("ConnectivityEvaluator lost a concurrency race; next cycle re-evaluates");
            return;   // do not publish events for uncommitted changes
        }

        // Publish AFTER commit (same discipline as lifecycle events)
        foreach (var evt in changes)
            await mediator.Publish(evt, ct);
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
git add src/MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs
git commit -m "fix(2A.8-2A-006): ConnectivityEvaluator uses IOptions<HeartbeatOptions>"
```

---

## Task 5: Migrate SyncJob to SyncOptions

**Files:**
- Modify: `src/MSOSync.Scheduler/SyncJob.cs`

**Interfaces:**
- Consumes: `SyncOptions` from Task 1

Current raw config call (line ~17):
```csharp
var interval = TimeSpan.FromSeconds(config.GetValue<int>("Sync:IntervalSeconds", 30));
```

- [ ] **Step 1: Update SyncJob**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Engine;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler;

public sealed class SyncJob(
    IServiceScopeFactory  scopeFactory,
    IOptions<SyncOptions> syncOptions,
    ILogger<SyncJob>      logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(syncOptions.Value.IntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        while (await timer.WaitForNextTickAsync(ct))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var lockProvider = scope.ServiceProvider.GetRequiredService<IDatabaseLockProvider>();
            var engine       = scope.ServiceProvider.GetRequiredService<SyncEngine>();

            await using var lease = await lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
            if (lease == null)
            {
                logger.LogDebug("SyncJob: lock held by another instance, skipping tick");
                continue;
            }

            try { await engine.RunAsync(ct); }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            { logger.LogError(ex, "SyncJob run failed"); }
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
git commit -m "fix(2A.8-2A-008): SyncJob uses IOptions<SyncOptions>"
```

---

## Task 6: Migrate PullJob to SyncOptions

**Files:**
- Modify: `src/MSOSync.Scheduler/PullJob.cs`

**Interfaces:**
- Consumes: `SyncOptions` from Task 1

Current raw config call (line ~40):
```csharp
var intervalSeconds = config.GetValue<int>("Sync:PullIntervalSeconds", 10);
```

- [ ] **Step 1: Update PullJob**

Replace the constructor signature and config usage — keep all business logic unchanged:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
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
    ILogger<PullJob>         logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var props = nodeProps.Value;

        // Self-check: if this node is in PUSH mode, PullJob is disabled
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
            try
            {
                await RunTickAsync(props.NodeId, ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
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

- [ ] **Step 3: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n 2>&1 | tail -20
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add src/MSOSync.Scheduler/PullJob.cs
git commit -m "fix(2A.8-2A-007): PullJob uses IOptions<SyncOptions>"
```

---

## Task 7: Final Verification and Architecture Document

**Files:**
- Create: `docs/architecture/` (directory if not exists)
- Create: `docs/architecture/audit-backlog-2A.md` — (or update if already created by another plan)

- [ ] **Step 1: Verify no IConfiguration.GetValue remains in Scheduler**

```
grep -rn "IConfiguration\|GetValue.*Heartbeat\|GetValue.*Sync" D:\MSOSync\src\MSOSync.Scheduler\ --include="*.cs"
```

Expected: No matches for `GetValue` with config key strings. `IConfiguration` may still appear in `SyncSchedulerExtensions` parameter (unused `_` param) — acceptable, remove that too if present.

If `SyncSchedulerExtensions` still has `IConfiguration _` parameter:
```csharp
public static IServiceCollection AddSyncScheduler(
    this IServiceCollection services,
    IConfiguration config)
```
This parameter is now used (passed to `Configure<T>()`), so it should remain.

- [ ] **Step 2: Run full test suite**

```
dotnet test D:\MSOSync\MSOSync.sln
```

Expected: 0 failures.

- [ ] **Step 3: Create docs/architecture directory if needed**

```powershell
if (-not (Test-Path "D:\MSOSync\docs\architecture")) {
    New-Item -ItemType Directory -Path "D:\MSOSync\docs\architecture"
}
```

- [ ] **Step 4: Create audit-backlog-2A.md with 2A.8 findings marked Fixed**

Create `docs/architecture/audit-backlog-2A.md`:

```markdown
# Phase 2A Audit Backlog

Last updated: 2026-07-17

| ID | Severity | Category | Workstream | File | ~Line | Issue | Resolution | Priority | Status |
|---|---|---|---|---|---|---|---|---|---|
| 2A-001 | Low | API | 2A.1 | MSOSync.Api/Controllers/ExportJobController.cs | 73 | Returns anonymous `new { jobId }` on 202 Accepted | Fixed — CreateExportJobResponse record created | P2 | Not Started |
| 2A-002 | Low | Validation | 2A.3 | MSOSync.Api/Controllers/PreferencesController.cs | 25 | Manual key validation in controller | Fixed — UpsertPreferenceValidator created | P2 | Not Started |
| 2A-003 | Low | DTO | 2A.6 | MSOSync.Api/Controllers/ExportJobController.cs | 154 | DTOs defined inline in controller file | Fixed — moved to MSOSync.Api/Dtos/Export/ | P2 | Not Started |
| 2A-004 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/Workers/HeartbeatWorker.cs | 42 | Raw IConfiguration.GetValue for Heartbeat:IntervalSeconds | Fixed — uses IOptions<HeartbeatOptions> | P1 | Complete |
| 2A-005 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/Workers/ProbeWorker.cs | 36 | Raw IConfiguration.GetValue for Heartbeat:ProbeIntervalSeconds | Fixed — uses IOptions<HeartbeatOptions> | P1 | Complete |
| 2A-006 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs | 63 | Raw IConfiguration for heartbeat/probe intervals | Fixed — uses IOptions<HeartbeatOptions> | P1 | Complete |
| 2A-007 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/PullJob.cs | 40 | Raw IConfiguration.GetValue for Sync:PullIntervalSeconds | Fixed — uses IOptions<SyncOptions> | P1 | Complete |
| 2A-008 | Medium | Configuration | 2A.8 | MSOSync.Scheduler/SyncJob.cs | 17 | Raw IConfiguration.GetValue for Sync:IntervalSeconds | Fixed — uses IOptions<SyncOptions> | P1 | Complete |
| 2A-009 | Medium | Workers | 2A.9 | MSOSync.Scheduler/SyncJob.cs | — | Missing IWorkerStatusRegistry | Fixed in 2A.9 plan | P1 | Not Started |
| 2A-010 | Medium | Workers | 2A.9 | MSOSync.Scheduler/PullJob.cs | — | Missing IWorkerStatusRegistry | Fixed in 2A.9 plan | P1 | Not Started |
| 2A-011 | Medium | Workers | 2A.9 | MSOSync.Scheduler/RetryJob.cs | 15 | Missing IWorkerStatusRegistry, interval hardcoded | Fixed in 2A.9 plan | P1 | Not Started |
| 2A-012 | Medium | Workers | 2A.9 | MSOSync.Scheduler/PurgeJob.cs | — | Missing IWorkerStatusRegistry, Task.Delay loop | Fixed in 2A.9 plan | P1 | Not Started |
```

- [ ] **Step 5: Commit**

```
git add docs/architecture/audit-backlog-2A.md
git commit -m "docs(2A.8): mark configuration findings Complete, create audit backlog"
```

---

## Completion Criteria

2A.8 is **Complete** when:
1. `grep -rn "GetValue.*Heartbeat\|GetValue.*Sync" src/MSOSync.Scheduler/ --include="*.cs"` returns zero matches.
2. `dotnet test D:\MSOSync\MSOSync.sln` exits 0.
3. `docs/architecture/audit-backlog-2A.md` exists with 2A-004 through 2A-008 marked Complete.
4. `src/MSOSync.App/appsettings.json` contains the `Sync` section.
