# Epic 9B: Topology APIs Design

**Goal:** Expose a read-only topology graph and group health APIs so the React dashboard (Epic 10) can render the MSOSync sync network using React Flow with client-side Dagre layout.

**Architecture:** Single `ITopologyQueryService` scoped service following the Epic 9A query-service pattern. `TopologyController` is thin — one line per endpoint. Structural data cached 60 seconds. No new migrations. No write endpoints.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core 9 / EF Core 9.0.0 / FluentValidation 11.11.0 (no validators needed this epic) / xUnit 2.9.3 / FluentAssertions 6.12.2 / Moq 4.20.72 / SQLite (unit tests) / LocalDB + WebApplicationFactory (integration tests)

---

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true` — zero warnings at all times
- Central Package Management — no inline `Version=` in `.csproj`
- `AsNoTracking()` on all EF queries
- No N+1 queries — all aggregations via GROUP BY or in-memory after bulk load
- No per-group or per-router sub-queries inside loops
- `GetTopologyGraphAsync` performs exactly 4 DB round-trips (NodeGroups, Nodes, Routers, TriggerRouter+Trigger)
- All endpoints: `[Authorize(Policy = "ViewerOrAbove")]`
- Cache keys versioned: `"topology:graph:v1"`, `"topology:groups:v1"` — 60-second TTL
- `GetGroupAsync` and `GetGroupNodesAsync` are not independently cached
- `NodeStatus` enum defined in `src/MSOSync.Persistence/NodeStatus.cs` (same layer as ConnectivityStatus)
- Entity `SyncNode.Status` remains `string` — DTO projection converts via `Enum.Parse<NodeStatus>(n.Status, true)`
- No `TopologyGroupSummaryDto` endpoint in 9B (deferred to Epic 10)
- No X/Y coordinates anywhere in this epic
- `TopologyMetadataDto.Version = 1` (const)
- `TopologyMetadataDto.LayoutHint = "dagre"`, `Direction = "TB"` (const)
- Worst-of-members rule for group ConnectivityStatus: Unreachable > Degraded > Unknown > Reachable; empty group → Unknown
- `TopologyEdgeDto.ChannelIds` = distinct ChannelIds from `SyncTriggerRouter` → `SyncTrigger` for each router

---

## Entities Used (read-only)

| Entity | DbSet | Fields Used |
|--------|-------|-------------|
| `SyncNodeGroup` | `NodeGroups` | `GroupId`, `GroupName` |
| `SyncNode` | `Nodes` | `NodeId`, `GroupId`, `Status`, `ConnectivityStatus`, `SyncEnabled`, `LastHeartbeat`, `LastProbeLatencyMs` |
| `SyncRouter` | `Routers` | `RouterId`, `SourceNodeGroup`, `TargetNodeGroup`, `Enabled` |
| `SyncTriggerRouter` | `TriggerRouters` | `RouterId`, `TriggerId` |
| `SyncTrigger` | `Triggers` | `TriggerId`, `ChannelId` |

---

## NodeStatus Enum

```csharp
// src/MSOSync.Persistence/NodeStatus.cs
namespace MSOSync.Persistence;

public enum NodeStatus
{
    Provisioning = 0,
    Registered   = 1,
    Offline      = 2,
    Disabled     = 3
}
```

**Implementation note:** Verify these string values against `NodeStateMachine` before finalising. The entity stores lowercase or PascalCase strings — use `Enum.Parse<NodeStatus>(value, ignoreCase: true)` in projection. Add `Unknown = -1` as a safe fallback if any unexpected string is encountered, or throw `InvalidOperationException` with a descriptive message.

---

## File Structure

```
src/MSOSync.Persistence/
    NodeStatus.cs                        ← new enum

src/MSOSync.Metadata/Topology/
    ITopologyQueryService.cs             ← new interface
    TopologyQueryService.cs              ← new implementation
    TopologyGraphDto.cs                  ← TopologyGraphDto, TopologyNodeDto, TopologyEdgeDto, TopologyMetadataDto
    TopologyGroupDto.cs                  ← TopologyGroupDto, TopologyGroupNodeDto
    TopologySummaryDto.cs                ← TopologySummaryDto

src/MSOSync.Api/Controllers/
    TopologyController.cs                ← new controller

src/MSOSync.Metadata/
    MetadataServiceExtensions.cs         ← add ITopologyQueryService registration

