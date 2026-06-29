# Epic 9C: Metrics APIs Design

**Goal:** Expose read-only metrics APIs — operational aggregates (fixed 24-hour window, no pagination) for the React dashboard, plus lightweight diagnostic passthroughs for DBA tooling.

**Architecture:** Single `IMetricsQueryService` scoped service following the Epic 9A query-service pattern. `MetricsController` is thin — one line per endpoint. Operational aggregate endpoints cached 30 seconds. Diagnostic endpoints not cached. No new migrations.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core 9 / EF Core 9.0.0 / xUnit 2.9.3 / FluentAssertions 6.12.2 / Moq 4.20.72 / SQLite (unit tests) / LocalDB + WebApplicationFactory (integration tests)

---

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings at all times
- Central Package Management — no inline `Version=` in `.csproj`
- `AsNoTracking()` on all EF queries
- **No N+1 queries** — no per-node or per-channel sub-queries inside loops; all aggregations via GROUP BY or scalar aggregates
- All endpoints: `[Authorize(Policy = "ViewerOrAbove")]`
- Primary metric endpoints cached 30 seconds: cache keys `"metrics:summary:v1"`, `"metrics:nodes:v1"`, `"metrics:channels:v1"`
- Diagnostic endpoints (`runtime`, `monitors`) not cached
- Fixed 24-hour window for all time-bounded aggregates: `DateTime cutoff = DateTime.UtcNow.AddHours(-24)`
- No filter classes, no FluentValidation validators, no `PagedResult<T>` — full lists returned
- Diagnostic endpoints support lightweight query-string filters: `?nodeId=` and `?metricName=`
- `OutgoingQueueDepth` = non-terminal `SyncOutgoingBatch` rows — **implementer checkpoint** (see Data Sources section)

---

## Architectural Rule

```
Operational Metrics  = Derived from authoritative business tables
                       (IncomingBatches, OutgoingBatches, BatchErrors, Nodes, DataEvents)

Diagnostic Metrics   = Raw passthrough from SyncMonitor and SyncRuntimeStats
```

---

## Entities Used (read-only)

| Entity | DbSet | Used For |
|--------|-------|----------|
| `SyncNode` | `Nodes` | Node count, ConnectivityStatus, GroupId, LastHeartbeat |
| `SyncIncomingBatch` | `IncomingBatches` | Queue depth, processed 24h, avg apply time, per-node/channel |
| `SyncOutgoingBatch` | `OutgoingBatches` | Queue depth (non-terminal status) |
| `SyncBatchError` | `BatchErrors` | Error count 24h |
| `SyncDataEvent` | `DataEvents` | Pending events (IsProcessed == false) |
| `SyncRuntimeStats` | `RuntimeStats` | CPU, heap, GC, uptime snapshots |
| `SyncMonitor` | `Monitors` | Arbitrary metric name/value pairs |

---

## Data Source Definitions

### IncomingQueueDepth
```
IncomingBatches WHERE Status == New (0) OR Status == Applying (1)
```

### OutgoingQueueDepth — IMPLEMENTER CHECKPOINT
`SyncOutgoingBatch.Status` is `byte`. The implementer must verify non-terminal byte values from the existing code (check SyncOutgoingBatchConfiguration, any `BatchStatus` or `OutgoingBatchStatus` enum, or usages in existing workers).

Recommended definition (confirm before implementing):
```
OutgoingQueueDepth = rows WHERE Status IN (New, Sending, Retry, Error)
Exclude:           rows WHERE Status == Acknowledged
```
Rationale: Error rows remain in queue because operators must act on them.

### BatchesProcessed24h
```
IncomingBatches WHERE AppliedTime >= UtcNow - 24h
```

### Errors24h
```
BatchErrors WHERE CreateTime >= UtcNow - 24h
```

**FK note:** `SyncBatchError.BatchId` is a FK to `SyncOutgoingBatch.BatchId` (not IncomingBatch). When joining to get NodeId for per-node error counts, join via `OutgoingBatches`.

