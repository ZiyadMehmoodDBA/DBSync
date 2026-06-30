# Epic 11A: Topology Graph Contract — Design Spec

## Goal

Introduce `GET /api/v1/topology/graph` returning a stable, UI-agnostic graph DTO. Add `queryKeys.topologyGraph()` on the frontend. Establish the single cache key that Epic 11B (React Flow) and Epic 11C (SignalR) will build on.

## Architecture

Backend exposes a read-only query service (`ITopologyGraphQueryService`) and a new controller action. DTOs carry pure topology data — no coordinates, no styling hints, no React Flow concepts. Frontend applies Dagre layout client-side.

All graph nodes are NodeGroup vertices (v1). Node-level expansion and persisted layouts are deferred to Epic 11E+.

## Tech Stack

- **Backend:** C# 13 / .NET 9 / ASP.NET Core, EF Core 9, IMemoryCache (60s, matching existing topology services)
- **Frontend:** React 19, TanStack Query v5, TypeScript strict

---

## Locked Decisions

| Area | Decision |
|---|---|
| Graph scope | Full topology, no `?groupId=` filter |
| Node representation | NodeGroup vertices only |
| Node IDs | `"group:{groupId}"` |
| Edge IDs | `"router:{routerId}"` |
| Edge channels | `IReadOnlyList<string> ChannelIds` |
| Coordinates | None — client-side Dagre |
| Query key | `queryKeys.topologyGraph()` — zero arguments |
| Cache invalidation | Single-key invalidation |
| `TopologyVersion` | Deferred to 11C |
| Persisted layouts | Deferred to 11E+ |
| Subgraphs | Future `queryKeys.topologySubgraph(id)` additive feature |

---

## Backend DTOs

Location: `src/MSOSync.Topology/Dtos/Graph/`

```csharp
public sealed record TopologyGraphDto(
    IReadOnlyList<TopologyGraphNodeDto> Nodes,
    IReadOnlyList<TopologyGraphEdgeDto> Edges,
    TopologyGraphMetaDto Meta
);

public sealed record TopologyGraphNodeDto(
    string Id,          // "group:{groupId}"
    string GroupId,
    string Label,
    NodeStatus Status,
    int MemberCount,
    int TriggerCount,
    int ChannelCount
);

public sealed record TopologyGraphEdgeDto(
    string Id,          // "router:{routerId}"
    string Source,      // "group:{sourceGroupId}"
    string Target,      // "group:{targetGroupId}"
    IReadOnlyList<string> ChannelIds,
    bool IsEnabled
);

public sealed record TopologyGraphMetaDto(
    int TotalGroups,
    int TotalNodes,
    int OnlineNodes,
    DateTimeOffset GeneratedAt
);
```

**Node ID construction:** `$"group:{group.GroupId}"`
**Edge ID construction:** `$"router:{router.RouterId}"`
**Source/Target:** `$"group:{router.SourceGroupId}"` / `$"group:{router.TargetGroupId}"`

`NodeStatus` is the existing enum from Epic 9B (`Online`, `Offline`, `Disabled`, `PendingApproval`). A group's status is derived from its member nodes: if all members are online → `Online`; if any are offline → `Offline`; edge cases follow existing `TopologyQueryService` conventions.

## Backend Service Interface

Location: `src/MSOSync.Topology/`

```csharp
public interface ITopologyGraphQueryService
{
    Task<TopologyGraphDto> GetGraphAsync(CancellationToken ct = default);
}
```

Implementation (`TopologyGraphQueryService`) queries:
1. All `NodeGroups` with their `Nodes` (for `MemberCount`, `Status`)
2. All `TriggerRouters` joined to `Routers` + `TriggerRouters` → channel IDs
3. All `Channels` referenced by active routers

Cache key: `"topology:graph"`, 60s absolute expiration via `IMemoryCache`.

## Backend Controller Action

Existing `TopologyController` (`src/MSOSync.Api/Controllers/TopologyController.cs`):

```csharp
[HttpGet("graph")]
[ProducesResponseType<TopologyGraphDto>(StatusCodes.Status200OK)]
public async Task<IActionResult> GetGraph(CancellationToken ct)
{
    var graph = await _graphQueryService.GetGraphAsync(ct);
    return Ok(graph);
}
```

