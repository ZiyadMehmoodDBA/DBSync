# Task 3: Topology + Overview Query Optimization

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate full-entity materialisation and multiple per-bucket `CountAsync` calls across four services: `TopologyQueryService`, `ClusterSummaryQueryService`, `DashboardQueryService`, and `BatchErrorQueryService`. Add cursor pagination to `GetGroupNodesAsync`. Add `nodeIdFilter` overload to `ITopologyQueryService`. Add dashboard summary snapshot cache.

**Prerequisites:** T1 (M038 migration indexes) is recommended but not blocking. T2 must be done before T3 because T3 needs `CursorSigner.EncodeString`/`DecodeString`.

## Files

- Modify: `src/MSOSync.Metadata/Topology/ITopologyQueryService.cs`
- Modify: `src/MSOSync.Metadata/Topology/TopologyQueryService.cs`
- Modify: `src/MSOSync.Api/Controllers/TopologyController.cs`
- Modify: `src/MSOSync.Metadata/Operations/Cluster/ClusterSummaryQueryService.cs`
- Modify: `src/MSOSync.Metadata/Dashboard/DashboardQueryService.cs`
- Create: `src/MSOSync.Metadata/Dashboard/DashboardSummaryCache.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Modify: `src/MSOSync.App/appsettings.json`
- Modify: `src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs`
- Create: `tests/MSOSync.MetadataTests/Scale/TopologyOptimizationTests.cs`

## Interfaces

**Produces:**

```csharp
// ITopologyQueryService — new overload:
Task<TopologyGraphDto> GetTopologyGraphAsync(string[]? nodeIdFilter, CancellationToken ct);
// Original becomes a default-calling shorthand implemented in the interface or service.

// GetGroupNodesAsync new signature:
Task<CursorPageResult<TopologyGroupNodeDto>> GetGroupNodesAsync(
    string groupId, string? cursor, int pageSize, CancellationToken ct);
```

## Context on Existing Code

**`TopologyQueryService.GetTopologyGraphAsync`** (line 43–124 of `TopologyQueryService.cs`) currently does 4 DB round-trips and loads all nodes projected to `{ GroupId, ConnectivityStatus }`. This projection is OK, but there is no `nodeIdFilter` path. The change adds the filter overload and the original no-arg overload becomes a delegate.

**`ClusterSummaryQueryService.QueryNodeStatesAsync`** (line 30–47) projects to `{ LifecycleState, MaintenanceMode }` and then counts in C# using `.Count()`. At 1000 nodes this allocates ~1000 anonymous objects. Replace with a single `GROUP BY` query.

**`DashboardQueryService.GetSummaryAsync`** (line 12–37) issues 5 separate `CountAsync` calls on `sync_node`, 4 of which filter by `connectivity_status`. Replace with one `GROUP BY connectivity_status` query and add a snapshot cache.

**`BatchErrorQueryService.GetBatchErrorSummaryAsync`** (line 69–88) issues 3 separate `CountAsync` calls. Replace with one `GROUP BY` query.

**`TopologyQueryService.GetGroupNodesAsync`** (line 201–230) loads unbounded results with no pagination. Change to cursor-paginated with `string? cursor, int pageSize` parameters; update `ITopologyQueryService` signature and `TopologyController`.

## Steps

- [ ] **Step 1: Write failing tests**

Create `tests/MSOSync.MetadataTests/Scale/TopologyOptimizationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Common.Pagination;
using MSOSync.Metadata.Dashboard;
using MSOSync.Metadata.BatchErrors;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Topology;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Scale;

// ─── ClusterSummaryQueryService ──────────────────────────────────────────────

