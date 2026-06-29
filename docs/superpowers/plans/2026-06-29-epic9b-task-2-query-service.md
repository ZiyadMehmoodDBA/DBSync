# Task 2: ITopologyQueryService + TopologyQueryService + Unit Tests

**Part of:** [Epic 9B Plan](2026-06-29-epic9b-topology-apis.md)

**Goal:** Define the interface, implement the query service with caching, and verify all 12 SQLite unit tests pass. Follow TDD — write each test group first, then implement the method to make it pass.

**Files:**
- Create: `src/MSOSync.Metadata/Topology/ITopologyQueryService.cs`
- Create: `src/MSOSync.Metadata/Topology/TopologyQueryService.cs`
- Create: `tests/MSOSync.MetadataTests/Topology/TopologyQueryServiceTests.cs`

**Interfaces:**
- Consumes (from Task 1):
  - `TopologyGraphDto`, `TopologyNodeDto`, `TopologyEdgeDto`, `TopologyMetadataDto`
  - `TopologyGroupDto`, `TopologyGroupNodeDto`, `TopologySummaryDto`
  - `NodeStatus` enum
  - `ConnectivityStatus` enum (from `MSOSync.Persistence`)
- Consumes (existing): `AppDbContext`, `IMemoryCache`, `TestDbContext.Create()` (test helper)
- Produces (used in Task 3):
  - `ITopologyQueryService` — 5 method interface
  - `TopologyQueryService` — concrete implementation, constructor: `(AppDbContext db, IMemoryCache cache)`

---

- [ ] **Step 1: Create ITopologyQueryService.cs**

Create `src/MSOSync.Metadata/Topology/ITopologyQueryService.cs`:

```csharp
namespace MSOSync.Metadata.Topology;

public interface ITopologyQueryService
{
    Task<TopologyGraphDto>                    GetTopologyGraphAsync(CancellationToken ct);
    Task<TopologySummaryDto>                  GetTopologySummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupDto>>     GetGroupsAsync(CancellationToken ct);
    Task<TopologyGroupDto?>                   GetGroupAsync(string groupId, CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupNodeDto>> GetGroupNodesAsync(string groupId, CancellationToken ct);
}
```

- [ ] **Step 2: Create TopologyQueryService.cs (skeleton)**

Create `src/MSOSync.Metadata/Topology/TopologyQueryService.cs` with the full implementation:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Topology;

