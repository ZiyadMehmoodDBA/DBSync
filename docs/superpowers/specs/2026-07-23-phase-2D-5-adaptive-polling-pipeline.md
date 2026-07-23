# Phase 2D.5 — Adaptive Polling + Pipeline Optimization

**Status:** Approved specification — 2026-07-23
**Phase:** 2D (Scalability & Performance)
**Roadmap:** `docs/superpowers/specs/2026-07-17-roadmap-v2.md`

---

## Goal

Phase 2D.5 targets two adjacent inefficiencies in the sync pipeline:

1. **Fixed polling cadence wastes resources and hurts latency.** `SyncJob` fires every `SyncOptions.IntervalSeconds` (default 30 s) regardless of whether a node has been active or silent. A busy node sees unacceptable latency between events; an idle node burns CPU and lock contention on empty queries. Adaptive polling collapses that trade-off: busy nodes poll faster, idle nodes back off, erroring nodes apply exponential backoff with jitter.

2. **Per-batch overhead is unbudgeted.** Gzip is applied unconditionally — even to 200-byte payloads where compression adds ~50 bytes of header overhead and burns CPU. Channels on the same node are dispatched serially. No per-stage timing data exists to guide future optimization. Pipeline optimization eliminates these overheads and makes the pipeline observable.

These two concerns share a single spec because adaptive polling drives which batches enter the pipeline, and pipeline timing data feeds the activity signal that drives polling decisions.

**Out of scope for 2D.5:** Distributed schedulers, Redis cache, horizontal scaling, bulk routing. Those are separate 2D deliverables.

---

## Architecture

### Component 1 — Adaptive Polling (`MSOSync.Scheduler`)

`IAdaptivePollingService` is a singleton living in `MSOSync.Scheduler`. It maintains per-node state — last-activity timestamp, consecutive idle count, consecutive error count — in `IMemoryCache` (ephemeral; resets on restart by design). `SyncJob` calls `GetIntervalAsync(nodeId)` before each dispatch decision and calls `RecordActivityAsync(nodeId, hadWork)` after each cycle completes.

`SyncJob` currently uses a single `PeriodicTimer` with a fixed interval. After 2D.5, `SyncJob` drives a per-node dispatch loop: one `Task` per active node, each sleeping for `GetIntervalAsync(nodeId)` between cycles. The global distributed lock (`LockNames.SyncEngine`) is retained — held per node-dispatch, not across the full multi-node loop.

Per-node tasks are managed by `AdaptivePollingOrchestrator` (a `BackgroundService` in `MSOSync.Scheduler`). It replaces the `SyncJob` `PeriodicTimer` loop. `SyncJob`'s `RunTickAsync(nodeId, ct)` is the unit of work invoked per node per cycle.

### Component 2 — Pipeline Optimization (`MSOSync.Transport` + `MSOSync.Engine`)

Two changes:

**Compression gating.** `NodeHttpClient.SendAsync` currently compresses every payload unconditionally. After 2D.5, it checks payload size against `CompressionOptions.ThresholdBytes` (default 1024). Payloads below the threshold are sent uncompressed. `Content-Encoding: gzip` header is omitted when skipping compression. No protocol negotiation is required for this gate — it is a pure sender-side optimization; the receiver's decompression path is already gated on the presence of the `Content-Encoding` header.

**Parallel channel dispatch.** When `SyncEngine.RunAsync` produces multiple batches targeting different channels on the same node, it currently sends them serially in a `foreach`. After 2D.5, batches are grouped by `batch.NodeId` and dispatched concurrently with `Task.WhenAll`. Each concurrent dispatch creates its own `IServiceScope` to avoid sharing `DbContext` instances across threads.

**Pipeline stage timing.** `IMetricsService` (introduced in 2D.5, registered in `MSOSync.Common`) exposes histogram recording. Four named histograms instrument the pipeline: `sync.pipeline.fetch_ms`, `sync.pipeline.compress_ms`, `sync.pipeline.send_ms`, `sync.pipeline.ack_ms`. Each stage wraps its work in a `Stopwatch` and records elapsed milliseconds. This is the data foundation for Phase 2F (OpenTelemetry / Prometheus export).