tests/MSOSync.MetadataTests/Topology/
    TopologyQueryServiceTests.cs         ← ~12 SQLite unit tests

tests/MSOSync.IntegrationTests/Topology/
    TopologyTests.cs                     ← ~8 LocalDB integration tests
    (shares OperationalReadFixture or new TopologyFixture — see Testing section)
```

---

## DTOs

### TopologyGraphDto (src/MSOSync.Metadata/Topology/TopologyGraphDto.cs)

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed record TopologyGraphDto(
    IReadOnlyList<TopologyNodeDto>  Nodes,
    IReadOnlyList<TopologyEdgeDto>  Edges,
    TopologyMetadataDto             Metadata);

public sealed record TopologyNodeDto(
    string             GroupId,
    string?            Name,
    int                TotalNodes,
    int                ReachableNodes,
    int                DegradedNodes,
    int                UnreachableNodes,
    int                UnknownNodes,
    ConnectivityStatus ConnectivityStatus);

public sealed record TopologyEdgeDto(
    string                  RouterId,
    string                  SourceGroupId,
    string                  TargetGroupId,
    IReadOnlyList<string>   ChannelIds,
    bool                    Enabled);

public sealed record TopologyMetadataDto(
    string   LayoutHint,
    string   Direction,
    int      Version,
    DateTime GeneratedAt);
```

### TopologyGroupDto (src/MSOSync.Metadata/Topology/TopologyGroupDto.cs)

```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed record TopologyGroupDto(
    string             GroupId,
    string?            Name,
    int                TotalNodes,
    int                ReachableNodes,
    int                DegradedNodes,
    int                UnreachableNodes,
    int                UnknownNodes,
    ConnectivityStatus ConnectivityStatus);

public sealed record TopologyGroupNodeDto(
    string             NodeId,
    NodeStatus         Status,
    ConnectivityStatus ConnectivityStatus,
    DateTime?          LastHeartbeat,
    int?               LastProbeLatencyMs,
    bool               SyncEnabled);
```

### TopologySummaryDto (src/MSOSync.Metadata/Topology/TopologySummaryDto.cs)

```csharp
namespace MSOSync.Metadata.Topology;

public sealed record TopologySummaryDto(
    int      TotalGroups,
    int      TotalNodes,
    int      ReachableNodes,
    int      DegradedNodes,
    int      UnreachableNodes,
    int      UnknownNodes,
    DateTime GeneratedAt);
```

---

## Interface

```csharp
// src/MSOSync.Metadata/Topology/ITopologyQueryService.cs
namespace MSOSync.Metadata.Topology;

public interface ITopologyQueryService
{
    Task<TopologyGraphDto>                  GetTopologyGraphAsync(CancellationToken ct);
    Task<TopologySummaryDto>                GetTopologySummaryAsync(CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupDto>>   GetGroupsAsync(CancellationToken ct);
    Task<TopologyGroupDto?>                 GetGroupAsync(string groupId, CancellationToken ct);
    Task<IReadOnlyList<TopologyGroupNodeDto>> GetGroupNodesAsync(string groupId, CancellationToken ct);
}
```

---

## Implementation: TopologyQueryService

```csharp
// src/MSOSync.Metadata/Topology/TopologyQueryService.cs
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Topology;

public sealed class TopologyQueryService(AppDbContext db, IMemoryCache cache)
    : ITopologyQueryService
{
    private static readonly MemoryCacheEntryOptions GraphCacheOptions = new()
    {
        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(60)
    };
}
```

### GetTopologyGraphAsync — 4 DB round-trips, cached "topology:graph:v1"