public sealed class ClusterSummaryProjectionTests
{
    [Fact]
    public async Task QueryNodeStates_GroupByReturnsCorrectCounts()
    {
        var db  = TestDbContext.Create();
        var svc = new ClusterSummaryQueryService(db);

        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "n1", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Active,  MaintenanceMode = false },
            new SyncNode { NodeId = "n2", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Active,  MaintenanceMode = true  },
            new SyncNode { NodeId = "n3", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Draining, MaintenanceMode = false },
            new SyncNode { NodeId = "n4", GroupId = "g1", SyncUrl = "http://x",
                LifecycleState = NodeLifecycleState.Disabled, MaintenanceMode = false });
        await db.SaveChangesAsync();

        // We can't call QueryNodeStatesAsync directly (it's private), so call GetSummaryAsync
        // and check NodeCounts on the result.
        var summary = await svc.GetSummaryAsync();

        summary.NodeCounts.Total.Should().Be(4);
        summary.NodeCounts.Active.Should().Be(1);       // Active && !Maintenance
        summary.NodeCounts.Maintenance.Should().Be(1);  // MaintenanceMode == true
        summary.NodeCounts.Draining.Should().Be(1);
        summary.NodeCounts.Offline.Should().Be(1);      // Disabled && !Maintenance
    }
}

// ─── DashboardQueryService ────────────────────────────────────────────────────

public sealed class DashboardSummaryOptimizationTests
{
    private static (DashboardQueryService Svc, AppDbContext Db) Make()
    {
        var db      = TestDbContext.Create();
        var cache   = new DashboardSummaryCache(new MemoryCache(new MemoryCacheOptions()),
                          Options.Create(new MSOSync.Metadata.Options.DashboardOptions()));
        var auditRepo = new TestPlatformRepository<SyncAudit>(db);
        var svc     = new DashboardQueryService(db, auditRepo, cache);
        return (svc, db);
    }

    [Fact]
    public async Task GetSummaryAsync_GroupByConnectivityStatus_CountsCorrectly()
    {
        var (svc, db) = Make();
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "n1", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Reachable   },
            new SyncNode { NodeId = "n2", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Reachable   },
            new SyncNode { NodeId = "n3", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Degraded    },
            new SyncNode { NodeId = "n4", GroupId = "g1", SyncUrl = "http://x",
                ConnectivityStatus = ConnectivityStatus.Unreachable });
        await db.SaveChangesAsync();

        var dto = await svc.GetSummaryAsync(default);

        dto.TotalNodes.Should().Be(4);
        dto.ReachableNodes.Should().Be(2);
        dto.DegradedNodes.Should().Be(1);
        dto.UnreachableNodes.Should().Be(1);
        dto.UnknownNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetSummaryAsync_CacheHit_DoesNotHitDb()
    {
        var (svc, _) = Make();
        // First call populates cache
        var first = await svc.GetSummaryAsync(default);
        // Second call must return same GeneratedAt (from cache)
        var second = await svc.GetSummaryAsync(default);

        second.GeneratedAt.Should().Be(first.GeneratedAt);
    }
}

// ─── BatchErrorQueryService ───────────────────────────────────────────────────

public sealed class BatchErrorSummaryGroupByTests
{
    [Fact]
    public async Task GetBatchErrorSummaryAsync_SingleQuery_CorrectCounts()
    {
        var db         = TestDbContext.Create();
        var classifier = new ErrorSeverityClassifier();
        var svc        = new BatchErrorQueryService(db, classifier);

        db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchId = 1L, BatchSequence = 1L,
            NodeId = "n1", ChannelId = "ch1", Status = 0
        });
        await db.SaveChangesAsync();

        db.BatchErrors.AddRange(
            new SyncBatchError { BatchId = 1L, ConflictType = "DuplicateKey",   ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = "Timeout",         ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = "Deadlock",        ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = "MetadataMissing", ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow },
            new SyncBatchError { BatchId = 1L, ConflictType = null,              ErrorMessage = "e", RetryCount = 0, CreateTime = DateTime.UtcNow });
        await db.SaveChangesAsync();

        var dto = await svc.GetBatchErrorSummaryAsync(null, null, null, default);

        dto.Info.Should().Be(1);
        dto.Warning.Should().Be(2);
        dto.Critical.Should().Be(2);
        dto.Total.Should().Be(5);
    }
}

// ─── TopologyQueryService group node cursor ───────────────────────────────────

public sealed class TopologyGroupNodeCursorTests
{
    private static TopologyQueryService MakeSvc(out AppDbContext db)
    {
        var ctx   = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var signer = new CursorSigner(new byte[32]);
        db = ctx;
        return new TopologyQueryService(ctx, cache, signer);
    }