### Component 3 — Compression Abstraction (`MSOSync.Transport`)

`GzipCompressionService` is a concrete class used directly in `NodeHttpClient` and `TransportServiceExtensions`. After 2D.5, all compression goes through `ICompressionService`. The existing `GzipCompressionService` becomes the default `ICompressionService` implementation. A new `BrotliCompressionService` implements the same interface for nodes that advertise brotli support in their heartbeat. Per-node compression negotiation is driven by `ICompressionNegotiator`.

---

## `IAdaptivePollingService` Interface

Located in `MSOSync.Scheduler`. Registered as singleton.

```csharp
namespace MSOSync.Scheduler;

/// <summary>
/// Maintains per-node polling state and computes the next poll interval.
/// State is ephemeral (IMemoryCache) — resets on process restart.
/// </summary>
public interface IAdaptivePollingService
{
    /// <summary>
    /// Returns the interval to wait before the next poll cycle for this node.
    /// Callers await this duration before dispatching the next SyncJob tick.
    /// </summary>
    Task<TimeSpan> GetIntervalAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Records the outcome of a completed poll cycle.
    /// hadWork = true  → events were found and dispatched this cycle.
    /// hadWork = false → no events found (idle cycle).
    /// </summary>
    Task RecordActivityAsync(string nodeId, bool hadWork, CancellationToken ct = default);

    /// <summary>
    /// Records that a poll cycle ended in an error (exception or transport failure).
    /// Triggers exponential backoff with jitter for subsequent intervals.
    /// </summary>
    Task RecordErrorAsync(string nodeId, CancellationToken ct = default);

    /// <summary>
    /// Resets all state for a node. Called when a node is re-activated or
    /// transitions out of an error lifecycle state.
    /// </summary>
    Task ResetAsync(string nodeId, CancellationToken ct = default);
}
```

---

## `AdaptivePollingOptions` Class

Located in `MSOSync.Scheduler.Options`. Bound from `appsettings.json` section `"AdaptivePolling"`.

```csharp
namespace MSOSync.Scheduler.Options;

public sealed class AdaptivePollingOptions
{
    public const string Section = "AdaptivePolling";

    /// <summary>Floor: fastest poll rate, applied when node is continuously busy.</summary>
    public int MinIntervalSeconds { get; init; } = 5;

    /// <summary>Ceiling: slowest poll rate, applied when node has been idle for ActivityWindowMinutes.</summary>
    public int MaxIntervalSeconds { get; init; } = 300;

    /// <summary>Starting interval for a node with no history.</summary>
    public int BaseIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Multiplier applied per consecutive idle cycle.
    /// Interval grows: base × BackoffMultiplier^idleCount, capped at MaxIntervalSeconds.
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2.0;

    /// <summary>
    /// Multiplier applied per consecutive error cycle (independent of idle backoff).
    /// Applied on top of current interval: current × ErrorBackoffMultiplier^errorCount.
    /// </summary>
    public double ErrorBackoffMultiplier { get; init; } = 2.0;

    /// <summary>Maximum consecutive error backoffs before interval is capped at MaxIntervalSeconds.</summary>
    public int MaxErrorBackoffCount { get; init; } = 5;

    /// <summary>
    /// Jitter range applied to error-backoff intervals as a fraction of the computed interval.
    /// 0.20 = ±20% random jitter to prevent thundering herd on multi-node error recovery.
    /// </summary>
    public double ErrorJitterFraction { get; init; } = 0.20;

    /// <summary>
    /// Number of consecutive busy cycles before interval is reduced to MinIntervalSeconds.
    /// </summary>
    public int BusyThresholdCycles { get; init; } = 3;

    /// <summary>
    /// Number of consecutive idle cycles before backoff begins increasing.
    /// </summary>
    public int IdleThresholdCycles { get; init; } = 2;

    /// <summary>
    /// Cache entry lifetime for per-node state. Nodes inactive beyond this window
    /// are evicted and start from BaseIntervalSeconds on next access.
    /// </summary>
    public int ActivityWindowMinutes { get; init; } = 60;
}
```

---

## Adaptive Interval Algorithm