### PendingEvents
```
DataEvents WHERE IsProcessed == false
```

### ErrorRatePercent (C# computed)
```csharp
double total = BatchesProcessed24h + Errors24h;
double ErrorRatePercent = total == 0 ? 0.0 : Math.Round(Errors24h * 100.0 / total, 2);
```

### ThroughputPerMinute (C# computed)
```csharp
double ThroughputPerMinute = Math.Round(BatchesProcessed24h / 1440.0, 2);
```

### ActiveNodes per Channel
```
COUNT DISTINCT NodeId FROM IncomingBatches WHERE ChannelId == x AND AppliedTime >= UtcNow - 24h
```

---

## File Structure

```
src/MSOSync.Metadata/Metrics/
    IMetricsQueryService.cs         ← new interface
    MetricsQueryService.cs          ← new implementation
    MetricsSummaryDto.cs            ← MetricsSummaryDto
    NodeMetricsDto.cs               ← NodeMetricsDto
    ChannelMetricsDto.cs            ← ChannelMetricsDto
    RuntimeMetricsDto.cs            ← RuntimeMetricsDto
    MonitorMetricDto.cs             ← MonitorMetricDto

src/MSOSync.Api/Controllers/
    MetricsController.cs            ← new controller

src/MSOSync.Metadata/
    MetadataServiceExtensions.cs    ← add IMetricsQueryService registration

tests/MSOSync.MetadataTests/Metrics/
    MetricsQueryServiceTests.cs     ← ~12 SQLite unit tests

tests/MSOSync.IntegrationTests/Metrics/
    MetricsTests.cs                 ← ~8 LocalDB integration tests
```

---

## DTOs

### MetricsSummaryDto

```csharp
// src/MSOSync.Metadata/Metrics/MetricsSummaryDto.cs
namespace MSOSync.Metadata.Metrics;

public sealed record MetricsSummaryDto(
    int      TotalNodes,
    int      ReachableNodes,
    int      DegradedNodes,
    int      UnreachableNodes,
    int      UnknownNodes,
    long     IncomingQueueDepth,
    long     OutgoingQueueDepth,
    long     BatchesProcessed24h,
    long     Errors24h,
    double   ErrorRatePercent,
    double   ThroughputPerMinute,
    DateTime GeneratedAt);
```

### NodeMetricsDto

```csharp
// src/MSOSync.Metadata/Metrics/NodeMetricsDto.cs
using MSOSync.Persistence;

namespace MSOSync.Metadata.Metrics;

public sealed record NodeMetricsDto(
    string             NodeId,
    string             GroupId,
    ConnectivityStatus ConnectivityStatus,
    long               IncomingQueueDepth,
    long               OutgoingQueueDepth,
    int                ProcessedBatches24h,
    int                Errors24h,
    double?            AvgApplyTimeMs,
    DateTime?          LastHeartbeat);
```

### ChannelMetricsDto

```csharp
// src/MSOSync.Metadata/Metrics/ChannelMetricsDto.cs
namespace MSOSync.Metadata.Metrics;

public sealed record ChannelMetricsDto(
    string  ChannelId,
    int     ActiveNodes,
    long    PendingEvents,
    long    PendingOutgoingBatches,
    long    ProcessedBatches24h,
    int     Errors24h,
    double  ThroughputPerMinute);
```