    [Fact]
    public async Task GetGroupNodesAsync_FirstPage_ReturnsPageSizeItems()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 5; i++)
            db.Nodes.Add(new SyncNode
            {
                NodeId = $"node-{i:D3}", GroupId = "g1",
                SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active
            });
        await db.SaveChangesAsync();

        var page1 = await svc.GetGroupNodesAsync("g1", null, 2, default);

        page1.Items.Should().HaveCount(2);
        page1.HasMore.Should().BeTrue();
        page1.NextCursor.Should().NotBeNull();
        page1.Items[0].NodeId.Should().Be("node-001");
    }

    [Fact]
    public async Task GetGroupNodesAsync_SubsequentPage_DoesNotDuplicate()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        for (int i = 1; i <= 4; i++)
            db.Nodes.Add(new SyncNode
            {
                NodeId = $"node-{i:D3}", GroupId = "g1",
                SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active
            });
        await db.SaveChangesAsync();

        var page1 = await svc.GetGroupNodesAsync("g1", null, 2, default);
        var page2 = await svc.GetGroupNodesAsync("g1", page1.NextCursor, 2, default);

        page1.Items.Select(n => n.NodeId)
            .Intersect(page2.Items.Select(n => n.NodeId))
            .Should().BeEmpty();
        page2.Items[0].NodeId.Should().Be("node-003");
    }

    [Fact]
    public async Task GetGroupNodesAsync_LastPage_HasMoreFalse()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().Add(new SyncNodeGroup { GroupId = "g1" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "node-001", GroupId = "g1", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active },
            new SyncNode { NodeId = "node-002", GroupId = "g1", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active });
        await db.SaveChangesAsync();

        var result = await svc.GetGroupNodesAsync("g1", null, 10, default);

        result.HasMore.Should().BeFalse();
        result.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetTopologyGraphAsync_WithNodeIdFilter_OnlyReturnsRelevantGroups()
    {
        var svc = MakeSvc(out var db);
        db.Set<SyncNodeGroup>().AddRange(
            new SyncNodeGroup { GroupId = "g1" },
            new SyncNodeGroup { GroupId = "g2" });
        db.Nodes.AddRange(
            new SyncNode { NodeId = "node-001", GroupId = "g1", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active },
            new SyncNode { NodeId = "node-002", GroupId = "g2", SyncUrl = "http://x", LifecycleState = NodeLifecycleState.Active });
        await db.SaveChangesAsync();

        // Filter to only g1 nodes
        var result = await svc.GetTopologyGraphAsync(new[] { "node-001" }, default);

        // Should only include groups relevant to the filtered nodes
        result.Nodes.Should().NotBeEmpty();
        result.Nodes.Any(n => n.GroupId == "g1").Should().BeTrue();
    }
}
```

- [ ] **Step 2: Run tests — confirm they fail**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj \
  --filter "FullyQualifiedName~TopologyOptimizationTests|ClusterSummaryProjection|DashboardSummaryOptimization|BatchErrorSummaryGroupBy|TopologyGroupNodeCursor" -v normal
```

Expected: compile errors (new signatures not yet defined).

- [ ] **Step 3: Update `ITopologyQueryService` signatures**

Replace the contents of `src/MSOSync.Metadata/Topology/ITopologyQueryService.cs`:

```csharp
using MSOSync.Common.Pagination;

namespace MSOSync.Metadata.Topology;

public interface ITopologyQueryService
{
    /// <summary>Full graph, no filter.</summary>
    Task<TopologyGraphDto> GetTopologyGraphAsync(CancellationToken ct)
        => GetTopologyGraphAsync(null, ct);

    /// <summary>Graph optionally filtered to groups containing any of the given node IDs.</summary>
    Task<TopologyGraphDto> GetTopologyGraphAsync(string[]? nodeIdFilter, CancellationToken ct);

    Task<TopologySummaryDto>              GetTopologySummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupDto>> GetGroupsAsync(CancellationToken ct);
    Task<TopologyGroupDto?>               GetGroupAsync(string groupId, CancellationToken ct);

    /// <summary>Cursor-paginated group membership. pageSize max 500.</summary>
    Task<CursorPageResult<TopologyGroupNodeDto>> GetGroupNodesAsync(
        string groupId, string? cursor, int pageSize, CancellationToken ct);
}
```