Route: `GET /api/v1/topology/graph`
Authorization: existing `[Authorize]` policy on controller.

## Frontend API Function

Location: `src/MSOSync.Frontend/src/shared/api/topology.ts` (extend existing file)

```ts
export interface TopologyGraphNodeDto {
  id: string;
  groupId: string;
  label: string;
  status: NodeStatus;
  memberCount: number;
  triggerCount: number;
  channelCount: number;
}

export interface TopologyGraphEdgeDto {
  id: string;
  source: string;
  target: string;
  channelIds: string[];
  isEnabled: boolean;
}

export interface TopologyGraphMetaDto {
  totalGroups: number;
  totalNodes: number;
  onlineNodes: number;
  generatedAt: string;
}

export interface TopologyGraphDto {
  nodes: TopologyGraphNodeDto[];
  edges: TopologyGraphEdgeDto[];
  meta: TopologyGraphMetaDto;
}

export async function getTopologyGraph(): Promise<TopologyGraphDto> {
  const res = await apiClient.get<TopologyGraphDto>('/topology/graph');
  return res.data;
}
```

## Frontend Query Key

Location: `src/MSOSync.Frontend/src/shared/queryKeys.ts` (extend existing object)

```ts
topologyGraph: () => ['topology-graph'] as const,
```

## Frontend Hook

Location: `src/MSOSync.Frontend/src/features/topology/hooks.ts` (extend existing file)

```ts
export function useTopologyGraph() {
  return useQuery({
    queryKey: queryKeys.topologyGraph(),
    queryFn: getTopologyGraph,
    staleTime: 30_000,
  });
}
```

## 11B Consumption Pattern (reference only)

```ts
const { data } = useTopologyGraph();

const layouted = useMemo(
  () => applyDagre(data?.nodes ?? [], data?.edges ?? []),
  [data]
);
```

## 11C SignalR Invalidation Pattern (reference only)

```ts
hubConnection.on('topology.changed', () => {
  queryClient.invalidateQueries({ queryKey: queryKeys.topologyGraph() });
});
```

## Testing

**Unit tests** (`tests/MSOSync.TopologyTests/`):
- `GetGraphAsync` returns correct node count, edge count, channel IDs, status aggregation
- Empty topology (no groups, no routers) returns valid empty DTO
- Cache hit is served without DB query on second call within 60s

**Integration tests** (`tests/MSOSync.IntegrationTests/Topology/`):
- `GET /api/v1/topology/graph` returns 200 with correct shape
- Unauthenticated request returns 401
- Node ID format is `"group:{groupId}"`; edge ID format is `"router:{routerId}"`

**Frontend** (`npm run build` exit 0, TypeScript strict, no `any`):
- `queryKeys.topologyGraph()` returns `['topology-graph']`
- `useTopologyGraph()` returns `{ data: TopologyGraphDto | undefined, ... }`

---

## Implementation Tasks

### Task 1: Backend DTOs + Service + Controller action
- Create `src/MSOSync.Topology/Dtos/Graph/` with 4 DTO records
- Create `ITopologyGraphQueryService` interface
- Implement `TopologyGraphQueryService` (cached, queries groups + routers + channels)
- Add `[HttpGet("graph")]` action to `TopologyController`
- Register service in `AddTopology()` DI extension
- Unit tests: node/edge shape, empty topology, cache hit

### Task 2: Frontend API + query key + hook + integration tests
- Add `TopologyGraphDto` types + `getTopologyGraph()` to `topology.ts`
- Add `topologyGraph: () => ['topology-graph'] as const` to `queryKeys.ts`
- Add `useTopologyGraph()` to `features/topology/hooks.ts`
- Add `GET /topology/graph` integration tests (200, 401, ID format)
- `npm run build` clean

### Task 3: Cache contract verification + invalidation wiring
- Confirm `invalidateTriggerRelated()` in `features/triggers/mutations.ts` does NOT yet include `topologyGraph` (placeholder comment for 11C)
- Confirm `topologyGraph` key format matches across `queryKeys.ts` and `useTopologyGraph()`
- Add `topologyGraph()` to `invalidateNodeRelated()` and `invalidateTriggerRelated()` helpers — these mutations already run after topology-affecting operations; adding the graph key now ensures 11B sees fresh data after user actions before 11C SignalR is wired
- Run full build + unit tests + integration tests green
