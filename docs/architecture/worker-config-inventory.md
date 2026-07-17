# Scheduler Worker Configuration Inventory

**Phase:** 2A.8 Configuration  
**Date:** 2026-07-17  
**Status:** Complete

This document catalogs all 5 scheduler workers and their configuration options. All workers use strongly-typed `IOptions<T>` configuration (no raw `IConfiguration.GetValue`).

---

## Worker 1: HeartbeatWorker

**File:** `src/MSOSync.Scheduler/Workers/HeartbeatWorker.cs`  
**Type:** `BackgroundService`

### Dependencies
- `IServiceScopeFactory` — Creates async scopes for HTTP requests
- `IOptions<NodeProperties>` — Node identity and sync URL
- `IOptions<HeartbeatOptions>` — Heartbeat timing
- `ILogger<HeartbeatWorker>` — Structured logging
- `IWorkerStatusRegistry` — Tick tracking and status reporting

### Configuration: `IOptions<HeartbeatOptions>`

| Property | Type | Default | appsettings Path | Purpose |
|---|---|---|---|---|
| `IntervalSeconds` | int | 30 | `Heartbeat:IntervalSeconds` | Interval between heartbeat POST requests to hub |

### Behavior
- **Interval:** Configurable via `HeartbeatOptions.IntervalSeconds`
- **Execution:** Sends `HeartbeatRequest` to hub's `/api/v1/nodes/{nodeId}/heartbeat` endpoint
- **Scope:** Creates a new async scope per tick to fetch `INodeHttpClient`
- **Metrics:** Increments `msosync_heartbeat_sent_total` counter on success
- **Registry:** Registers with `IWorkerStatusRegistry` on start; records tick lifecycle (start/complete/failed)
- **Fallback:** If `IntervalSeconds <= 0`, defaults to 30 seconds

---

## Worker 2: ProbeWorker

**File:** `src/MSOSync.Scheduler/Workers/ProbeWorker.cs`  
**Type:** `BackgroundService`

### Dependencies
- `IServiceScopeFactory` — Creates async scopes for DB and HTTP access
- `IOptions<NodeProperties>` — Local node identity
- `IOptions<LifecycleOptions>` — Lifecycle controls (MaintenanceContinueProbing)
- `IOptions<HeartbeatOptions>` — Probe timing
- `ILogger<ProbeWorker>` — Structured logging
- `IWorkerStatusRegistry` — Tick tracking and status reporting

### Configuration: `IOptions<HeartbeatOptions>`

| Property | Type | Default | appsettings Path | Purpose |
|---|---|---|---|---|
| `ProbeIntervalSeconds` | int | 60 | `Heartbeat:ProbeIntervalSeconds` | Interval between probes of child nodes (hub only) |

### Behavior
- **Enabled:** Hub nodes only (self-check in `StartAsync` disables if not a hub)
- **Interval:** Configurable via `HeartbeatOptions.ProbeIntervalSeconds`
- **Execution:** Pings all child nodes (Active/Recovery/Decommissioning states, unless in maintenance and `MaintenanceContinueProbing=false`)
- **Telemetry:** Writes `LastProbeTime`, `LastProbeLatencyMs`, `LastProbeError`, `ConsecutiveProbeFailures` via `ExecuteUpdateAsync` (bypasses RowVersion)
- **Metrics:** Increments `msosync_probe_success_total` or `msosync_probe_failure_total`
- **Registry:** Registers on start; records tick lifecycle
- **Note:** Does NOT write `ConnectivityStatus` — that is owned by `ConnectivityEvaluator`

---

## Worker 3: ConnectivityEvaluator

**File:** `src/MSOSync.Scheduler/Workers/ConnectivityEvaluator.cs`  
**Type:** `BackgroundService`

### Dependencies
- `IServiceScopeFactory` — Creates async scopes for DB and policy evaluation
- `IOptions<NodeProperties>` — Local node identity
- `IOptions<LifecycleOptions>` — Lifecycle controls and evaluation interval
- `IOptions<HeartbeatOptions>` — Heartbeat/probe intervals for policy input
- `ILogger<ConnectivityEvaluator>` — Structured logging
- `IConnectivityPolicy` — Evaluates node connectivity based on telemetry
- `IMediator` — Publishes `NodeConnectivityChangedEvent`