The algorithm runs inside `AdaptivePollingService.GetIntervalAsync`. It reads the per-node state record from `IMemoryCache`, computes the interval, and returns it. State is updated by `RecordActivityAsync` and `RecordErrorAsync` after each cycle.

### Per-Node State Record (`NodePollingState`)

```csharp
internal sealed record NodePollingState(
    int     ConsecutiveBusyCycles,   // incremented on hadWork=true, reset to 0 on idle or error
    int     ConsecutiveIdleCycles,   // incremented on hadWork=false, reset to 0 on busy
    int     ConsecutiveErrorCycles,  // incremented on RecordError, reset to 0 on RecordActivity
    bool    InErrorBackoff,          // true if last cycle was an error
    DateTimeOffset LastActivity      // UTC timestamp of last hadWork=true cycle
);
```

### Pseudocode

```
function GetInterval(nodeId, options) -> TimeSpan:

    state = cache.Get(nodeId) ?? NodePollingState.Default(options.BaseIntervalSeconds)

    // --- Error backoff path (highest priority) ---
    if state.InErrorBackoff:
        errorCount   = Clamp(state.ConsecutiveErrorCycles, 1, options.MaxErrorBackoffCount)
        rawInterval  = options.BaseIntervalSeconds * (options.ErrorBackoffMultiplier ^ errorCount)
        capped       = Min(rawInterval, options.MaxIntervalSeconds)
        jitterRange  = capped * options.ErrorJitterFraction
        jitter       = Random(-jitterRange, +jitterRange)
        return TimeSpan.FromSeconds(Clamp(capped + jitter, options.MinIntervalSeconds, options.MaxIntervalSeconds))

    // --- Busy path ---
    if state.ConsecutiveBusyCycles >= options.BusyThresholdCycles:
        return TimeSpan.FromSeconds(options.MinIntervalSeconds)

    // --- Idle backoff path ---
    if state.ConsecutiveIdleCycles >= options.IdleThresholdCycles:
        idleCount   = state.ConsecutiveIdleCycles - options.IdleThresholdCycles + 1
        rawInterval = options.BaseIntervalSeconds * (options.BackoffMultiplier ^ idleCount)
        return TimeSpan.FromSeconds(Min(rawInterval, options.MaxIntervalSeconds))

    // --- Default: no clear signal yet ---
    return TimeSpan.FromSeconds(options.BaseIntervalSeconds)


function RecordActivity(nodeId, hadWork, options):
    state = cache.Get(nodeId) ?? NodePollingState.Default(options.BaseIntervalSeconds)

    if hadWork:
        newState = state with {
            ConsecutiveBusyCycles  = state.ConsecutiveBusyCycles + 1,
            ConsecutiveIdleCycles  = 0,
            ConsecutiveErrorCycles = 0,
            InErrorBackoff         = false,
            LastActivity           = UtcNow
        }
    else:
        newState = state with {
            ConsecutiveBusyCycles  = 0,
            ConsecutiveIdleCycles  = state.ConsecutiveIdleCycles + 1,
            ConsecutiveErrorCycles = 0,
            InErrorBackoff         = false
        }

    cache.Set(nodeId, newState, slidingExpiry: options.ActivityWindowMinutes)


function RecordError(nodeId, options):
    state = cache.Get(nodeId) ?? NodePollingState.Default(options.BaseIntervalSeconds)

    newState = state with {
        ConsecutiveBusyCycles  = 0,
        ConsecutiveIdleCycles  = 0,
        ConsecutiveErrorCycles = state.ConsecutiveErrorCycles + 1,
        InErrorBackoff         = true
    }

    cache.Set(nodeId, newState, slidingExpiry: options.ActivityWindowMinutes)


function Reset(nodeId):
    cache.Remove(nodeId)
```

### Interval Convergence Guarantees

| Condition | Result |
|---|---|
| `BusyThresholdCycles` consecutive busy cycles | `MinIntervalSeconds` |
| `IdleThresholdCycles` + N consecutive idle cycles | `Min(BaseIntervalSeconds × BackoffMultiplier^N, MaxIntervalSeconds)` |
| 1 error cycle | `BaseIntervalSeconds × ErrorBackoffMultiplier¹ ± jitter` |
| `MaxErrorBackoffCount` error cycles | `MaxIntervalSeconds ± jitter` |
| Next busy cycle after error | Error state clears; busy path takes over |