```csharp
public async Task<TopologyGraphDto> GetTopologyGraphAsync(CancellationToken ct)
{
    if (cache.TryGetValue("topology:graph:v1", out TopologyGraphDto? cached))
        return cached!;

    // Round-trip 1: all node groups
    var groups = await db.NodeGroups.AsNoTracking()
        .Select(g => new { g.GroupId, g.GroupName })
        .ToListAsync(ct);

    // Round-trip 2: all nodes (connectivity + group)
    var nodes = await db.Nodes.AsNoTracking()
        .Select(n => new { n.NodeId, n.GroupId, n.ConnectivityStatus })
        .ToListAsync(ct);

    // Round-trip 3: all routers
    var routers = await db.Routers.AsNoTracking()
        .Select(r => new { r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup, r.Enabled })
        .ToListAsync(ct);

    // Round-trip 4: TriggerRouter → Trigger → ChannelId, grouped by RouterId
    var routerChannels = await db.TriggerRouters.AsNoTracking()
        .Join(db.Triggers, tr => tr.TriggerId, t => t.TriggerId,
              (tr, t) => new { tr.RouterId, t.ChannelId })
        .GroupBy(x => x.RouterId)
        .Select(g => new { RouterId = g.Key, ChannelIds = g.Select(x => x.ChannelId).Distinct().ToList() })
        .ToListAsync(ct);

    // Aggregate in C#
    var nodesByGroup      = nodes.GroupBy(n => n.GroupId).ToDictionary(g => g.Key);
    var channelsByRouter  = routerChannels.ToDictionary(r => r.RouterId,
                                r => (IReadOnlyList<string>)r.ChannelIds);

    var nodeDtos = groups.Select(g =>
    {
        var members = nodesByGroup.TryGetValue(g.GroupId, out var m)
            ? m.Select(x => x.ConnectivityStatus).ToList()
            : [];
        return new TopologyNodeDto(
            g.GroupId,
            g.GroupName,
            members.Count,
            members.Count(s => s == ConnectivityStatus.Reachable),
            members.Count(s => s == ConnectivityStatus.Degraded),
            members.Count(s => s == ConnectivityStatus.Unreachable),
            members.Count(s => s == ConnectivityStatus.Unknown),
            AggregateConnectivity(members));
    }).ToList();

    var edgeDtos = routers.Select(r => new TopologyEdgeDto(
        r.RouterId, r.SourceNodeGroup, r.TargetNodeGroup,
        channelsByRouter.TryGetValue(r.RouterId, out var ch) ? ch : [],
        r.Enabled)).ToList();

    var result = new TopologyGraphDto(
        nodeDtos, edgeDtos,
        new TopologyMetadataDto("dagre", "TB", 1, DateTime.UtcNow));

    cache.Set("topology:graph:v1", result, GraphCacheOptions);
    return result;
}
```

### ConnectivityStatus aggregation helper

```csharp
private static ConnectivityStatus AggregateConnectivity(IReadOnlyList<ConnectivityStatus> statuses)
{
    if (statuses.Count == 0)           return ConnectivityStatus.Unknown;
    if (statuses.Any(s => s == ConnectivityStatus.Unreachable)) return ConnectivityStatus.Unreachable;
    if (statuses.Any(s => s == ConnectivityStatus.Degraded))    return ConnectivityStatus.Degraded;
    if (statuses.Any(s => s == ConnectivityStatus.Unknown))     return ConnectivityStatus.Unknown;
    return ConnectivityStatus.Reachable;
}
```

### GetGroupsAsync — cached "topology:groups:v1"

Same 2 round-trips (NodeGroups + Nodes), C# aggregation. Returns `IReadOnlyList<TopologyGroupDto>`.

### GetGroupAsync — not cached; queries from DB directly

```csharp
public async Task<TopologyGroupDto?> GetGroupAsync(string groupId, CancellationToken ct)
{
    var group = await db.NodeGroups.AsNoTracking()
        .Where(g => g.GroupId == groupId)
        .FirstOrDefaultAsync(ct);
    if (group is null) return null;

    var members = await db.Nodes.AsNoTracking()
        .Where(n => n.GroupId == groupId)
        .Select(n => n.ConnectivityStatus)
        .ToListAsync(ct);

    return new TopologyGroupDto(
        group.GroupId, group.GroupName, members.Count,
        members.Count(s => s == ConnectivityStatus.Reachable),
        members.Count(s => s == ConnectivityStatus.Degraded),
        members.Count(s => s == ConnectivityStatus.Unreachable),
        members.Count(s => s == ConnectivityStatus.Unknown),
        AggregateConnectivity(members));
}
```

### GetGroupNodesAsync — not cached; 1 DB round-trip

```csharp
public async Task<IReadOnlyList<TopologyGroupNodeDto>> GetGroupNodesAsync(string groupId, CancellationToken ct)
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
```

---

## Controller