- [ ] **Step 4: Update `TopologyQueryService`**

Open `src/MSOSync.Metadata/Topology/TopologyQueryService.cs`. Make the following changes:

**4a. Change constructor** to accept `CursorSigner`:

```csharp
using MSOSync.Metadata.Pagination;

public sealed class TopologyQueryService(AppDbContext db, IMemoryCache cache, CursorSigner cursorSigner)
    : ITopologyQueryService
```

**4b. Replace `GetTopologyGraphAsync(CancellationToken ct)` with the overloads:**

Remove the existing `GetTopologyGraphAsync` method (lines 41–124) and replace with:

```csharp
// ── GetTopologyGraphAsync (with optional nodeIdFilter) ────────────────────────
public async Task<TopologyGraphDto> GetTopologyGraphAsync(
    string[]? nodeIdFilter, CancellationToken ct)
{
    // Only cache the unfiltered full-graph result
    bool useCache = nodeIdFilter is null or { Length: 0 };
    const string cacheKey = "topology:graph";

    if (useCache && cache.TryGetValue(cacheKey, out TopologyGraphDto? cached))
        return cached!;

    // Round-trip 1: groups (optionally filtered by nodeIdFilter)
    List<string>? filteredGroupIds = null;
    if (!useCache)
    {
        filteredGroupIds = await db.Nodes.AsNoTracking()
            .Where(n => nodeIdFilter!.Contains(n.NodeId))
            .Select(n => n.GroupId)
            .Distinct()
            .ToListAsync(ct);
    }

    var groupsQ = db.NodeGroups.AsNoTracking()
        .Select(g => new { g.GroupId, g.GroupName });
    if (filteredGroupIds is not null)
        groupsQ = groupsQ.Where(g => filteredGroupIds.Contains(g.GroupId));
    var groups = await groupsQ.ToListAsync(ct);

    // Round-trip 2: node member counts per group (projection-only, GROUP BY in SQL)
    var memberCountsQ = db.Nodes.AsNoTracking();
    if (filteredGroupIds is not null)
        memberCountsQ = memberCountsQ.Where(n => filteredGroupIds.Contains(n.GroupId));

    var memberData = await memberCountsQ
        .Select(n => new { n.GroupId, n.ConnectivityStatus })
        .ToListAsync(ct);

    // Round-trip 3: routers
    var routersQ = db.Routers.AsNoTracking()
        .Select(r => new { r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup, r.Enabled });
    if (filteredGroupIds is not null)
        routersQ = routersQ.Where(r =>
            filteredGroupIds.Contains(r.SourceNodeGroup) ||
            filteredGroupIds.Contains(r.TargetNodeGroup));
    var routers = await routersQ.ToListAsync(ct);

    // Round-trip 4: trigger-router join
    var routerIds = routers.Select(r => r.RouterId).ToList();
    var joinRows = await db.TriggerRouters.AsNoTracking()
        .Where(tr => routerIds.Contains(tr.RouterId))
        .Join(db.Triggers,
              tr => tr.TriggerId,
              t  => t.TriggerId,
              (tr, t) => new { tr.TriggerId, tr.RouterId, t.ChannelId })
        .ToListAsync(ct);

    // Build dictionaries in C# (bounded by number of groups/routers, not node count)
    var channelsByRouter = joinRows
        .GroupBy(x => x.RouterId)
        .ToDictionary(g => g.Key,
                      g => (IReadOnlyList<string>)g.Select(x => x.ChannelId).Distinct().ToList());

    var routerSourceByRouterId = routers.ToDictionary(r => r.RouterId, r => r.SourceNodeGroup);
    var statsByGroup = joinRows
        .Where(x => routerSourceByRouterId.ContainsKey(x.RouterId))
        .GroupBy(x => routerSourceByRouterId[x.RouterId])
        .ToDictionary(
            g => g.Key,
            g => (TriggerCount: g.Select(x => x.TriggerId).Distinct().Count(),
                  ChannelCount: g.Select(x => x.ChannelId).Distinct().Count()));

    var statusByGroup = memberData
        .GroupBy(n => n.GroupId)
        .ToDictionary(g => g.Key, g => g.Select(x => x.ConnectivityStatus).ToList());

    var nodeDtos = groups.Select(g =>
    {
        var statuses = statusByGroup.TryGetValue(g.GroupId, out var s)
            ? (IReadOnlyList<ConnectivityStatus>)s : [];
        var (trigCount, chanCount) = statsByGroup.TryGetValue(g.GroupId, out var gs) ? gs : (0, 0);
        return new TopologyGraphNodeDto(
            $"group:{g.GroupId}", g.GroupId, g.GroupName ?? g.GroupId,
            AggregateConnectivity(statuses), statuses.Count, trigCount, chanCount);
    }).ToList();

    var edgeDtos = routers.Select(r => new TopologyGraphEdgeDto(
        $"router:{r.RouterId}",
        $"group:{r.SourceNodeGroup}",
        $"group:{r.TargetNodeGroup}",
        channelsByRouter.TryGetValue(r.RouterId, out var ch) ? ch : [],
        r.Enabled)).ToList();

    int totalNodes  = nodeDtos.Sum(n => n.MemberCount);
    int onlineNodes = nodeDtos.Count(n => n.Status == ConnectivityStatus.Reachable);

    var result = new TopologyGraphDto(
        nodeDtos, edgeDtos,
        new TopologyGraphMetaDto(groups.Count, totalNodes, onlineNodes, DateTimeOffset.UtcNow));

    if (useCache)
        cache.Set(cacheKey, result, CacheOptions);

    return result;
}
```