---

## `AdaptivePollingOrchestrator` — Replacing `SyncJob`'s Fixed Timer

`AdaptivePollingOrchestrator` is a `BackgroundService` registered in `SyncSchedulerExtensions`. It replaces the fixed `PeriodicTimer` in `SyncJob.ExecuteAsync`.

**Design:**

- On startup, load all active node IDs from the database (one-time query via scoped `INodeMetadataService`).
- For each node, spawn a `Task` that loops: dispatch `SyncJob.RunTickAsync(nodeId, ct)`, record activity, sleep for `GetIntervalAsync(nodeId)`.
- A background refresh loop (every `NodeRefreshIntervalSeconds`, default 60 s) detects newly added nodes and adds them to the dispatch set. Decommissioned or rejected nodes are pruned.
- All per-node `Task` objects are tracked in a `ConcurrentDictionary<string, Task>`. On `StopAsync`, `CancellationToken` cancels all tasks; the orchestrator awaits `Task.WhenAll` with a 10-second drain timeout.

`SyncJob` is retained as the unit-of-work class. Its `ExecuteAsync` (fixed timer loop) is removed; only `RunTickAsync(CancellationToken ct)` is called by the orchestrator. `SyncJob` is demoted from `BackgroundService` to a plain scoped service.

---

## Pipeline Stage Changes

### Stage 1: Fetch (`SyncEngine.RunAsync`)

Unchanged in implementation. Instrumented: wrap `eventReader.ReadAsync` in a `Stopwatch`, record `sync.pipeline.fetch_ms` via `IMetricsService.RecordHistogram`.

### Stage 2: Route + Batch Creation

Unchanged. No instrumentation added at this stage (routing is not a bottleneck candidate in 2D.5).

### Stage 3: Parallel Channel Dispatch (modified)

**Before:**
```csharp
foreach (var batch in batches)
    await transport.SendBatchAsync(batch, events, ct);
```

**After:**
```csharp
// Group batches by target node
var byNode = batches.GroupBy(b => b.NodeId);

await Task.WhenAll(byNode.Select(group =>
    DispatchNodeBatchesAsync(group.Key, group.ToList(), events, scopeFactory, ct)));
```

`DispatchNodeBatchesAsync` creates one `IServiceScope` per node group. Batches within a single node are dispatched serially within that scope (preserving sequence order per channel). Batches for different nodes are dispatched in parallel across scopes. This avoids sharing `DbContext` across concurrent tasks.

**Why serial within a node:** Batch sequence numbers must be applied in order on the target. Parallelising within a node would require the target to buffer and reorder, which is not part of 2D.5.

Instrumented: wrap `transport.SendBatchAsync` in a `Stopwatch`, record `sync.pipeline.send_ms`.

### Stage 4: Acknowledgement

`AcknowledgementService.AcknowledgeOutgoingAsync` is instrumented: record `sync.pipeline.ack_ms`. No logic changes.

### Stage 5: Compression Gate (modified in `NodeHttpClient`)

**Before:** Every request is gzip-compressed unconditionally.

**After:**
```
if payload.Length >= compressionOptions.ThresholdBytes:
    compressed = compressionService.Compress(payload, compressionLevel)
    content.Headers.ContentEncoding.Add(compressionService.EncodingName)
else:
    // send raw — no Content-Encoding header
    compressed = payload
```

`sync.pipeline.compress_ms` is recorded only when compression is actually applied (skip overhead is not worth measuring).

---

## `ICompressionService` Abstraction

Located in `MSOSync.Transport`. Replaces all direct references to `GzipCompressionService`.