### Configuration: `IOptions<LifecycleOptions>`

| Property | Type | Default | appsettings Path | Purpose |
|---|---|---|---|---|
| `ConnectivityEvaluatorIntervalSeconds` | int | 30 | `Lifecycle:ConnectivityEvaluatorIntervalSeconds` | Interval between connectivity evaluations |
| `ConnectivityHistoryRetentionDays` | int | 30 | `Lifecycle:ConnectivityHistoryRetentionDays` | Retention for connectivity history table |
| `MaintenanceContinueProbing` | bool | true | `Lifecycle:MaintenanceContinueProbing` | Whether to probe nodes in maintenance mode |

### Additional Configuration Used (for policy logic)

| Property | Type | Default | appsettings Path | Purpose |
|---|---|---|---|---|
| `IntervalSeconds` | int | 30 | `Heartbeat:IntervalSeconds` | Used by policy to determine timeout threshold |
| `ProbeIntervalSeconds` | int | 60 | `Heartbeat:ProbeIntervalSeconds` | Used by policy to determine probe timeout |

### Behavior
- **Enabled:** Hub nodes only (self-check in `ExecuteAsync` disables if not a hub)
- **Interval:** Configurable via `LifecycleOptions.ConnectivityEvaluatorIntervalSeconds`
- **Exclusive Writer:** SOLE writer of `Node.ConnectivityStatus` and `Node.ConnectivityReason`
- **Policy Input:** Passes telemetry (heartbeat, probe latency, probe failures) to `IConnectivityPolicy` for evaluation
- **Events:** Publishes `NodeConnectivityChangedEvent` when status changes
- **History:** Records status transitions in `SyncNodeConnectivityHistory` table
- **Cleanup:** Prunes history older than `ConnectivityHistoryRetentionDays` in same cycle
- **Skip Cycle:** If previous evaluation is still running (prevents overlapping evaluations)
- **Concurrency:** Catches `DbUpdateConcurrencyException` if a lifecycle command races with connectivity write; logs debug and retries next cycle

---

## Worker 4: SyncJob

**File:** `src/MSOSync.Scheduler/SyncJob.cs`  
**Type:** `BackgroundService`

### Dependencies
- `IServiceScopeFactory` — Creates async scopes for sync engine access
- `IOptions<SyncOptions>` — Sync timing
- `ILogger<SyncJob>` — Structured logging
- `IDatabaseLockProvider` — Acquires exclusive lock for sync engine
- `SyncEngine` — Executes sync operations

### Configuration: `IOptions<SyncOptions>`

| Property | Type | Default | appsettings Path | Purpose |
|---|---|---|---|---|
| `IntervalSeconds` | int | 30 | `Sync:IntervalSeconds` | Interval between sync engine runs |

### Behavior
- **Interval:** Configurable via `SyncOptions.IntervalSeconds`
- **Locking:** Tries to acquire `LockNames.SyncEngine` database lock on each tick
- **Conditional Execution:** Skips tick if lock is held by another instance (logs debug)
- **Scope:** Creates a new async scope per tick
- **Engine:** Calls `SyncEngine.RunAsync(ct)` under lock
- **Error Handling:** Catches and logs exceptions; does not propagate (allows next tick to proceed)

---

## Worker 5: PullJob

**File:** `src/MSOSync.Scheduler/PullJob.cs`  
**Type:** `BackgroundService`

### Dependencies
- `IServiceScopeFactory` — Creates async scopes for pull client and apply service access
- `IOptions<NodeProperties>` — Local node identity
- `IOptions<SyncOptions>` — Pull timing
- `ILogger<PullJob>` — Structured logging
- `IChannelMetadataService` — Fetches enabled channels
- `ITopologyService` — Fetches source nodes
- `IBatchTransportQueryService` — Queries batch state
- `PullClient` — Pulls batches from source nodes
- `IApplyService` — Applies batches to local database
- `IClock` — Provides UTC timestamps