**4c. Replace `GetGroupNodesAsync` with cursor-paginated version:**

Remove the existing `GetGroupNodesAsync` method (lines 201–230) and replace with:

```csharp
// ── GetGroupNodesAsync (cursor-paginated) ─────────────────────────────────────
public async Task<CursorPageResult<TopologyGroupNodeDto>> GetGroupNodesAsync(
    string groupId, string? cursor, int pageSize, CancellationToken ct)
{
    pageSize = Math.Clamp(pageSize, 1, 500);

    var q = db.Nodes.AsNoTracking()
        .Where(n => n.GroupId == groupId)
        .OrderBy(n => n.NodeId);

    if (cursor is not null)
    {
        var (cursorNodeId, _) = cursorSigner.DecodeString(cursor);
        if (!string.IsNullOrEmpty(cursorNodeId))
            q = (IOrderedQueryable<SyncNode>)q.Where(
                n => string.Compare(n.NodeId, cursorNodeId, StringComparison.Ordinal) > 0);
    }

    var rows = await q
        .Take(pageSize + 1)
        .Select(n => new TopologyGroupNodeDto(
            n.NodeId,
            n.LifecycleState,
            n.ConnectivityStatus,
            n.LastHeartbeat,
            n.LastProbeLatencyMs,
            n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode))
        .ToListAsync(ct);

    var hasMore = rows.Count > pageSize;
    if (hasMore) rows = rows.Take(pageSize).ToList();

    string? nextCursor = hasMore
        ? cursorSigner.EncodeString(rows[^1].NodeId, DateTime.UtcNow.Ticks)
        : null;

    return new CursorPageResult<TopologyGroupNodeDto>(rows.AsReadOnly(), nextCursor, hasMore, null);
}
```

Keep `GetTopologySummaryAsync`, `GetGroupsAsync`, and `GetGroupAsync` unchanged (they are already correct).

- [ ] **Step 5: Update `TopologyController`**

Open `src/MSOSync.Api/Controllers/TopologyController.cs`. Replace `GetGraph` and `GetGroupNodes` actions:

```csharp
using MSOSync.Common.Pagination;

[HttpGet("graph")]
[ProducesResponseType(typeof(TopologyGraphDto), 200)]
[ProducesResponseType(400)]
public async Task<IActionResult> GetGraph(
    [FromQuery] string? nodeIds = null,
    CancellationToken ct = default)
{
    string[]? filter = null;
    if (!string.IsNullOrWhiteSpace(nodeIds))
    {
        filter = nodeIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (filter.Length > 50)
            return BadRequest(new { error = "TooManyNodeIds", message = "Maximum 50 node IDs allowed in nodeIds filter." });
    }

    return Ok(await topology.GetTopologyGraphAsync(filter, ct));
}

[HttpGet("groups/{groupId}/nodes")]
[ProducesResponseType(typeof(CursorPageResult<TopologyGroupNodeDto>), 200)]
[ProducesResponseType(400)]
public async Task<IActionResult> GetGroupNodes(
    string groupId,
    [FromQuery] string? cursor   = null,
    [FromQuery] int     pageSize = 100,
    CancellationToken ct = default)
{
    pageSize = Math.Clamp(pageSize, 1, 500);
    try
    {
        return Ok(await topology.GetGroupNodesAsync(groupId, cursor, pageSize, ct));
    }
    catch (ArgumentException ex)
    {
        return BadRequest(new { error = "InvalidCursorToken", message = ex.Message });
    }
}
```

- [ ] **Step 6: Rewrite `ClusterSummaryQueryService.QueryNodeStatesAsync`**

Open `src/MSOSync.Metadata/Operations/Cluster/ClusterSummaryQueryService.cs`. Replace the `QueryNodeStatesAsync` method (lines 30–47) with:

```csharp
private async Task<NodeStateCountsDto> QueryNodeStatesAsync(CancellationToken ct)
{
    // Single GROUP BY query; returns at most ~(distinct states) * 2 rows regardless of node count
    var groups = await db.Nodes
        .AsNoTracking()
        .GroupBy(n => new { n.LifecycleState, n.MaintenanceMode })
        .Select(g => new
        {
            g.Key.LifecycleState,
            g.Key.MaintenanceMode,
            Count = g.Count()
        })
        .ToListAsync(ct);

    var total       = groups.Sum(g => g.Count);
    var maintenance = groups.Where(g => g.MaintenanceMode).Sum(g => g.Count);
    var active      = groups
        .Where(g => g.LifecycleState == NodeLifecycleState.Active && !g.MaintenanceMode)
        .Sum(g => g.Count);
    var draining    = groups
        .Where(g => g.LifecycleState == NodeLifecycleState.Draining)
        .Sum(g => g.Count);
    var offline     = groups
        .Where(g => !g.MaintenanceMode
                 && g.LifecycleState != NodeLifecycleState.Active
                 && g.LifecycleState != NodeLifecycleState.Draining)
        .Sum(g => g.Count);

    return new NodeStateCountsDto(total, active, maintenance, draining, offline);
}
```

- [ ] **Step 7: Create `DashboardSummaryCache`**

Create `src/MSOSync.Metadata/Dashboard/DashboardSummaryCache.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Metadata.Options;

namespace MSOSync.Metadata.Dashboard;

/// <summary>
/// In-process snapshot cache for DashboardSummaryDto.
/// TTL is configurable via Dashboard:SummaryTtlSeconds (default 30).
/// Cache key: "dashboard:summary" (single-tenant; for multi-tenant, key per tenant).
/// </summary>
public sealed class DashboardSummaryCache(
    IMemoryCache                cache,
    IOptions<DashboardOptions>  options)
{
    private const string CacheKey = "dashboard:summary";

    public async Task<DashboardSummaryDto> GetOrCreateAsync(
        Func<CancellationToken, Task<DashboardSummaryDto>> factory,
        CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out DashboardSummaryDto? cached))
            return cached!;

        var result = await factory(ct);

        var ttl = TimeSpan.FromSeconds(options.Value.SummaryTtlSeconds);
        cache.Set(CacheKey, result, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = ttl
        });

        return result;
    }
}
```

- [ ] **Step 8: Create `DashboardOptions`**

Create `src/MSOSync.Metadata/Options/DashboardOptions.cs`:

```csharp
namespace MSOSync.Metadata.Options;

public sealed class DashboardOptions
{
    public const string Section = "Dashboard";

    /// <summary>How long to cache the dashboard summary snapshot. Default: 30 seconds.</summary>
    public int SummaryTtlSeconds { get; init; } = 30;
}
```

- [ ] **Step 9: Rewrite `DashboardQueryService.GetSummaryAsync`**

Open `src/MSOSync.Metadata/Dashboard/DashboardQueryService.cs`. Change constructor to accept `DashboardSummaryCache`:

```csharp
public sealed class DashboardQueryService(
    AppDbContext                              db,
    IPlatformRepository<SyncAudit>           auditRepo,
    DashboardSummaryCache                    summaryCache) : IDashboardQueryService
```

Replace `GetSummaryAsync` with:

```csharp
public Task<DashboardSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    => summaryCache.GetOrCreateAsync(BuildSummaryAsync, ct);

private async Task<DashboardSummaryDto> BuildSummaryAsync(CancellationToken ct)
{
    var cutoff24h     = DateTime.UtcNow.AddHours(-24);
    var todayMidnight = DateTime.UtcNow.Date;

    // Single GROUP BY for node connectivity status — replaces 4 separate CountAsync calls.
    // Covered by IX_sync_node_connectivity_status (M038).
    var statusCounts = await db.Nodes.AsNoTracking()
        .GroupBy(n => n.ConnectivityStatus)
        .Select(g => new { Status = g.Key, Count = g.Count() })
        .ToListAsync(ct);

    var totalNodes       = statusCounts.Sum(x => x.Count);
    var reachableNodes   = statusCounts.FirstOrDefault(x => x.Status == ConnectivityStatus.Reachable)?.Count   ?? 0;
    var degradedNodes    = statusCounts.FirstOrDefault(x => x.Status == ConnectivityStatus.Degraded)?.Count    ?? 0;
    var unreachableNodes = statusCounts.FirstOrDefault(x => x.Status == ConnectivityStatus.Unreachable)?.Count ?? 0;
    var unknownNodes     = statusCounts.FirstOrDefault(x => x.Status == ConnectivityStatus.Unknown)?.Count     ?? 0;

    var pendingEvents = await db.DataEvents.AsNoTracking()
        .LongCountAsync(e => !e.IsProcessed, ct);

    var queueDepth = await db.OutgoingBatches.AsNoTracking()
        .LongCountAsync(b => b.Status != 2, ct);

    var eventsToday = await db.DataEvents.AsNoTracking()
        .LongCountAsync(e => e.CreateTime >= todayMidnight, ct);

    var transportErrors24h = await db.BatchErrors.AsNoTracking()
        .LongCountAsync(e => e.CreateTime >= cutoff24h, ct);

    // GeneratedAt reflects when this snapshot was computed, not DateTime.UtcNow on every read.
    return new DashboardSummaryDto(
        totalNodes,
        reachableNodes,
        degradedNodes,
        unreachableNodes,
        unknownNodes,
        pendingEvents,
        queueDepth,
        eventsToday,
        transportErrors24h,
        DateTime.UtcNow);
}
```

Keep `GetActivityAsync` unchanged.

- [ ] **Step 10: Collapse `BatchErrorQueryService.GetBatchErrorSummaryAsync` to one GROUP BY**

Open `src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs`. Replace `GetBatchErrorSummaryAsync` (lines 69–88) with:

```csharp
public async Task<BatchErrorSummaryCountDto> GetBatchErrorSummaryAsync(
    long? batchId, DateTime? from, DateTime? to, CancellationToken ct = default)
{
    var baseQ = db.BatchErrors.AsNoTracking();
    if (batchId.HasValue) baseQ = baseQ.Where(e => e.BatchId    == batchId.Value);
    if (from.HasValue)    baseQ = baseQ.Where(e => e.CreateTime >= from.Value);
    if (to.HasValue)      baseQ = baseQ.Where(e => e.CreateTime <= to.Value);

    // Single GROUP BY conflict_type query — replaces 3 separate CountAsync calls.
    var rawGroups = await baseQ
        .GroupBy(e => e.ConflictType)
        .Select(g => new { ConflictType = g.Key, Count = g.Count() })
        .ToListAsync(ct);

    // Classify in C# on a result set bounded by distinct conflict_type values (small).
    int info = 0, warn = 0, crit = 0;
    foreach (var group in rawGroups)
    {
        var sev = classifier.Classify(group.ConflictType);
        switch (sev)
        {
            case ErrorSeverity.Info:     info += group.Count; break;
            case ErrorSeverity.Warning:  warn += group.Count; break;
            case ErrorSeverity.Critical: crit += group.Count; break;
        }
    }

    return new BatchErrorSummaryCountDto(info, warn, crit, info + warn + crit);
}
```