```csharp
namespace MSOSync.Transport;

/// <summary>
/// Content-encoding-agnostic compression contract.
/// Implementations: GzipCompressionService, BrotliCompressionService.
/// </summary>
public interface ICompressionService
{
    /// <summary>
    /// The HTTP Content-Encoding token this implementation produces (e.g. "gzip", "br").
    /// </summary>
    string EncodingName { get; }

    /// <summary>
    /// Compress <paramref name="data"/> using the configured CompressionLevel.
    /// </summary>
    byte[] Compress(byte[] data);

    /// <summary>
    /// Decompress <paramref name="data"/> compressed with this encoding.
    /// </summary>
    byte[] Decompress(byte[] data);
}
```

`GzipCompressionService` is updated to implement `ICompressionService`. The `Compress` method is updated to accept `CompressionLevel` from `CompressionOptions` instead of hardcoding `CompressionLevel.Optimal`.

`BrotliCompressionService` is a new class implementing `ICompressionService` using `System.IO.Compression.BrotliStream`. It is registered as a named service and resolved by `ICompressionNegotiator`.

### `ICompressionNegotiator`

```csharp
namespace MSOSync.Transport;

/// <summary>
/// Selects the appropriate ICompressionService for a given target node
/// based on the node's advertised compression capabilities from its most
/// recent heartbeat.
/// </summary>
public interface ICompressionNegotiator
{
    /// <summary>
    /// Returns the best ICompressionService for the given node.
    /// Falls back to gzip if the node has not advertised capabilities
    /// or if the advertised algorithm is unsupported.
    /// </summary>
    ICompressionService SelectFor(string nodeId);
}
```

Node compression capability is advertised in the heartbeat payload as an optional `string[] SupportedEncodings` field (e.g. `["gzip", "br"]`). The hub stores the most recently advertised value in a per-node cache entry (same `IMemoryCache` used by `NodeMetadataService`, keyed `node-compression:{nodeId}`). `ICompressionNegotiator` reads this cache entry and returns the highest-priority supported `ICompressionService`. Priority order: brotli > gzip > none.

---

## `CompressionOptions` Class

Located in `MSOSync.Transport`. Bound from `appsettings.json` section `"Compression"`.

```csharp
namespace MSOSync.Transport;

public sealed class CompressionOptions
{
    public const string Section = "Compression";

    /// <summary>
    /// Payloads smaller than this byte count are sent uncompressed.
    /// Default 1024 bytes: gzip overhead (~50 bytes header) plus CPU cost
    /// exceeds size savings for small payloads below this threshold.
    /// </summary>
    public int ThresholdBytes { get; init; } = 1024;

    /// <summary>
    /// Compression level applied when gzip is used.
    /// Fastest: lowest CPU, ~60% ratio. Optimal: balanced. SmallestSize: highest CPU, best ratio.
    /// </summary>
    public CompressionLevelOption Level { get; init; } = CompressionLevelOption.Fastest;
}

/// <summary>Maps to System.IO.Compression.CompressionLevel without a direct dependency on it in options.</summary>
public enum CompressionLevelOption
{
    Fastest,
    Optimal,
    SmallestSize
}
```

`CompressionLevelOption` is translated to `System.IO.Compression.CompressionLevel` inside `GzipCompressionService.Compress`.

---

## `IMetricsService` Interface

Located in `MSOSync.Common`. Registered as singleton. In 2D.5 the default implementation is `InMemoryMetricsService` (a thread-safe ring-buffer per histogram). Phase 2F will swap in an OpenTelemetry-backed implementation without changing call sites.

```csharp
namespace MSOSync.Common;

/// <summary>
/// Lightweight metrics sink. Records named histograms for pipeline stage timing.
/// Phase 2F replaces the default implementation with an OpenTelemetry-backed one.
/// </summary>
public interface IMetricsService
{
    /// <summary>Record a duration in milliseconds against a named histogram.</summary>
    void RecordHistogram(string name, double valueMs, IReadOnlyDictionary<string, string>? tags = null);

    /// <summary>Increment a named counter.</summary>
    void IncrementCounter(string name, IReadOnlyDictionary<string, string>? tags = null);
}
```

### Named Histograms (2D.5)