```csharp
// src/MSOSync.Api/Controllers/TopologyController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Topology;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/topology")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class TopologyController(ITopologyQueryService topology) : ControllerBase
{
    [HttpGet("graph")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGraph(CancellationToken ct)
        => Ok(await topology.GetTopologyGraphAsync(ct));

    [HttpGet("summary")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await topology.GetTopologySummaryAsync(ct));

    [HttpGet("groups")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGroups(CancellationToken ct)
        => Ok(await topology.GetGroupsAsync(ct));

    [HttpGet("groups/{groupId}")]
    [ProducesResponseType(200)]
    [ProducesResponseType(typeof(ProblemDetails), 404)]
    public async Task<IActionResult> GetGroup(string groupId, CancellationToken ct)
    {
        var group = await topology.GetGroupAsync(groupId, ct);
        if (group is null) throw new NotFoundException($"Group {groupId} not found.");
        return Ok(group);
    }

    [HttpGet("groups/{groupId}/nodes")]
    [ProducesResponseType(200)]
    public async Task<IActionResult> GetGroupNodes(string groupId, CancellationToken ct)
        => Ok(await topology.GetGroupNodesAsync(groupId, ct));
}
```

---

## DI Registration

In `MetadataServiceExtensions.AddMetadata()`, add:

```csharp
using MSOSync.Metadata.Topology;

// Epic 9B — Topology APIs
services.AddScoped<ITopologyQueryService, TopologyQueryService>();
```

`AddMemoryCache()` is already registered from Epic 9A.

---

## Testing

### Unit Tests (SQLite) — TopologyQueryServiceTests.cs (~12 tests)

Use the established SQLite `TestDbContext` pattern from Epic 9A. Seed NodeGroups, Nodes with different ConnectivityStatus values, Routers, Triggers, and TriggerRouters.

Required tests:

1. `GetTopologyGraph_ReturnsAllGroups` — graph nodes count equals NodeGroup count
2. `GetTopologyGraph_AggregatesReachableCounts` — correct ReachableNodes count per group
3. `GetTopologyGraph_WorstOfMembersRule_Unreachable` — one Unreachable member → group is Unreachable
4. `GetTopologyGraph_WorstOfMembersRule_Degraded` — Degraded beats Unknown
5. `GetTopologyGraph_EmptyGroup_IsUnknown` — group with no nodes → ConnectivityStatus.Unknown
6. `GetTopologyGraph_EdgeChannelIds_ResolvedCorrectly` — router with 2 triggers (different channels) → ChannelIds has 2 distinct values
7. `GetTopologyGraph_MetadataHint` — LayoutHint == "dagre", Direction == "TB", Version == 1
8. `GetGroups_ReturnsAllGroups`
9. `GetGroup_ExistingGroup_ReturnsDto`
10. `GetGroup_Missing_ReturnsNull`
11. `GetGroupNodes_ReturnsMemberNodes`
12. `GetGroupNodes_EmptyGroup_ReturnsEmptyList`

### Integration Tests (LocalDB) — TopologyTests.cs (~8 tests)

Uses `WebApplicationFactory<Program>` + `IAsyncLifetime`. Shares collection fixture pattern with Epic 9A (`[Collection("Topology")]`, `[CollectionDefinition("Topology")]` on a `ICollectionFixture<TopologyFixture>` marker class).

Required tests:

1. `GetGraph_ReturnsEdgesWithChannelIds` — seeded router with trigger → edge has ChannelIds
2. `GetGraph_AggregatesConnectivityCorrectly` — mixed-status nodes → correct group status
3. `GetSummary_ReturnsExpectedCounts` — correct TotalGroups, TotalNodes counts
4. `GetGroups_ReturnsAllGroups` — list endpoint works
5. `GetGroup_Missing_Returns404` — 404 with NotFoundException body
6. `GetGroupNodes_ReturnsMembers` — seeded nodes appear in response
7. `GetGroupNodes_EmptyGroup_ReturnsEmptyArray` — returns `[]`
8. `Unauthorized_Returns401` — no token → 401

---

## Task Breakdown

| Task | Deliverable |
|------|-------------|
| 1 | `NodeStatus.cs` enum in MSOSync.Persistence; DTOs (3 files in Topology/); build clean |
| 2 | `ITopologyQueryService` + `TopologyQueryService` + SQLite unit tests; build + tests green |
| 3 | `TopologyController` + DI wire + integration tests; full suite green; commit |