- [ ] **Step 11: Register `DashboardSummaryCache` and `DashboardOptions` in DI**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`.

Find the `// Epic 9E — Dashboard Query Optimization` block and replace:
```csharp
services.AddScoped<IDashboardQueryService, DashboardQueryService>();
```
with:
```csharp
services.Configure<DashboardOptions>(configuration.GetSection(DashboardOptions.Section));
services.AddSingleton<DashboardSummaryCache>();
services.AddScoped<IDashboardQueryService, DashboardQueryService>();
```

- [ ] **Step 12: Add `Dashboard:SummaryTtlSeconds` to `appsettings.json`**

Open `src/MSOSync.App/appsettings.json`. Add a `Dashboard` section:

```json
"Dashboard": {
  "SummaryTtlSeconds": 30
},
```

- [ ] **Step 13: Run the new tests**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj \
  --filter "FullyQualifiedName~TopologyOptimizationTests|ClusterSummaryProjection|DashboardSummaryOptimization|BatchErrorSummaryGroupBy|TopologyGroupNodeCursor" -v normal
```

Expected: all new tests pass.

- [ ] **Step 14: Run full MetadataTests to check for regressions**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj -v normal
```

Expected: all tests pass. In particular `TopologyQueryServiceTests` must still pass — the existing `GetTopologyGraphAsync(default)` tests call the no-arg default interface method which now delegates to `GetTopologyGraphAsync(null, ct)`.

- [ ] **Step 15: Run topology integration tests**

```
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj \
  --filter "FullyQualifiedName~TopologyTests" -v normal
```

Expected: all pass. `GetGroupNodes_ReturnsMembers` now returns `CursorPageResult<TopologyGroupNodeDto>` — the test currently deserialises as `IReadOnlyList<TopologyGroupNodeDto>`. Update `TopologyTests.cs` to deserialise as `CursorPageResult<TopologyGroupNodeDto>` and assert `result!.Items`:

```csharp
// In TopologyTests.cs, replace GetGroupNodes_ReturnsMembers:
[Fact]
public async Task GetGroupNodes_ReturnsMembers()
{
    var client = await AuthenticatedClientAsync();

    var result = await client.GetFromJsonAsync<CursorPageResult<TopologyGroupNodeDto>>(
        "api/v1/topology/groups/group-hub/nodes");

    result!.Items.Should().HaveCount(2);
    result!.Items.Select(n => n.NodeId).Should().BeEquivalentTo(new[] { "hub-1", "hub-2" });
}

// And GetGroupNodes_EmptyGroup_ReturnsEmptyArray:
[Fact]
public async Task GetGroupNodes_EmptyGroup_ReturnsEmptyArray()
{
    var client = await AuthenticatedClientAsync();

    var result = await client.GetFromJsonAsync<CursorPageResult<TopologyGroupNodeDto>>(
        "api/v1/topology/groups/group-empty/nodes");

    result!.Items.Should().BeEmpty();
}
```

- [ ] **Step 16: Commit**

```
git add src/MSOSync.Metadata/Topology/ITopologyQueryService.cs \
        src/MSOSync.Metadata/Topology/TopologyQueryService.cs \
        src/MSOSync.Api/Controllers/TopologyController.cs \
        src/MSOSync.Metadata/Operations/Cluster/ClusterSummaryQueryService.cs \
        src/MSOSync.Metadata/Dashboard/DashboardQueryService.cs \
        src/MSOSync.Metadata/Dashboard/DashboardSummaryCache.cs \
        src/MSOSync.Metadata/Options/DashboardOptions.cs \
        src/MSOSync.Metadata/BatchErrors/BatchErrorQueryService.cs \
        src/MSOSync.Metadata/MetadataServiceExtensions.cs \
        src/MSOSync.App/appsettings.json \
        tests/MSOSync.MetadataTests/Scale/TopologyOptimizationTests.cs \
        tests/MSOSync.IntegrationTests/Topology/TopologyTests.cs
git commit -m "feat(2D.4-T3): topology/overview query optimizations, GROUP BY node states, dashboard snapshot cache, group node cursor pagination"
```