public sealed class TopologyQueryService(AppDbContext db, IMemoryCache cache)
    : ITopologyQueryService
{
    private static readonly MemoryCacheEntryOptions CacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };

    // Worst-of-members rule: Unreachable > Degraded > Unknown > Reachable; empty → Unknown
    private static ConnectivityStatus AggregateConnectivity(
        IReadOnlyList<ConnectivityStatus> statuses)
    {
        if (statuses.Count == 0) return ConnectivityStatus.Unknown;
        if (statuses.Any(s => s == ConnectivityStatus.Unreachable)) return ConnectivityStatus.Unreachable;
        if (statuses.Any(s => s == ConnectivityStatus.Degraded))    return ConnectivityStatus.Degraded;
        if (statuses.Any(s => s == ConnectivityStatus.Unknown))     return ConnectivityStatus.Unknown;
        return ConnectivityStatus.Reachable;
    }

    private static TopologyGroupDto BuildGroupDto(
        string groupId, string? name,
        IReadOnlyList<ConnectivityStatus> memberStatuses)
    {
        return new TopologyGroupDto(
            groupId, name,
            memberStatuses.Count,
            memberStatuses.Count(s => s == ConnectivityStatus.Reachable),
            memberStatuses.Count(s => s == ConnectivityStatus.Degraded),
            memberStatuses.Count(s => s == ConnectivityStatus.Unreachable),
            memberStatuses.Count(s => s == ConnectivityStatus.Unknown),
            AggregateConnectivity(memberStatuses));
    }

    // ── GetTopologyGraphAsync ─────────────────────────────────────────────────
    // 4 DB round-trips; result cached for 60 seconds under "topology:graph:v1"
    public async Task<TopologyGraphDto> GetTopologyGraphAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("topology:graph:v1", out TopologyGraphDto? cached))
            return cached!;

        // Round-trip 1: all node groups
        var groups = await db.NodeGroups.AsNoTracking()
            .Select(g => new { g.GroupId, g.GroupName })
            .ToListAsync(ct);

        // Round-trip 2: all nodes — connectivity status and group membership
        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.NodeId, n.GroupId, n.ConnectivityStatus })
            .ToListAsync(ct);

        // Round-trip 3: all routers
        var routers = await db.Routers.AsNoTracking()
            .Select(r => new { r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup, r.Enabled })
            .ToListAsync(ct);

        // Round-trip 4: TriggerRouter JOIN Trigger → RouterId + ChannelId pairs
        // Grouped in C# (not SQL) to avoid complex EF GroupBy translation issues
        var joinRows = await db.TriggerRouters.AsNoTracking()
            .Join(db.Triggers,
                  tr => tr.TriggerId,
                  t  => t.TriggerId,
                  (tr, t) => new { tr.RouterId, t.ChannelId })
            .ToListAsync(ct);

        var channelsByRouter = joinRows
            .GroupBy(x => x.RouterId)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<string>)g.Select(x => x.ChannelId).Distinct().ToList());

        var nodesByGroup = nodes.GroupBy(n => n.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConnectivityStatus).ToList());

        var nodeDtos = groups.Select(g =>
        {
            var statuses = nodesByGroup.TryGetValue(g.GroupId, out var s)
                ? (IReadOnlyList<ConnectivityStatus>)s
                : [];
            return new TopologyNodeDto(
                g.GroupId, g.GroupName,
                statuses.Count,
                statuses.Count(cs => cs == ConnectivityStatus.Reachable),
                statuses.Count(cs => cs == ConnectivityStatus.Degraded),
                statuses.Count(cs => cs == ConnectivityStatus.Unreachable),
                statuses.Count(cs => cs == ConnectivityStatus.Unknown),
                AggregateConnectivity(statuses));
        }).ToList();

        var edgeDtos = routers.Select(r => new TopologyEdgeDto(
            r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup,
            channelsByRouter.TryGetValue(r.RouterId, out var ch) ? ch : [],
            r.Enabled)).ToList();

        var result = new TopologyGraphDto(
            nodeDtos, edgeDtos,
            new TopologyMetadataDto("dagre", "TB", 1, DateTime.UtcNow));

        cache.Set("topology:graph:v1", result, CacheOptions);
        return result;
    }

    // ── GetTopologySummaryAsync ───────────────────────────────────────────────
    public async Task<TopologySummaryDto> GetTopologySummaryAsync(CancellationToken ct)
    {
        int totalGroups = await db.NodeGroups.AsNoTracking().CountAsync(ct);

        var statuses = await db.Nodes.AsNoTracking()
            .Select(n => n.ConnectivityStatus)
            .ToListAsync(ct);

        return new TopologySummaryDto(
            totalGroups,
            statuses.Count,
            statuses.Count(s => s == ConnectivityStatus.Reachable),
            statuses.Count(s => s == ConnectivityStatus.Degraded),
            statuses.Count(s => s == ConnectivityStatus.Unreachable),
            statuses.Count(s => s == ConnectivityStatus.Unknown),
            DateTime.UtcNow);
    }

    // ── GetGroupsAsync ────────────────────────────────────────────────────────
    // Result cached for 60 seconds under "topology:groups:v1"
    public async Task<IReadOnlyList<TopologyGroupDto>> GetGroupsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue("topology:groups:v1", out IReadOnlyList<TopologyGroupDto>? cached))
            return cached!;

        var groups = await db.NodeGroups.AsNoTracking()
            .Select(g => new { g.GroupId, g.GroupName })
            .ToListAsync(ct);

        var nodes = await db.Nodes.AsNoTracking()
            .Select(n => new { n.GroupId, n.ConnectivityStatus })
            .ToListAsync(ct);

        var nodesByGroup = nodes.GroupBy(n => n.GroupId)
            .ToDictionary(g => g.Key, g => g.Select(x => x.ConnectivityStatus).ToList());

        var result = groups.Select(g =>
        {
            var statuses = nodesByGroup.TryGetValue(g.GroupId, out var s)
                ? (IReadOnlyList<ConnectivityStatus>)s
                : [];
            return BuildGroupDto(g.GroupId, g.GroupName, statuses);
        }).ToList();

        cache.Set("topology:groups:v1", (IReadOnlyList<TopologyGroupDto>)result, CacheOptions);
        return result;
    }

    // ── GetGroupAsync ─────────────────────────────────────────────────────────
    // Not cached — direct DB queries
    public async Task<TopologyGroupDto?> GetGroupAsync(string groupId, CancellationToken ct)
    {
        var group = await db.NodeGroups.AsNoTracking()
            .Where(g => g.GroupId == groupId)
            .Select(g => new { g.GroupId, g.GroupName })
            .FirstOrDefaultAsync(ct);

        if (group is null) return null;

        var statuses = await db.Nodes.AsNoTracking()
            .Where(n => n.GroupId == groupId)
            .Select(n => n.ConnectivityStatus)
            .ToListAsync(ct);

        return BuildGroupDto(group.GroupId, group.GroupName, statuses);
    }

    // ── GetGroupNodesAsync ────────────────────────────────────────────────────
    // Not cached — direct DB query; returns empty list for unknown groupId
    public async Task<IReadOnlyList<TopologyGroupNodeDto>> GetGroupNodesAsync(
        string groupId, CancellationToken ct)
    {
        return await db.Nodes.AsNoTracking()
            .Where(n => n.GroupId == groupId)
            .Select(n => new TopologyGroupNodeDto(
                n.NodeId,
                Enum.Parse<NodeStatus>(n.Status, ignoreCase: true),
                n.ConnectivityStatus,
                n.LastHeartbeat,
                n.LastProbeLatencyMs,
                n.SyncEnabled))
            .ToListAsync(ct);
    }
}
```

- [ ] **Step 3: Verify build is clean**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 4: Create TopologyQueryServiceTests.cs**

Create `tests/MSOSync.MetadataTests/Topology/TopologyQueryServiceTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Metadata.Topology;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Topology;