### Configuration: `IOptions<SyncOptions>`

| Property | Type | Default | appsettings Path | Purpose |
|---|---|---|---|---|
| `PullIntervalSeconds` | int | 10 | `Sync:PullIntervalSeconds` | Interval between pull ticks |

### Behavior
- **Enabled:** Disabled if node is in Push mode (self-check in `ExecuteAsync`)
- **Interval:** Configurable via `SyncOptions.PullIntervalSeconds`
- **Scope:** Creates a new async scope per tick
- **Execution:** For each enabled channel (sorted by priority desc), pulls batches from all source nodes
- **Polling:** Continues polling a single source+channel pair until `response.MoreAvailable=false`
- **Validation:** Checks sequence gaps and duplicates
- **Apply:** Processes batches via `IApplyService.ApplyAsync`
- **Ack:** Sends ACK back to source with success/failure status
- **Error Handling:** Catches and logs exceptions; does not propagate (allows next tick)

---

## Configuration File

**File:** `src/MSOSync.App/appsettings.json`

```json
{
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
  "Lifecycle": {
    "DecommissionGraceMinutes": 60,
    "BootstrapTokenTtlHours": 72,
    "MaintenanceContinueProbing": true,
    "ConnectivityHistoryRetentionDays": 30,
    "ConnectivityEvaluatorIntervalSeconds": 30,
    "DecommissionWorkerIntervalSeconds": 30
  }
}
```

---

## Dependency Injection

All workers are registered as hosted services in `SyncSchedulerExtensions.AddSyncScheduler()`:

```csharp
services.Configure<HeartbeatOptions>(config.GetSection("Heartbeat"));
services.Configure<SyncOptions>(config.GetSection("Sync"));
services.Configure<LifecycleOptions>(config.GetSection("Lifecycle"));

services.AddHostedService<HeartbeatWorker>();
services.AddHostedService<ProbeWorker>();
services.AddHostedService<ConnectivityEvaluator>();
services.AddHostedService<SyncJob>();
services.AddHostedService<PullJob>();
```

---

## Configuration Options Classes

### HeartbeatOptions
**File:** `src/MSOSync.Scheduler/Options/HeartbeatOptions.cs`

```csharp
public sealed class HeartbeatOptions
{
    public const string Section = "Heartbeat";
    public int IntervalSeconds { get; init; } = 30;
    public int ProbeIntervalSeconds { get; init; } = 60;
}
```

### SyncOptions
**File:** `src/MSOSync.Scheduler/Options/SyncOptions.cs`

```csharp
public sealed class SyncOptions
{
    public const string Section = "Sync";
    public int IntervalSeconds { get; init; } = 30;
    public int PullIntervalSeconds { get; init; } = 10;
}
```

### LifecycleOptions
**File:** `src/MSOSync.Metadata/Lifecycle/LifecycleOptions.cs`

```csharp
public sealed class LifecycleOptions
{
    public const string Section = "Lifecycle";
    public int DecommissionGraceMinutes { get; init; } = 60;
    public int BootstrapTokenTtlHours { get; init; } = 72;
    public bool MaintenanceContinueProbing { get; init; } = true;
    public int ConnectivityHistoryRetentionDays { get; init; } = 30;
    public int ConnectivityEvaluatorIntervalSeconds { get; init; } = 30;
    public int DecommissionWorkerIntervalSeconds { get; init; } = 30;
}
```

---

## Verification

**Completion Criteria (2A.8):**
1. ✓ No `IConfiguration.GetValue` calls for Heartbeat/Sync config keys in scheduler workers
2. ✓ All 5 workers use `IOptions<T>` typed configuration
3. ✓ appsettings.json contains both "Heartbeat" and "Sync" sections
4. ✓ Configuration classes have defaults matching appsettings values
5. ✓ All workers registered via dependency injection in SyncSchedulerExtensions