`ThroughputPerMinute` = `ProcessedBatches24h / 1440.0` (C#-computed per channel).

### RuntimeMetricsDto

```csharp
// src/MSOSync.Metadata/Metrics/RuntimeMetricsDto.cs
namespace MSOSync.Metadata.Metrics;

public sealed record RuntimeMetricsDto(
    string?  NodeId,
    long?    HeapUsed,
    long?    HeapMax,
    int?     ThreadCount,
    decimal? CpuPercent,
    long?    GcCount,
    long?    GcTimeMs,
    long?    UptimeMs,
    DateTime CreateTime);
```

`CreateTime` is non-nullable — schema guarantees it on SyncRuntimeStats.

### MonitorMetricDto

```csharp
// src/MSOSync.Metadata/Metrics/MonitorMetricDto.cs
namespace MSOSync.Metadata.Metrics;

public sealed record MonitorMetricDto(
    string?  NodeId,
    string?  MetricName,
    string?  MetricValue,
    DateTime CreateTime);
```

`CreateTime` is non-nullable — schema guarantees it on SyncMonitor.

---

## Interface

```csharp
// src/MSOSync.Metadata/Metrics/IMetricsQueryService.cs
namespace MSOSync.Metadata.Metrics;

public interface IMetricsQueryService
{
    Task<MetricsSummaryDto>                 GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<NodeMetricsDto>>     GetNodeMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<ChannelMetricsDto>>  GetChannelMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<RuntimeMetricsDto>>  GetRuntimeMetricsAsync(string? nodeId, CancellationToken ct);
    Task<IReadOnlyList<MonitorMetricDto>>   GetMonitorMetricsAsync(string? nodeId, string? metricName, CancellationToken ct);
}
```

---

## Implementation: MetricsQueryService

```csharp
// src/MSOSync.Metadata/Metrics/MetricsQueryService.cs
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Metrics;

public sealed class MetricsQueryService(AppDbContext db, IMemoryCache cache)
    : IMetricsQueryService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
    };
}
```

### GetSummaryAsync — cached "metrics:summary:v1"

```csharp
public async Task<MetricsSummaryDto> GetSummaryAsync(CancellationToken ct)
{
    if (cache.TryGetValue("metrics:summary:v1", out MetricsSummaryDto? cached))
        return cached!;

    var cutoff = DateTime.UtcNow.AddHours(-24);

    // Node connectivity counts
    var nodeStats = await db.Nodes.AsNoTracking()
        .GroupBy(_ => 1)
        .Select(g => new
        {
            Total       = g.Count(),
            Reachable   = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Reachable),
            Degraded    = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Degraded),
            Unreachable = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Unreachable),
            Unknown     = g.Count(n => n.ConnectivityStatus == ConnectivityStatus.Unknown)
        })
        .FirstOrDefaultAsync(ct);

    var incomingQueue     = await db.IncomingBatches.AsNoTracking()
        .CountAsync(b => b.Status == IncomingBatchStatus.New || b.Status == IncomingBatchStatus.Applying, ct);

    // IMPLEMENTER: replace terminal status check with confirmed non-terminal byte values
    var outgoingQueue     = await db.OutgoingBatches.AsNoTracking()
        .CountAsync(b => /* non-terminal status */ true, ct);

    var processed24h      = await db.IncomingBatches.AsNoTracking()
        .CountAsync(b => b.AppliedTime >= cutoff, ct);

    var errors24h         = await db.BatchErrors.AsNoTracking()
        .CountAsync(e => e.CreateTime >= cutoff, ct);

    double total          = processed24h + errors24h;
    double errorRate      = total == 0 ? 0.0 : Math.Round(errors24h * 100.0 / total, 2);
    double throughput     = Math.Round(processed24h / 1440.0, 2);

    var result = new MetricsSummaryDto(
        nodeStats?.Total       ?? 0,
        nodeStats?.Reachable   ?? 0,
        nodeStats?.Degraded    ?? 0,
        nodeStats?.Unreachable ?? 0,
        nodeStats?.Unknown     ?? 0,
        incomingQueue,
        outgoingQueue,
        processed24h,
        errors24h,
        errorRate,
        throughput,
        DateTime.UtcNow);

    cache.Set("metrics:summary:v1", result, CacheOptions);
    return result;
}
```

### GetNodeMetricsAsync — cached "metrics:nodes:v1"

Two queries: one for Node base data, one aggregated IncomingBatch stats and BatchError counts grouped by NodeId. Join in C#. No per-node sub-queries.

```csharp
public async Task<IReadOnlyList<NodeMetricsDto>> GetNodeMetricsAsync(CancellationToken ct)
{
    if (cache.TryGetValue("metrics:nodes:v1", out IReadOnlyList<NodeMetricsDto>? cached))
        return cached!;

    var cutoff = DateTime.UtcNow.AddHours(-24);

    var nodes = await db.Nodes.AsNoTracking()
        .Select(n => new { n.NodeId, n.GroupId, n.ConnectivityStatus, n.LastHeartbeat })
        .ToListAsync(ct);

    // Aggregate incoming queue depth per node
    var incomingByNode = await db.IncomingBatches.AsNoTracking()
        .GroupBy(b => b.NodeId)
        .Select(g => new
        {
            NodeId           = g.Key,
            QueueDepth       = g.Count(b => b.Status == IncomingBatchStatus.New || b.Status == IncomingBatchStatus.Applying),
            Processed24h     = g.Count(b => b.AppliedTime >= cutoff),
            AvgApplyTimeMs   = g.Where(b => b.AppliedTime >= cutoff && b.ApplyTimeMs.HasValue)
                                .Average(b => (double?)b.ApplyTimeMs)
        })
        .ToDictionaryAsync(x => x.NodeId, ct);

    // Aggregate errors per node (BatchError.BatchId → OutgoingBatch.BatchId, confirmed Epic 9A)
    var errorsByNode = await db.BatchErrors.AsNoTracking()
        .Where(e => e.CreateTime >= cutoff)
        .Join(db.OutgoingBatches, e => e.BatchId, b => b.BatchId, (e, b) => new { b.NodeId })
        .GroupBy(x => x.NodeId)
        .Select(g => new { NodeId = g.Key, Count = g.Count() })
        .ToDictionaryAsync(x => x.NodeId, x => x.Count, ct);

    // IMPLEMENTER: outgoing queue per node — grouped by NodeId, non-terminal status
    // var outgoingByNode = await db.OutgoingBatches...

    var result = nodes.Select(n =>
    {
        var inc = incomingByNode.TryGetValue(n.NodeId, out var i) ? i : null;
        return new NodeMetricsDto(
            n.NodeId,
            n.GroupId,
            n.ConnectivityStatus,
            inc?.QueueDepth      ?? 0L,
            0L, // outgoingQueueDepth — wire after IMPLEMENTER confirms OutgoingBatch status values
            inc?.Processed24h    ?? 0,
            errorsByNode.TryGetValue(n.NodeId, out var err) ? err : 0,
            inc?.AvgApplyTimeMs,
            n.LastHeartbeat);
    }).ToList();

    cache.Set("metrics:nodes:v1", (IReadOnlyList<NodeMetricsDto>)result, CacheOptions);
    return result;
}
```

### GetChannelMetricsAsync — cached "metrics:channels:v1"

Three aggregated queries (IncomingBatches, OutgoingBatches, DataEvents) grouped by ChannelId + errors joined by BatchId. No per-channel loops.

### GetRuntimeMetricsAsync — not cached

```csharp
public async Task<IReadOnlyList<RuntimeMetricsDto>> GetRuntimeMetricsAsync(string? nodeId, CancellationToken ct)
{
    var q = db.RuntimeStats.AsNoTracking();
    if (nodeId is not null) q = q.Where(r => r.NodeId == nodeId);

    return await q.OrderByDescending(r => r.CreateTime)
        .Select(r => new RuntimeMetricsDto(
            r.NodeId, r.HeapUsed, r.HeapMax, r.ThreadCount,
            r.CpuPercent, r.GcCount, r.GcTimeMs, r.UptimeMs,
            r.CreateTime!.Value))
        .ToListAsync(ct);
}
```

### GetMonitorMetricsAsync — not cached

```csharp
public async Task<IReadOnlyList<MonitorMetricDto>> GetMonitorMetricsAsync(
    string? nodeId, string? metricName, CancellationToken ct)
{
    var q = db.Monitors.AsNoTracking();
    if (nodeId     is not null) q = q.Where(m => m.NodeId     == nodeId);
    if (metricName is not null) q = q.Where(m => m.MetricName == metricName);

    return await q.OrderByDescending(m => m.CreateTime)
        .Select(m => new MonitorMetricDto(
            m.NodeId, m.MetricName, m.MetricValue, m.CreateTime!.Value))
        .ToListAsync(ct);
}
```

---

## Controller

```csharp
// src/MSOSync.Api/Controllers/MetricsController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Metrics;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/metrics")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class MetricsController(IMetricsQueryService metrics) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await metrics.GetSummaryAsync(ct));

    [HttpGet("nodes")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetNodes(CancellationToken ct)
        => Ok(await metrics.GetNodeMetricsAsync(ct));

    [HttpGet("channels")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetChannels(CancellationToken ct)
        => Ok(await metrics.GetChannelMetricsAsync(ct));

    [HttpGet("runtime")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetRuntime(
        [FromQuery] string? nodeId, CancellationToken ct)
        => Ok(await metrics.GetRuntimeMetricsAsync(nodeId, ct));

    [HttpGet("monitors")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetMonitors(
        [FromQuery] string? nodeId,
        [FromQuery] string? metricName,
        CancellationToken ct)
        => Ok(await metrics.GetMonitorMetricsAsync(nodeId, metricName, ct));
}
```

---

## DI Registration

In `MetadataServiceExtensions.AddMetadata()`, add:

```csharp
using MSOSync.Metadata.Metrics;

// Epic 9C — Metrics APIs
services.AddScoped<IMetricsQueryService, MetricsQueryService>();
```

---

## Testing

### Unit Tests (SQLite) — MetricsQueryServiceTests.cs (~12 tests)

Use SQLite `TestDbContext`. Seed Nodes with ConnectivityStatus values, IncomingBatches with Status and AppliedTime, BatchErrors with CreateTime, DataEvents with IsProcessed, RuntimeStats, Monitors.

Required tests:

1. `GetSummary_ReachableNodesCount` — seeds 2 Reachable, 1 Unreachable → TotalNodes = 3, ReachableNodes = 2
2. `GetSummary_IncomingQueueDepth` — seeds New + Applying + Applied → only New+Applying counted
3. `GetSummary_Errors24h_ExcludesOlderErrors` — error at 25h ago → not counted
4. `GetSummary_ErrorRatePercent_ZeroIfNoActivity` — no processed/errors → 0.0
5. `GetSummary_ThroughputPerMinute` — 1440 processed → 1.0 per minute
6. `GetNodeMetrics_ReturnsAllNodes` — each node has a row
7. `GetNodeMetrics_ProcessedBatches24h` — AppliedTime filter works
8. `GetNodeMetrics_AvgApplyTimeMs_Nullable` — node with no applied batches → null
9. `GetChannelMetrics_PendingEvents` — IsProcessed == false counted correctly
10. `GetChannelMetrics_ActiveNodes` — distinct NodeIds in 24h window
11. `GetRuntimeMetrics_FiltersOnNodeId` — only rows for requested node returned
12. `GetMonitorMetrics_FiltersOnMetricName` — only matching metricName rows returned

### Integration Tests (LocalDB) — MetricsTests.cs (~8 tests)

Required tests:

1. `GET /metrics/summary` — 200, non-null GeneratedAt
2. `GET /metrics/nodes` — 200, seeded node appears with correct ConnectivityStatus
3. `GET /metrics/channels` — 200, channel with seeded events appears
4. `GET /metrics/runtime` — 200, returns seeded RuntimeStats rows
5. `GET /metrics/runtime?nodeId=hub` — filtered to single node
6. `GET /metrics/monitors` — 200, returns seeded Monitor rows
7. `GET /metrics/monitors?nodeId=hub&metricName=cpu` — filtered correctly
8. Unauthorized request → 401

---

## Task Breakdown

| Task | Deliverable |
|------|-------------|
| 1 | DTOs (5 files in Metrics/); build clean |
| 2 | `IMetricsQueryService` + `MetricsQueryService` + SQLite unit tests; resolve OutgoingBatch status checkpoint; tests green |
| 3 | `MetricsController` + DI wire + integration tests; full suite green; commit |