public sealed class TopologyQueryServiceTests
{
    private static TopologyQueryService Make(out Microsoft.EntityFrameworkCore.DbContext db)
    {
        var ctx   = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        db = ctx;
        return new TopologyQueryService(ctx, cache);
    }

    private static SyncNodeGroup Group(string id, string? name = null) =>
        new() { GroupId = id, GroupName = name };

    private static SyncNode Node(string id, string groupId,
        ConnectivityStatus cs = ConnectivityStatus.Reachable) =>
        new()
        {
            NodeId             = id,
            GroupId            = groupId,
            SyncUrl            = "http://localhost",
            Status             = "REGISTERED",
            ConnectivityStatus = cs,
        };

    private static SyncRouter Router(string id, string src, string tgt) =>
        new() { RouterId = id, SourceNodeGroup = src, TargetNodeGroup = tgt, Enabled = true };

    private static SyncTrigger Trigger(string id, string channelId) =>
        new() { TriggerId = id, SourceTable = "dbo.T", ChannelId = channelId };

    private static SyncTriggerRouter TriggerRouter(string triggerId, string routerId) =>
        new() { TriggerId = triggerId, RouterId = routerId, Enabled = true };

    // ── GetTopologyGraphAsync ────────────────────────────────────────────────

    [Fact]
    public async Task GetTopologyGraph_ReturnsAllGroups()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1"), Group("g2"));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetTopologyGraph_AggregatesReachableCounts()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Reachable),
            Node("n2", "g1", ConnectivityStatus.Unreachable),
            Node("n3", "g1", ConnectivityStatus.Degraded));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);
        var node   = result.Nodes.Single();

        node.TotalNodes.Should().Be(3);
        node.ReachableNodes.Should().Be(1);
        node.UnreachableNodes.Should().Be(1);
        node.DegradedNodes.Should().Be(1);
        node.UnknownNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetTopologyGraph_WorstOfMembers_UnreachableWins()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Reachable),
            Node("n2", "g1", ConnectivityStatus.Unreachable));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Single().ConnectivityStatus.Should().Be(ConnectivityStatus.Unreachable);
    }

    [Fact]
    public async Task GetTopologyGraph_WorstOfMembers_DegradedBeatsUnknown()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Unknown),
            Node("n2", "g1", ConnectivityStatus.Degraded));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Single().ConnectivityStatus.Should().Be(ConnectivityStatus.Degraded);
    }

    [Fact]
    public async Task GetTopologyGraph_EmptyGroup_IsUnknown()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        // No nodes
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Nodes.Single().ConnectivityStatus.Should().Be(ConnectivityStatus.Unknown);
        result.Nodes.Single().TotalNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetTopologyGraph_EdgeChannelIds_ResolvedFromTriggers()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1"), Group("g2"));
        db.Set<SyncRouter>().Add(Router("r1", "g1", "g2"));
        db.Set<SyncTrigger>().AddRange(
            Trigger("t1", "ch-default"),
            Trigger("t2", "ch-config"));
        db.Set<SyncTriggerRouter>().AddRange(
            TriggerRouter("t1", "r1"),
            TriggerRouter("t2", "r1"));
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        var edge = result.Edges.Single();
        edge.RouterId.Should().Be("r1");
        edge.ChannelIds.Should().BeEquivalentTo(new[] { "ch-default", "ch-config" });
    }

    [Fact]
    public async Task GetTopologyGraph_MetadataHint_IsCorrect()
    {
        var svc = Make(out var db);
        await db.SaveChangesAsync();

        var result = await svc.GetTopologyGraphAsync(default);

        result.Metadata.LayoutHint.Should().Be("dagre");
        result.Metadata.Direction.Should().Be("TB");
        result.Metadata.Version.Should().Be(1);
    }

    // ── GetGroupsAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task GetGroups_ReturnsAllGroups()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().AddRange(Group("g1", "Hub"), Group("g2", "Store"));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupsAsync(default);

        result.Should().HaveCount(2);
        result.Select(g => g.GroupId).Should().Contain(new[] { "g1", "g2" });
    }

    // ── GetGroupAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetGroup_Existing_ReturnsDto()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1", "Hub"));
        db.Set<SyncNode>().Add(Node("n1", "g1", ConnectivityStatus.Reachable));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupAsync("g1", default);

        result.Should().NotBeNull();
        result!.GroupId.Should().Be("g1");
        result.Name.Should().Be("Hub");
        result.TotalNodes.Should().Be(1);
        result.ReachableNodes.Should().Be(1);
    }

    [Fact]
    public async Task GetGroup_Missing_ReturnsNull()
    {
        var svc = Make(out var db);
        await db.SaveChangesAsync();

        var result = await svc.GetGroupAsync("nonexistent", default);

        result.Should().BeNull();
    }

    // ── GetGroupNodesAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetGroupNodes_ReturnsMemberNodes()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        db.Set<SyncNode>().AddRange(
            Node("n1", "g1", ConnectivityStatus.Reachable),
            Node("n2", "g1", ConnectivityStatus.Degraded));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupNodesAsync("g1", default);

        result.Should().HaveCount(2);
        result.Select(n => n.NodeId).Should().BeEquivalentTo(new[] { "n1", "n2" });
        result.All(n => n.Status == NodeStatus.Registered).Should().BeTrue();
    }

    [Fact]
    public async Task GetGroupNodes_EmptyGroup_ReturnsEmptyList()
    {
        var svc = Make(out var db);
        db.Set<SyncNodeGroup>().Add(Group("g1"));
        await db.SaveChangesAsync();

        var result = await svc.GetGroupNodesAsync("g1", default);

        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 5: Run unit tests**

```powershell
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet test tests\MSOSync.MetadataTests -c Debug --logger "console;verbosity=normal" --filter "FullyQualifiedName~TopologyQueryServiceTests"
```

Expected: 12/12 PASS, 0 failures.

- [ ] **Step 6: Run full MetadataTests suite to check for regressions**

```powershell
dotnet test tests\MSOSync.MetadataTests -c Debug --logger "console;verbosity=normal"
```

Expected: all tests PASS (was 99 before this task; now 111).

- [ ] **Step 7: Commit**

```powershell
git add src/MSOSync.Metadata/Topology/ITopologyQueryService.cs
git add src/MSOSync.Metadata/Topology/TopologyQueryService.cs
git add tests/MSOSync.MetadataTests/Topology/TopologyQueryServiceTests.cs
git commit -m "feat(9b): add ITopologyQueryService + TopologyQueryService with 12 unit tests"
```
