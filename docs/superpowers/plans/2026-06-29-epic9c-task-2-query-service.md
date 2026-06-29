# Task 2: IMetricsQueryService + MetricsQueryService + Unit Tests

**Part of:** [Epic 9C Plan](2026-06-29-epic9c-metrics-apis.md)

**Goal:** Create `IMetricsQueryService`, `MetricsQueryService`, and 12 SQLite unit tests. OutgoingBatch terminal status is confirmed: `BatchStatus.Acknowledged = 2` (byte) — use `b.Status != 2` for non-terminal rows.

**Files:**
- Create: `src/MSOSync.Metadata/Metrics/IMetricsQueryService.cs`
- Create: `src/MSOSync.Metadata/Metrics/MetricsQueryService.cs`
- Create: `tests/MSOSync.MetadataTests/Metrics/MetricsQueryServiceTests.cs`

**Interfaces:**
- Consumes (from Task 1):
  - `MetricsSummaryDto`, `NodeMetricsDto`, `ChannelMetricsDto`, `RuntimeMetricsDto`, `MonitorMetricDto`
  - All in namespace `MSOSync.Metadata.Metrics`
- Consumes: `MSOSync.Persistence.AppDbContext`, `MSOSync.Persistence.ConnectivityStatus`, `MSOSync.Persistence.IncomingBatchStatus`
- Consumes: `Microsoft.Extensions.Caching.Memory.IMemoryCache`
- Produces (used in Task 3):
  - `IMetricsQueryService` with 5 methods (see interface below)

**Key entity fields confirmed:**
- `SyncNode`: `NodeId`, `GroupId`, `SyncUrl`(required), `Status`(required), `ConnectivityStatus`, `LastHeartbeat`
- `SyncIncomingBatch`: `BatchId`, `NodeId`, `ChannelId`, `Status`(IncomingBatchStatus), `BatchSequence`, `SourceNodeId`, `ReceivedTime`, `AppliedTime`, `ApplyTimeMs`
- `SyncOutgoingBatch`: `BatchId`, `NodeId`, `ChannelId`, `Status`(byte), `BatchSequence`
- `SyncBatchError`: `ErrorId`, `BatchId`(FK→OutgoingBatch), `CreateTime`(DateTime, non-nullable)
- `SyncDataEvent`: `EventId`, `ChannelId`, `IsProcessed`
- `SyncRuntimeStats`: `StatId`, `HeapUsed`, `HeapMax`, `ThreadCount`, `CpuPercent`, `GcCount`, `GcTimeMs`, `UptimeMs`, `CreateTime` — **no NodeId**
- `SyncMonitor`: `SnapshotId`, `NodeId`(?), `MetricName`(?), `MetricValue`(?), `CreateTime`(?)
- DbSets: `db.Nodes`, `db.IncomingBatches`, `db.OutgoingBatches`, `db.BatchErrors`, `db.DataEvents`, `db.RuntimeStats`, `db.Monitors`

---

- [ ] **Step 1: Create IMetricsQueryService.cs**

Create `src/MSOSync.Metadata/Metrics/IMetricsQueryService.cs`:

```csharp
namespace MSOSync.Metadata.Metrics;

public interface IMetricsQueryService
{
    Task<MetricsSummaryDto>                GetSummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<NodeMetricsDto>>    GetNodeMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<ChannelMetricsDto>> GetChannelMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<RuntimeMetricsDto>> GetRuntimeMetricsAsync(CancellationToken ct);
    Task<IReadOnlyList<MonitorMetricDto>>  GetMonitorMetricsAsync(string? nodeId, string? metricName, CancellationToken ct);
}
```

- [ ] **Step 2: Create MetricsQueryService.cs**