| Histogram Name | Recorded In | Tags |
|---|---|---|
| `sync.pipeline.fetch_ms` | `SyncEngine.RunAsync` after `ReadAsync` | `node_id` |
| `sync.pipeline.compress_ms` | `NodeHttpClient.SendAsync` when compression applied | `node_id`, `encoding` |
| `sync.pipeline.send_ms` | `SmartTransportService.SendBatchAsync` | `node_id`, `batch_id` |
| `sync.pipeline.ack_ms` | `AcknowledgementService.AcknowledgeOutgoingAsync` | `node_id`, `success` |
| `sync.adaptive.interval_s` | `AdaptivePollingService.GetIntervalAsync` | `node_id`, `state` (busy/idle/error/default) |

---

## `appsettings.json` Changes

Add the following two sections to `src/MSOSync.App/appsettings.json`:

```json
"AdaptivePolling": {
  "MinIntervalSeconds": 5,
  "MaxIntervalSeconds": 300,
  "BaseIntervalSeconds": 30,
  "BackoffMultiplier": 2.0,
  "ErrorBackoffMultiplier": 2.0,
  "MaxErrorBackoffCount": 5,
  "ErrorJitterFraction": 0.20,
  "BusyThresholdCycles": 3,
  "IdleThresholdCycles": 2,
  "ActivityWindowMinutes": 60
},
"Compression": {
  "ThresholdBytes": 1024,
  "Level": "Fastest"
}
```

The existing `"Sync"` section (`IntervalSeconds`, `PullIntervalSeconds`) is retained for `PullJob`. `IntervalSeconds` is no longer used by `SyncJob` after 2D.5 (superseded by `AdaptivePolling.BaseIntervalSeconds`) but is kept for backward compatibility and documents the old default.

---

## Registration Changes (`SyncSchedulerExtensions`, `TransportServiceExtensions`)

### `SyncSchedulerExtensions.AddSyncScheduler`

```csharp
// New
services.Configure<AdaptivePollingOptions>(config.GetSection(AdaptivePollingOptions.Section));
services.AddSingleton<IAdaptivePollingService, AdaptivePollingService>();
services.AddHostedService<AdaptivePollingOrchestrator>();

// Changed: SyncJob demoted from BackgroundService to scoped service
services.AddScoped<SyncJob>();   // was: services.AddHostedService<SyncJob>()
```

`AdaptivePollingOrchestrator` registers itself with `IWorkerStatusRegistry` using the name `"AdaptivePollingOrchestrator"`.

### `TransportServiceExtensions.AddTransportServices`

```csharp
// New
services.Configure<CompressionOptions>(config.GetSection(CompressionOptions.Section));
services.AddSingleton<ICompressionService, GzipCompressionService>();    // default
services.AddSingleton<BrotliCompressionService>();                        // named, for negotiator
services.AddSingleton<ICompressionNegotiator, CompressionNegotiator>();

// Changed: GzipCompressionService no longer registered as concrete singleton
// (NodeHttpClient now depends on ICompressionService and ICompressionNegotiator)
```

`NodeHttpClient` constructor changes from `GzipCompressionService compression` to `ICompressionNegotiator negotiator, IOptions<CompressionOptions> compressionOptions`.

---

## Testing

### Test Project: `MSOSync.SchedulerTests` (new or existing)

#### `AdaptivePollingServiceTests`

**Busy convergence test**
- Create `AdaptivePollingService` with `BusyThresholdCycles = 3`, `MinIntervalSeconds = 5`.
- Call `RecordActivity(nodeId, hadWork: true)` three times.
- Assert `GetInterval(nodeId)` returns `TimeSpan.FromSeconds(5)`.

**Idle backoff convergence test**
- Create service with `IdleThresholdCycles = 2`, `BackoffMultiplier = 2.0`, `BaseIntervalSeconds = 30`, `MaxIntervalSeconds = 300`.
- Call `RecordActivity(nodeId, hadWork: false)` repeatedly.
- Assert interval sequence: 30 s (first 2 idle cycles), 60 s (idle+1), 120 s (idle+2), 240 s (idle+3), 300 s (capped at idle+4).
- Assert interval never exceeds `MaxIntervalSeconds`.

**Error backoff with jitter test**
- Create service with `ErrorBackoffMultiplier = 2.0`, `BaseIntervalSeconds = 30`, `ErrorJitterFraction = 0.20`.
- Call `RecordError(nodeId)` once. Assert returned interval is within `[30*2 - jitter, 30*2 + jitter]`.
- Call `RecordError(nodeId)` again (count=2). Assert interval within `[30*4 - jitter, 30*4 + jitter]`.