Create `src/MSOSync.Metadata/Metrics/MetricsQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Metrics;

public sealed class MetricsQueryService(AppDbContext db, IMemoryCache cache)
    : IMetricsQueryService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30)
    };

    public async Task<MetricsSummaryDto> GetSummaryAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("metrics:summary:v1", out MetricsSummaryDto? cached))
            return cached!;

        var cutoff = DateTime.UtcNow.AddHours(-24);

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

        var incomingQueue = await db.IncomingBatches.AsNoTracking()
            .CountAsync(b => b.Status == IncomingBatchStatus.New || b.Status == IncomingBatchStatus.Applying, ct);

        var outgoingQueue = await db.OutgoingBatches.AsNoTracking()
            .CountAsync(b => b.Status != 2, ct); // 2 = BatchStatus.Acknowledged

        var processed24h = await db.IncomingBatches.AsNoTracking()
            .CountAsync(b => b.AppliedTime >= cutoff, ct);

        var errors24h = await db.BatchErrors.AsNoTracking()
            .CountAsync(e => e.CreateTime >= cutoff, ct);

        double total      = processed24h + errors24h;
        double errorRate  = total == 0 ? 0.0 : Math.Round(errors24h * 100.0 / total, 2);
        double throughput = Math.Round(processed24h / 1440.0, 2);

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

    public async Task<IReadOnlyList<NodeMetricsDto>> GetNodeMetricsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("metrics:nodes:v1", out IReadOnlyList<NodeMetricsDto>? cached))
            return cached!;

        var cutoff = DateTime.UtcNow.AddHours(-24);

        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.NodeId, n.GroupId, n.ConnectivityStatus, n.LastHeartbeat })
            .ToListAsync(ct);

        // Load to C# so AvgApplyTimeMs calculation avoids complex EF GroupBy translation
        var incomingRaw = await db.IncomingBatches.AsNoTracking()
            .Select(b => new { b.NodeId, b.Status, b.AppliedTime, b.ApplyTimeMs })
            .ToListAsync(ct);

        var incomingByNode = incomingRaw.GroupBy(b => b.NodeId).ToDictionary(
            g => g.Key,
            g =>
            {
                var applied  = g.Where(b => b.AppliedTime >= cutoff).ToList();
                var withTime = applied.Where(b => b.ApplyTimeMs.HasValue)
                                      .Select(b => (double)b.ApplyTimeMs!.Value).ToList();
                return (
                    QueueDepth:     (long)g.Count(b => b.Status == IncomingBatchStatus.New
                                                    || b.Status == IncomingBatchStatus.Applying),
                    Processed24h:   applied.Count,
                    AvgApplyTimeMs: withTime.Count > 0 ? (double?)withTime.Average() : null
                );
            });

        var outgoingByNode = await db.OutgoingBatches.AsNoTracking()
            .Where(b => b.Status != 2) // 2 = BatchStatus.Acknowledged
            .GroupBy(b => b.NodeId)
            .Select(g => new { NodeId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.NodeId, x => x.Count, ct);

        // Join BatchErrors → OutgoingBatches to get NodeId, then group by NodeId
        var errorsRaw = await db.BatchErrors.AsNoTracking()
            .Where(e => e.CreateTime >= cutoff)
            .Join(db.OutgoingBatches, e => e.BatchId, b => b.BatchId, (e, b) => b.NodeId)
            .ToListAsync(ct);
        var errorsByNode = errorsRaw.GroupBy(n => n).ToDictionary(g => g.Key, g => g.Count());

        var result = nodes.Select(n =>
        {
            var inc = incomingByNode.TryGetValue(n.NodeId, out var i) ? (ValueTuple<long, int, double?>?)i : null;
            return new NodeMetricsDto(
                n.NodeId,
                n.GroupId,
                n.ConnectivityStatus,
                inc?.Item1 ?? 0L,
                outgoingByNode.TryGetValue(n.NodeId, out var og) ? og : 0L,
                inc?.Item2 ?? 0,
                errorsByNode.TryGetValue(n.NodeId, out var err) ? err : 0,
                inc?.Item3,
                n.LastHeartbeat);
        }).ToList();

        cache.Set("metrics:nodes:v1", (IReadOnlyList<NodeMetricsDto>)result, CacheOptions);
        return result;
    }

    public async Task<IReadOnlyList<ChannelMetricsDto>> GetChannelMetricsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("metrics:channels:v1", out IReadOnlyList<ChannelMetricsDto>? cached))
            return cached!;

        var cutoff = DateTime.UtcNow.AddHours(-24);

        // Load to C# so distinct-NodeId count per channel is safe across EF providers
        var incomingRaw = await db.IncomingBatches.AsNoTracking()
            .Select(b => new { b.ChannelId, b.NodeId, b.AppliedTime })
            .ToListAsync(ct);

        var incoming = incomingRaw.GroupBy(b => b.ChannelId).ToDictionary(
            g => g.Key,
            g =>
            {
                var applied = g.Where(b => b.AppliedTime >= cutoff).ToList();
                return (
                    Processed24h:   (long)applied.Count,
                    ActiveNodes24h: applied.Select(b => b.NodeId).Distinct().Count()
                );
            });

        var outgoing = await db.OutgoingBatches.AsNoTracking()
            .Where(b => b.Status != 2) // 2 = BatchStatus.Acknowledged
            .GroupBy(b => b.ChannelId)
            .Select(g => new { ChannelId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.ChannelId, x => x.Count, ct);

        var events = await db.DataEvents.AsNoTracking()
            .Where(e => !e.IsProcessed)
            .GroupBy(e => e.ChannelId)
            .Select(g => new { ChannelId = g.Key, Count = (long)g.Count() })
            .ToDictionaryAsync(x => x.ChannelId, x => x.Count, ct);

        // Join BatchErrors → OutgoingBatches to get ChannelId
        var errorsRaw = await db.BatchErrors.AsNoTracking()
            .Where(e => e.CreateTime >= cutoff)
            .Join(db.OutgoingBatches, e => e.BatchId, b => b.BatchId, (e, b) => b.ChannelId)
            .ToListAsync(ct);
        var errors = errorsRaw.GroupBy(ch => ch).ToDictionary(g => g.Key, g => g.Count());

        var channelIds = new HashSet<string>(
            incoming.Keys.Concat(outgoing.Keys).Concat(events.Keys).Concat(errors.Keys));

        var result = channelIds.Select(ch =>
        {
            var inc       = incoming.TryGetValue(ch, out var i) ? (ValueTuple<long, int>?)i : null;
            var processed = inc?.Item1 ?? 0L;
            return new ChannelMetricsDto(
                ch,
                inc?.Item2 ?? 0,
                events.TryGetValue(ch, out var ev) ? ev : 0L,
                outgoing.TryGetValue(ch, out var og) ? og : 0L,
                processed,
                errors.TryGetValue(ch, out var err) ? err : 0,
                Math.Round(processed / 1440.0, 2));
        })
        .OrderBy(x => x.ChannelId)
        .ToList();

        cache.Set("metrics:channels:v1", (IReadOnlyList<ChannelMetricsDto>)result, CacheOptions);
        return result;
    }

    public async Task<IReadOnlyList<RuntimeMetricsDto>> GetRuntimeMetricsAsync(CancellationToken ct)
    {
        return await db.RuntimeStats.AsNoTracking()
            .OrderByDescending(r => r.CreateTime)
            .Select(r => new RuntimeMetricsDto(
                r.HeapUsed, r.HeapMax, r.ThreadCount,
                r.CpuPercent, r.GcCount, r.GcTimeMs, r.UptimeMs,
                r.CreateTime!.Value))
            .ToListAsync(ct);
    }

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
}
```

**Implementation note:** `GetNodeMetricsAsync` uses named tuples via `ValueTuple`. If the compiler complains about tuple element access via `.Item1`/`.Item2`/`.Item3`, replace the tuple with an anonymous record or a local struct. The pattern is straightforward — the implementer should prefer whichever form compiles cleanly.

- [ ] **Step 3: Build to verify service compiles**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build src/MSOSync.Metadata -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Create MetricsQueryServiceTests.cs**

Create `tests/MSOSync.MetadataTests/Metrics/MetricsQueryServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Metadata.Metrics;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Metrics;

public sealed class MetricsQueryServiceTests : IDisposable
{
    private readonly AppDbContext        _db;
    private readonly IMemoryCache        _cache;
    private readonly MetricsQueryService _sut;

    public MetricsQueryServiceTests()
    {
        _db    = TestDbContext.Create();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut   = new MetricsQueryService(_db, _cache);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static SyncNode Node(string id, ConnectivityStatus cs) => new()
    {
        NodeId  = id,
        GroupId = "g1",
        SyncUrl = $"http://{id}",
        Status  = "REGISTERED",
        ConnectivityStatus = cs
    };

    private static SyncOutgoingBatch OutBatch(long id, string nodeId, string channelId, byte status) => new()
    {
        BatchId       = id,
        BatchSequence = id,
        NodeId        = nodeId,
        ChannelId     = channelId,
        Status        = status
    };

    private static SyncIncomingBatch InBatch(long id, string nodeId, string channelId,
        IncomingBatchStatus status, DateTime? appliedTime = null, long? applyTimeMs = null) => new()
    {
        BatchId       = id,
        BatchSequence = id,
        NodeId        = nodeId,
        SourceNodeId  = nodeId,
        ChannelId     = channelId,
        Status        = status,
        ReceivedTime  = DateTime.UtcNow.AddHours(-2),
        AppliedTime   = appliedTime,
        ApplyTimeMs   = applyTimeMs
    };

    // ── GetSummaryAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_ReachableNodesCount()
    {
        _db.Nodes.AddRange(
            Node("n1", ConnectivityStatus.Reachable),
            Node("n2", ConnectivityStatus.Reachable),
            Node("n3", ConnectivityStatus.Unreachable));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.TotalNodes.Should().Be(3);
        result.ReachableNodes.Should().Be(2);
        result.UnreachableNodes.Should().Be(1);
        result.DegradedNodes.Should().Be(0);
        result.UnknownNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_IncomingQueueDepth()
    {
        _db.IncomingBatches.AddRange(
            InBatch(1L, "n1", "ch-1", IncomingBatchStatus.New),
            InBatch(2L, "n1", "ch-1", IncomingBatchStatus.Applying),
            InBatch(3L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-1)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.IncomingQueueDepth.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_Errors24h_ExcludesOlderErrors()
    {
        _db.OutgoingBatches.Add(OutBatch(1L, "n1", "ch-1", 2));
        await _db.SaveChangesAsync();

        _db.BatchErrors.AddRange(
            new SyncBatchError { BatchId = 1L, ErrorMessage = "old",   CreateTime = DateTime.UtcNow.AddHours(-25) },
            new SyncBatchError { BatchId = 1L, ErrorMessage = "recent", CreateTime = DateTime.UtcNow.AddHours(-1) });
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.Errors24h.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_ErrorRatePercent_ZeroIfNoActivity()
    {
        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.ErrorRatePercent.Should().Be(0.0);
        result.ThroughputPerMinute.Should().Be(0.0);
    }

    [Fact]
    public async Task GetSummary_ThroughputPerMinute()
    {
        // 144 batches applied → Math.Round(144 / 1440.0, 2) = 0.1
        var batches = Enumerable.Range(1, 144).Select(i =>
            InBatch(100 + i, "n1", "ch-1", IncomingBatchStatus.Applied,
                    appliedTime: DateTime.UtcNow.AddHours(-1))).ToList();
        _db.IncomingBatches.AddRange(batches);
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.BatchesProcessed24h.Should().Be(144);
        result.ThroughputPerMinute.Should().Be(0.1);
    }

    // ── GetNodeMetricsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetNodeMetrics_ReturnsAllNodes()
    {
        _db.Nodes.AddRange(
            Node("n1", ConnectivityStatus.Reachable),
            Node("n2", ConnectivityStatus.Degraded));
        await _db.SaveChangesAsync();

        var result = await _sut.GetNodeMetricsAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(x => x.NodeId).Should().BeEquivalentTo(new[] { "n1", "n2" });
    }

    [Fact]
    public async Task GetNodeMetrics_ProcessedBatches24h()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        _db.IncomingBatches.AddRange(
            InBatch(1L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-1)),
            InBatch(2L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-2)),
            InBatch(3L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-25)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetNodeMetricsAsync(CancellationToken.None);

        result.Single(x => x.NodeId == "n1").ProcessedBatches24h.Should().Be(2);
    }

    [Fact]
    public async Task GetNodeMetrics_AvgApplyTimeMs_Nullable()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        _db.IncomingBatches.Add(InBatch(1L, "n1", "ch-1", IncomingBatchStatus.New));
        await _db.SaveChangesAsync();

        var result = await _sut.GetNodeMetricsAsync(CancellationToken.None);

        result.Single().AvgApplyTimeMs.Should().BeNull();
    }

    // ── GetChannelMetricsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetChannelMetrics_PendingEvents()
    {
        _db.DataEvents.AddRange(
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch-1", EventType = 'I', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch-1", EventType = 'U', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch-1", EventType = 'D', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = true });
        await _db.SaveChangesAsync();

        var result = await _sut.GetChannelMetricsAsync(CancellationToken.None);

        result.Single(x => x.ChannelId == "ch-1").PendingEvents.Should().Be(2);
    }

    [Fact]
    public async Task GetChannelMetrics_ActiveNodes()
    {
        // 3 distinct nodes with batches applied within 24h; 1 outside 24h window
        _db.IncomingBatches.AddRange(
            InBatch(1L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-1)),
            InBatch(2L, "n2", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-2)),
            InBatch(3L, "n3", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-3)),
            InBatch(4L, "n4", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-25)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetChannelMetricsAsync(CancellationToken.None);

        result.Single(x => x.ChannelId == "ch-1").ActiveNodes.Should().Be(3);
    }

    // ── GetRuntimeMetricsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetRuntimeMetrics_ReturnsAllRows_OrderedDescending()
    {
        var older = DateTime.UtcNow.AddHours(-2);
        var newer = DateTime.UtcNow.AddHours(-1);
        _db.RuntimeStats.AddRange(
            new SyncRuntimeStats { HeapUsed = 100L, CreateTime = older },
            new SyncRuntimeStats { HeapUsed = 200L, CreateTime = newer });
        await _db.SaveChangesAsync();

        var result = await _sut.GetRuntimeMetricsAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].CreateTime.Should().BeCloseTo(newer, TimeSpan.FromSeconds(5));
        result[1].CreateTime.Should().BeCloseTo(older, TimeSpan.FromSeconds(5));
    }

    // ── GetMonitorMetricsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetMonitorMetrics_FiltersOnMetricName()
    {
        _db.Monitors.AddRange(
            new SyncMonitor { NodeId = "n1", MetricName = "cpu",    MetricValue = "50", CreateTime = DateTime.UtcNow },
            new SyncMonitor { NodeId = "n1", MetricName = "cpu",    MetricValue = "60", CreateTime = DateTime.UtcNow },
            new SyncMonitor { NodeId = "n1", MetricName = "memory", MetricValue = "70", CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetMonitorMetricsAsync(null, "cpu", CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(x => x.MetricName.Should().Be("cpu"));
    }
}
```

- [ ] **Step 5: Run the unit tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~Metrics" -c Debug -v normal
```

Expected: 12 tests pass, 0 fail.

- [ ] **Step 6: Build the full solution**

```powershell
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Metadata/Metrics/IMetricsQueryService.cs
git add src/MSOSync.Metadata/Metrics/MetricsQueryService.cs
git add tests/MSOSync.MetadataTests/Metrics/MetricsQueryServiceTests.cs
git commit -m "feat(9c): add IMetricsQueryService + MetricsQueryService + 12 SQLite unit tests"
```