**Error cap test**
- Set `MaxErrorBackoffCount = 5`. Call `RecordError` six times.
- Assert interval equals `MaxIntervalSeconds ± jitter` (clamped).

**Reset test**
- Record several busy cycles. Call `Reset(nodeId)`. Assert `GetInterval(nodeId)` returns `BaseIntervalSeconds`.

**Error-clears-on-activity test**
- Record 3 errors. Call `RecordActivity(nodeId, hadWork: true)`. Assert `GetInterval(nodeId)` returns `MinIntervalSeconds` (busy path, not error path).

### Test Project: `MSOSync.TransportTests` (new or existing)

#### `CompressionGateTests`

**Below-threshold test**
- Construct `NodeHttpClient` with `CompressionOptions.ThresholdBytes = 1024`.
- Send a payload of 512 bytes. Capture outgoing request. Assert `Content-Encoding` header is absent.

**Above-threshold test**
- Send a payload of 2048 bytes. Assert `Content-Encoding: gzip` header is present and body decompresses correctly.

**Threshold boundary test**
- Send a payload of exactly 1024 bytes. Assert compression is applied (threshold is inclusive lower bound for compression).

#### `CompressionServiceTests`

**GzipCompressionService round-trip**
- Compress and decompress a random 4096-byte payload. Assert byte equality.

**BrotliCompressionService round-trip**
- Compress and decompress a random 4096-byte payload. Assert byte equality.

**CompressionLevel option test**
- Compress a 4096-byte payload with `Fastest` and with `SmallestSize`. Assert `SmallestSize` output is smaller or equal (content-dependent; use a compressible test pattern).

#### `CompressionNegotiatorTests`

- Node with `SupportedEncodings: ["gzip", "br"]` → negotiator returns `BrotliCompressionService`.
- Node with `SupportedEncodings: ["gzip"]` → returns `GzipCompressionService`.
- Node with no cached capability → returns `GzipCompressionService` (default).

---

## Global Constraints

- `IAdaptivePollingService` lives in `MSOSync.Scheduler`. It has no dependency on `MSOSync.Transport` or `MSOSync.Engine`.
- Adaptive polling state is stored in `IMemoryCache` only. No EF entity, no DB table, no migration.
- Parallel channel dispatch creates one `IServiceScope` per node group. No scope is shared across concurrent tasks.
- `DbContext` is never passed across task boundaries. Each scope creates its own `DbContext` instance.
- `CompressionOptions.ThresholdBytes` is read from `IOptions<CompressionOptions>` — never hardcoded.
- `IMetricsService` in 2D.5 is `InMemoryMetricsService`. It records into a `ConcurrentDictionary<string, ConcurrentQueue<double>>` with a ring-buffer cap of 1000 entries per histogram name to bound memory usage.
- No new EF migrations are introduced by 2D.5.
- `BrotliCompressionService` uses `System.IO.Compression.BrotliStream`, available in .NET 9 BCL. No new NuGet packages required.
- `SyncJob.RunTickAsync` signature gains an optional `string? nodeId` parameter to support per-node lock key scoping (`LockNames.SyncEngine + ":" + nodeId`). The parameterless overload is retained for test backward compatibility.
- `PullJob` is not affected by adaptive polling in 2D.5. It retains its fixed `PullIntervalSeconds` timer. Adaptive pull is deferred to a future 2D task.
- The `IWorkerStatusRegistry` registration for `SyncJob` is moved to `AdaptivePollingOrchestrator`. `SyncJob` no longer self-registers.
- All public interfaces and options classes introduced in 2D.5 are `sealed` (where applicable) or `interface` with no default implementations. No abstract base classes.
- The `AdaptivePollingOrchestrator` must handle the case where zero active nodes are found at startup. In this case the refresh loop runs normally; per-node tasks are spawned as nodes become available.
- Jitter for error backoff uses `System.Random.Shared` (thread-safe in .NET 6+). No `new Random()` per call.
