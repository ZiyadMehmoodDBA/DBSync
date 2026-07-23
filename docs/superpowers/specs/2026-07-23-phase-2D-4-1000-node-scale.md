# Phase 2D.4 — 1000-Node Scale

**Status:** Approved for implementation  
**Date:** 2026-07-23  
**Phase:** 2D — Scalability & Performance  
**Predecessor specs:** `2026-07-17-roadmap-v2.md` (Phase 2D definition), `2026-07-02-epic11g-performance-scale-design.md`

---

## 1. Goal

Ensure MSOSync remains performant and memory-safe at 1000 registered nodes. The target is:

- `GET /api/v1/topology/graph` responds in under 500 ms at 1000 nodes, 200 groups, 400 router edges.
- `GET /api/v1/nodes` (unbounded) returns a deprecation cursor redirect without loading all nodes into memory when count exceeds `NodeListCursorThreshold` (default 200).
- Routing resolution (batch fan-out to target nodes) uses a single bulk insert per batch, not N individual inserts.
- `GET /api/v1/dashboard/summary` does not degrade with node count: all per-node aggregation is done in SQL, never in C#.
- `ClusterSummaryQueryService.QueryNodeStatesAsync` does not hydrate full `SyncNode` entities into memory.
- Every new index introduced in this phase is documented with the query it covers and the migration number.

---

## 2. Problem Statement

### 2.1 What breaks at 1000 nodes

#### 2.1.1 `GET /api/v1/nodes` — unbounded full-entity load

`NodeMetadataService.GetNodesAsync` executes:

```csharp
var nodes = await db.Nodes.AsNoTracking().ToListAsync(ct);
```

At 1000 nodes, `SyncNode` has approximately 30 columns including nullable `nvarchar` fields (`DbPasswordEncrypted`, `ExpectedEffectiveHash`, `AppliedEffectiveHash`, `MaintenanceReason`, `DecommissionReason`, etc.). A full hydration of 1000 rows with these columns loads several megabytes of heap per request and takes 300–600 ms on LocalDB depending on page pressure. The endpoint is called by the React topology sidebar and the node-list dashboard view. Under concurrent load from multiple browser tabs, this compounds.

The paged alternative (`GET /api/v1/nodes/paged`) uses offset-based pagination: `Skip((pageNumber - 1) * pageSize)`. Offset pagination degrades O(n) because SQL Server must scan and discard rows before the skip boundary. At page 20 with pageSize=50, SQL scans 1000 rows to return 50.

#### 2.1.2 `ClusterSummaryQueryService.QueryNodeStatesAsync` — full entity hydration

```csharp
var nodes = await db.Nodes
    .AsNoTracking()
    .Select(n => new { n.LifecycleState, n.MaintenanceMode })
    .ToListAsync(ct);
```

This projection is correct but materializes all 1000 anonymous objects into C# memory to perform `Count()` in LINQ. The equivalent SQL `GROUP BY` would compute the same counts server-side with a single scan. At 1000 nodes this is a minor issue today, but at 10 000 nodes it becomes a 100 MB allocation per request.

#### 2.1.3 `ITopologyQueryService.GetTopologyGraphAsync` — node count hidden from graph query

`TopologyGraphDto` aggregates per-group: `MemberCount`, `TriggerCount`, `ChannelCount`. The current interface signature `GetTopologyGraphAsync(CancellationToken ct)` has no filter parameter. If the underlying implementation queries `sync_node` to compute member counts, it scans all nodes. There is no optional `NodeId[]` filter to restrict the graph to a subgraph of interest (e.g., the nodes visible in the current React Flow viewport).

`GET /api/v1/topology/groups/{groupId}/nodes` returns `IReadOnlyList<TopologyGroupNodeDto>` with no pagination. A group with 300 member nodes returns all 300 nodes in one response.

#### 2.1.4 Batch routing fan-out — N individual outgoing-batch inserts

`IRoutingService.ResolveAsync` returns `IReadOnlyList<string>` (resolved node IDs). The caller (batch pipeline) issues one `OutgoingBatch` insert per node ID. At a trigger broadcast to 1000 nodes, this is 1000 round-trips or 1000 individual EF `SaveChangesAsync` calls in a loop. This holds the database connection for the full loop duration and contends with heartbeat writes.

#### 2.1.5 `EventQueryService` — correlated subquery per row

```csharp
.Select(e => new EventSummaryDto(
    ...
    db.DataEventBatches
        .Where(deb => deb.EventId == e.EventId)
        .Max(deb => (long?)deb.BatchId),   // correlated subquery
    ...))
```

At pageSize=50 this executes 50 correlated subqueries on `sync_data_event_batch`. The table has a composite primary key `(event_id, batch_id)` but no standalone index on `event_id`. At 1000 nodes, event volume grows proportionally. The correlated subquery pattern prevents SQL Server from using an index scan and falls back to nested-loop lookups.

#### 2.1.6 `DashboardController.GetSummary` — missing index on connectivity_status

`DashboardSummaryDto` counts `ReachableNodes`, `DegradedNodes`, `UnreachableNodes`, `UnknownNodes`. These counts filter `sync_node` on `connectivity_status` (tinyint). The column has no dedicated index. At 1000 nodes, SQL Server performs a full clustered index scan on `sync_node` per status bucket. With four status values this is four sequential scans.

#### 2.1.7 `BatchErrorQueryService.GetBatchErrorSummaryAsync` — three separate COUNT queries

```csharp
int info = await baseQ.CountAsync(e => infoTypes.Contains(e.ConflictType), ct);
int warn = await baseQ.CountAsync(e => warnTypes.Contains(e.ConflictType), ct);
int crit = await baseQ.CountAsync(..., ct);
```

Three independent `COUNT` queries on `sync_batch_error`, each with a `WHERE` clause. These should be collapsed into a single `GROUP BY` query.

---

## 3. Architecture

### 3.1 Cursor Pagination Design

MSOSync already has a complete cursor pagination infrastructure:
- `CursorToken` in `MSOSync.Common.Pagination` — signed, HMAC-SHA256, opaque base64.
- `CursorSigner` in `MSOSync.Metadata.Pagination` — singleton, reads key from `Pagination:CursorHmacKey`.
- `CursorPageResult<T>` — wire contract: `Items`, `NextCursor`, `HasMore`, `TotalCount?`.
- `EventQueryService` and `IncomingBatchQueryService` already implement cursor pagination correctly.

#### 3.1.1 Cursor token format

The cursor encodes two fields: the primary sort key (an identity `long`) and the secondary tiebreak (a `DateTime.Ticks` value as `long`). The encoded format before outer base64 is:

```
v2:{id}:{ticks}:{base64HmacSHA256}
```

The outer layer base64-encodes the entire string as UTF-8. Consumers receive and send the opaque outer base64 string. They never see raw IDs or ticks.

The decode path:
1. Base64-decode the outer string.
2. Find the last `:` to split HMAC from payload.
3. HMAC-verify the payload using `CryptographicOperations.FixedTimeEquals` (timing-safe).
4. Split remaining payload on `:` and parse `version`, `id`, `ticks`.
5. If any step fails, return HTTP 400 with `InvalidCursorToken` problem code.

#### 3.1.2 Cursor semantics for node lists

For `GET /api/v1/nodes` cursor pagination, the cursor encodes `(NodeId: opaque string, RegistrationTime: ticks)`. Because `NodeId` is a `varchar(50)` (not a `long`), the cursor encoding is extended:

```
v2n:{nodeIdBase64}:{ticks}:{base64HmacSHA256}
```

Where `nodeIdBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(nodeId))`.

The decode path extracts `nodeIdBase64` and `ticks`, then filters with `WHERE (node_id > @cursorNodeId)` ordering by `node_id ASC` (lexicographic). This is stable because `node_id` is unique and immutable.

A new method `CursorToken.EncodeString(string id, long ticks, ReadOnlySpan<byte> hmacKey)` / `DecodeString(string token, ReadOnlySpan<byte> hmacKey) -> (string Id, long Ticks)` is added to `MSOSync.Common.Pagination.CursorToken`.

`CursorSigner` gains `EncodeString(string id, long ticks)` and `DecodeString(string token)` delegating to the new methods.

#### 3.1.3 Cursor pagination for topology group nodes

`GetGroupNodesAsync(string groupId, CancellationToken ct)` is extended to:

```csharp
Task<CursorPageResult<TopologyGroupNodeDto>> GetGroupNodesAsync(
    string groupId,
    string? cursor,
    int pageSize,
    CancellationToken ct);
```

The cursor encodes `(NodeId: string, ticks: 0)` — same v2n scheme. Page size defaults to 100, max 500.

The HTTP endpoint becomes:

```
GET /api/v1/topology/groups/{groupId}/nodes?cursor=&pageSize=100
```

The existing shape `IReadOnlyList<TopologyGroupNodeDto>` becomes `CursorPageResult<TopologyGroupNodeDto>`. This is a breaking API change on the `groups/{groupId}/nodes` sub-route, but that route was only introduced for topological browsing (no public consumers yet) — it is safe to change.

#### 3.1.4 Node cursor pagination — new endpoint strategy

The existing `GET /api/v1/nodes` (unbounded) is preserved for backward compatibility but gated by count:

- If `sync_node` row count for the tenant is below `NodeListCursorThreshold` (default 200, configurable via `Pagination:NodeListCursorThreshold`), the endpoint continues to return the full list.
- If row count is at or above the threshold, the endpoint returns HTTP 200 with a response body that includes a `paginationRequired` flag set to `true`, an empty `items` array, and `nextCursor` pointing to page 1. Callers must switch to the cursor endpoint.

The cursor-paginated endpoint:

```
GET /api/v1/nodes/cursor?cursor=&pageSize=50&includeTotal=false
```

Returns `CursorPageResult<NodeDto>`. The existing `GET /api/v1/nodes/paged` (offset-based) is **deprecated** but not removed. It gains a `Deprecation` response header: `Deprecation: true; rel="successor-version"; url=/api/v1/nodes/cursor`.

### 3.2 Topology Query Optimization

#### 3.2.1 Projection-only graph query

`GetTopologyGraphAsync` must not execute a query that materializes full `SyncNode` rows. The implementation must project:

```sql
SELECT n.group_id, COUNT(*) AS member_count
FROM msosync.sync_node n
WHERE n.lifecycle_state = 3  -- Active
GROUP BY n.group_id
```

In EF Core 9:

```csharp
var memberCounts = await db.Nodes
    .AsNoTracking()
    .Where(n => n.LifecycleState == NodeLifecycleState.Active)
    .GroupBy(n => n.GroupId)
    .Select(g => new { GroupId = g.Key, Count = g.Count() })
    .ToDictionaryAsync(x => x.GroupId, x => x.Count, ct);
```

Then join to groups and routers in C# — all three sets (groups, routers, member counts) are small enough that a dictionary lookup is appropriate.

#### 3.2.2 Count query separated from data query

`GetTopologySummaryAsync` currently (inferred from `TopologySummaryDto`) counts nodes by connectivity status. The implementation must use a single `GROUP BY connectivity_status` query:

```csharp
var statusGroups = await db.Nodes
    .AsNoTracking()
    .GroupBy(n => n.ConnectivityStatus)
    .Select(g => new { Status = g.Key, Count = g.Count() })
    .ToListAsync(ct);
```

This replaces any pattern of `CountAsync` called once per status value.

`GetTopologyGraphAsync` must issue a separate `CountAsync()` for total node count only when `Meta.TotalNodes` is required, not by materializing nodes. It can reuse the group membership dictionary's values sum.

#### 3.2.3 Optional `NodeId[]` filter for focused topology views

`ITopologyQueryService` gains an overload:

```csharp
Task<TopologyGraphDto> GetTopologyGraphAsync(
    string[]? nodeIdFilter,
    CancellationToken ct);
```

The original `GetTopologyGraphAsync(CancellationToken ct)` becomes a default-calling shorthand:

```csharp
Task<TopologyGraphDto> GetTopologyGraphAsync(CancellationToken ct)
    => GetTopologyGraphAsync(null, ct);
```

When `nodeIdFilter` is non-null and non-empty, the group membership query filters:

```csharp
var filteredGroupIds = await db.Nodes
    .AsNoTracking()
    .Where(n => nodeIdFilter.Contains(n.NodeId))
    .Select(n => n.GroupId)
    .Distinct()
    .ToListAsync(ct);
```

Only groups, routers, and member counts for those group IDs are then fetched. This caps the graph response size for viewport-filtered topology views.

The HTTP endpoint gains an optional query parameter:

```
GET /api/v1/topology/graph?nodeIds=node-001,node-002,...
```

Parsed as a comma-delimited string, split in the controller, max 50 values enforced with HTTP 400.

### 3.3 Bulk Routing Design

#### 3.3.1 Problem: N individual inserts

The batch pipeline currently calls `IRoutingService.ResolveAsync` and then loops over the result, inserting one `SyncOutgoingBatch` per node. At fan-out to 1000 nodes this is 1000 round trips.

#### 3.3.2 Solution: `IBulkRoutingService`

A new interface is introduced in `MSOSync.Routing`:

```csharp
public interface IBulkRoutingService
{
    /// <summary>
    /// Resolves target nodes for <paramref name="triggerId"/> and bulk-inserts one outgoing
    /// batch row per target node. Returns the list of batch IDs inserted.
    /// </summary>
    Task<IReadOnlyList<long>> FanOutAsync(
        string   triggerId,
        string   channelId,
        long     batchSequence,
        int      rowCount,
        long     byteCount,
        Guid     tenantId,
        CancellationToken ct = default);
}
```

The implementation uses `ExecuteSqlRawAsync` with an `INSERT INTO ... SELECT` pattern:

```sql
INSERT INTO [msosync].[sync_outgoing_batch]
    ([batch_sequence], [node_id], [channel_id], [status], [row_count], [byte_count],
     [retry_count], [create_time], [tenant_id])
SELECT
    @batchSequence,
    n.[node_id],
    @channelId,
    0,            -- status = New
    @rowCount,
    @byteCount,
    0,
    SYSUTCDATETIME(),
    @tenantId
FROM [msosync].[sync_node] n
INNER JOIN [msosync].[sync_trigger_router] tr ON tr.[trigger_id] = @triggerId AND tr.[enabled] = 1
INNER JOIN [msosync].[sync_router] r
    ON r.[router_id] = tr.[router_id]
    AND r.[enabled] = 1
    AND r.[target_node_group] = n.[group_id]
WHERE n.[lifecycle_state] = 3     -- Active (EligibleExpression)
  AND n.[maintenance_mode] = 0
  AND n.[tenant_id] = @tenantId
OUTPUT INSERTED.[batch_id];
```

The `OUTPUT INSERTED.[batch_id]` clause returns the identity values of all inserted rows in one round-trip.

Because `ExecuteSqlRawAsync` does not use EF change tracking, the caller must not also insert these rows via EF in the same unit of work.

**DbContext threading constraint:** `IBulkRoutingService` receives `AppDbContext` by constructor injection (scoped). The implementation does not use `Task.WhenAll` or any parallel execution on the same context.

**Cache integration:** `BulkRoutingService` resolves the `IMemoryCache` from DI to invalidate routing entries in the same manner as `RoutingService`. However, the bulk insert does not call `ResolveAsync` — it executes the join inline. The routing cache is therefore not needed for the fan-out path itself; it is only needed when the pipeline queries the resolved node list separately (e.g., for status checks).

#### 3.3.3 Backward compatibility

`IRoutingService.ResolveAsync` is unchanged. Existing callers (heartbeat pipeline, diagnostics) continue to use it. The batch pipeline is the only caller that switches to `IBulkRoutingService.FanOutAsync`.

---

## 4. Query Optimizations

### 4.1 Top-5 high-cost queries at 1000 nodes

The following queries are identified as the highest estimated cost based on plan analysis. Estimated costs assume 1000 nodes, 100 000 events/day, 50 000 outgoing batches, and 10 000 incoming batches.

| Rank | Query | Table | Current issue | Fix |
|---|---|---|---|---|
| 1 | `GetEventsAsync` — correlated `MAX(batch_id)` subquery | `sync_data_event_batch` | No index on `event_id` alone; nested-loop scan per row | Add `IX_sync_data_event_batch_event_id` on `(event_id)` |
| 2 | `GetTopologySummaryAsync` — per-status `CountAsync` | `sync_node` | Multiple scans; no index on `connectivity_status` | Add `IX_sync_node_connectivity_status` on `(connectivity_status)` INCLUDE `(lifecycle_state)` |
| 3 | `GetNodesAsync` — full entity scan | `sync_node` | Loads all columns for all 1000 rows | Add cursor pagination; projection-only for list endpoint |
| 4 | `GetBatchErrorSummaryAsync` — three separate `COUNT` queries | `sync_batch_error` | Three sequential scans on unbounded table | Collapse to single `GROUP BY conflict_type` query |
| 5 | `GetGroupNodesAsync` — unbounded group membership load | `sync_node` | No `LIMIT` / `TOP`; loads all members per group | Add cursor pagination; add `IX_sync_node_group_id_node_id` on `(group_id, node_id)` if absent |

### 4.2 Index inventory

The following indexes are added in migration `M031_ScaleIndexes` (next migration after `M030_MultiTenancyFoundation`):

#### Index 1: `IX_sync_data_event_batch_event_id`

```sql
CREATE INDEX [IX_sync_data_event_batch_event_id]
    ON [msosync].[sync_data_event_batch] ([event_id] ASC);
```

**Covers:** `EventQueryService` correlated subquery `WHERE deb.EventId == e.EventId`. Reduces per-row lookup from table scan to index seek on `event_id`. The existing PK `(event_id, batch_id)` is not used for this lookup because EF generates the correlated query with `event_id` as the leading predicate but the PK is composite — SQL Server's optimizer may not use the PK for nested-loop lookups efficiently. A dedicated single-column index on `event_id` gives a clean seek.

#### Index 2: `IX_sync_node_connectivity_status`

```sql
CREATE INDEX [IX_sync_node_connectivity_status]
    ON [msosync].[sync_node] ([connectivity_status] ASC)
    INCLUDE ([lifecycle_state], [maintenance_mode]);
```

**Covers:** `GetTopologySummaryAsync` status-bucket counts and `DashboardSummaryDto` reachability counts. Both queries filter or group by `connectivity_status`. The INCLUDE adds `lifecycle_state` and `maintenance_mode` to support covering index scans for the `ClusterSummaryQueryService.QueryNodeStatesAsync` projection `new { n.LifecycleState, n.MaintenanceMode }`.

#### Index 3: `IX_sync_node_group_id`

```sql
CREATE INDEX [IX_sync_node_group_id]
    ON [msosync].[sync_node] ([group_id] ASC, [node_id] ASC)
    INCLUDE ([lifecycle_state], [connectivity_status]);
```

**Covers:** `GetGroupNodesAsync` which filters by `group_id` and orders by `node_id` for cursor pagination. The INCLUDE adds the two status fields used in `TopologyGroupNodeDto` projection so the query is a covering scan.

#### Index 4: `IX_sync_outgoing_batch_create_time`

```sql
CREATE INDEX [IX_sync_outgoing_batch_create_time]
    ON [msosync].[sync_outgoing_batch] ([create_time] DESC)
    INCLUDE ([node_id], [channel_id], [status]);
```

**Covers:** Dashboard and metrics queries that filter outgoing batches by time window. Existing index `IX_sync_outgoing_batch_node_status` covers per-node status lookups, but time-range queries on `create_time` have no dedicated index.

#### Index 5: `IX_sync_node_lifecycle_state`

```sql
CREATE INDEX [IX_sync_node_lifecycle_state]
    ON [msosync].[sync_node] ([lifecycle_state] ASC)
    INCLUDE ([group_id], [maintenance_mode], [tenant_id]);
```

**Covers:** `IBulkRoutingService.FanOutAsync` bulk-insert's `WHERE lifecycle_state = 3` predicate. Also covers `NodeSyncPolicy.EligibleExpression` used by `RoutingService.ResolveAsync`. The existing routing cache reduces the frequency of this query, but on cache miss at 1000 nodes the full node scan is expensive.

### 4.3 Projection patterns

All read queries in this phase follow the projection rule: `Select` to an anonymous type or DTO before `ToListAsync`. No query may call `ToListAsync` on a full entity set and then filter in C#.

The `ClusterSummaryQueryService.QueryNodeStatesAsync` is rewritten:

```csharp
private async Task<NodeStateCountsDto> QueryNodeStatesAsync(CancellationToken ct)
{
    var groups = await db.Nodes
        .AsNoTracking()
        .GroupBy(n => new { n.LifecycleState, n.MaintenanceMode })
        .Select(g => new { g.Key.LifecycleState, g.Key.MaintenanceMode, Count = g.Count() })
        .ToListAsync(ct);

    var total       = groups.Sum(g => g.Count);
    var maintenance = groups.Where(g => g.MaintenanceMode).Sum(g => g.Count);
    var active      = groups.Where(g => g.LifecycleState == NodeLifecycleState.Active && !g.MaintenanceMode).Sum(g => g.Count);
    var draining    = groups.Where(g => g.LifecycleState == NodeLifecycleState.Draining).Sum(g => g.Count);
    var offline     = groups.Where(g => !g.MaintenanceMode
        && g.LifecycleState != NodeLifecycleState.Active
        && g.LifecycleState != NodeLifecycleState.Draining).Sum(g => g.Count);

    return new NodeStateCountsDto(total, active, maintenance, draining, offline);
}
```

This issues a single `SELECT lifecycle_state, maintenance_mode, COUNT(*) FROM sync_node GROUP BY lifecycle_state, maintenance_mode` and computes the dashboard counts in C# on a result set of at most `(distinct lifecycle states) * 2` rows — bounded at roughly 20 rows regardless of node count.

---

## 5. Migration Plan — Which Endpoints Get Pagination

### 5.1 Endpoint audit

| Endpoint | Current pagination | Action in 2D.4 |
|---|---|---|
| `GET /api/v1/nodes` | None — unbounded | Add threshold gate; redirect to cursor endpoint when count >= threshold |
| `GET /api/v1/nodes/paged` | Offset (`pageNumber`, `pageSize`) | Deprecate; add `Deprecation` header; preserve functioning for backward compat |
| `GET /api/v1/nodes/cursor` | Does not exist | Create with cursor pagination returning `CursorPageResult<NodeDto>` |
| `GET /api/v1/topology/groups/{groupId}/nodes` | None — unbounded | Add cursor pagination (`cursor`, `pageSize`) returning `CursorPageResult<TopologyGroupNodeDto>` |
| `GET /api/v1/topology/graph` | N/A (graph, not list) | Add optional `?nodeIds=` filter; no pagination (graph is aggregated per group, not per node) |
| `GET /api/v1/events` | Cursor (already) | No change — already correct |
| `GET /api/v1/incoming-batches` | Cursor (already) | No change — already correct |
| `GET /api/v1/batch-errors` | Offset (`page`, `pageSize`) | No change in 2D.4 — volume is bounded by batch count, not node count |
| `GET /api/v1/nodes/registrations/pending` | None | No change — bounded by pending approvals, low volume |
| `GET /api/v1/nodes/groups` | None | No change — group count is bounded by topology design, not node count |

### 5.2 Node cursor endpoint specification

**Request:**

```
GET /api/v1/nodes/cursor?cursor={opaque}&pageSize=50&includeTotal=false
```

Parameters:
- `cursor` — opaque base64 string from a previous response's `nextCursor`. Absent on first request.
- `pageSize` — integer 1–200 (clamped, default 50).
- `includeTotal` — boolean, default false. When true, runs `COUNT(*)` in addition to the data query.

**Response:**

```json
{
  "items": [ /* NodeDto array */ ],
  "nextCursor": "djJuOm...",
  "hasMore": true,
  "totalCount": null
}
```

When `includeTotal=true`:

```json
{
  "items": [ /* NodeDto array */ ],
  "nextCursor": null,
  "hasMore": false,
  "totalCount": 1000
}
```

**EF Core query (first page):**

```csharp
var rows = await db.Nodes
    .AsNoTracking()
    .Where(n => n.TenantId == tenantId)
    .OrderBy(n => n.NodeId)
    .Take(pageSize + 1)
    .Select(n => new NodeDto(...))  // projection of needed columns only
    .ToListAsync(ct);
```

**EF Core query (subsequent pages, cursor present):**

```csharp
var (cursorNodeId, _) = cursorSigner.DecodeString(cursor);

var rows = await db.Nodes
    .AsNoTracking()
    .Where(n => n.TenantId == tenantId && string.Compare(n.NodeId, cursorNodeId, StringComparison.Ordinal) > 0)
    .OrderBy(n => n.NodeId)
    .Take(pageSize + 1)
    .Select(n => new NodeDto(...))
    .ToListAsync(ct);
```

The `string.Compare > 0` translates to `WHERE node_id > @cursorNodeId` in SQL Server, which uses `IX_sync_node_group_id` (leading column `group_id`) or the clustered PK on `node_id` depending on selectivity. SQL Server will use the PK directly because `node_id` is the primary key.

**`HasMore` and `NextCursor` construction:**

```csharp
var hasMore = rows.Count > pageSize;
if (hasMore) rows = rows.Take(pageSize).ToList();

string? nextCursor = hasMore
    ? cursorSigner.EncodeString(rows[^1].NodeId, DateTime.UtcNow.Ticks)
    : null;
```

### 5.3 `GET /api/v1/nodes` backward-compatibility gate

`INodeMetadataService` gains:

```csharp
Task<NodeListGateResult> GetNodesWithGateAsync(
    int threshold, CancellationToken ct);
```

Where:

```csharp
public sealed record NodeListGateResult(
    bool                    PaginationRequired,
    IReadOnlyList<NodeDto>? Items,       // null when PaginationRequired = true
    string?                 NextCursor); // first-page cursor when PaginationRequired = true
```

The controller:

```csharp
[HttpGet]
[Authorize]
[ProducesResponseType(typeof(IReadOnlyList<NodeDto>), 200)]
[ProducesResponseType(typeof(NodeListGateResponse), 200)]
public async Task<IActionResult> GetNodes(CancellationToken ct)
{
    var threshold = options.Value.NodeListCursorThreshold; // default 200
    var result = await nodeService.GetNodesWithGateAsync(threshold, ct);

    if (!result.PaginationRequired)
        return Ok(result.Items);

    return Ok(new NodeListGateResponse(
        PaginationRequired: true,
        Items: Array.Empty<NodeDto>(),
        NextCursor: result.NextCursor,
        CursorEndpoint: "/api/v1/nodes/cursor"));
}
```

`NodeListGateResponse` is a new DTO in `MSOSync.Api.Dtos.Nodes`. The response shape changes only when `PaginationRequired = true`; existing consumers that handle a JSON array continue to work when below the threshold.

---

## 6. Overview Snapshot Efficiency

### 6.1 Current state

`IDashboardQueryService.GetSummaryAsync` is called by `GET /api/v1/dashboard/summary`. Based on the `DashboardSummaryDto` fields (`PendingEvents`, `QueueDepth`, `EventsToday`, `TransportErrors24h`) the implementation issues multiple queries — one per metric. There is no explicit cache documented in the interface, but the spec requires that this be evaluated.

### 6.2 Required behavior in 2D.4

The dashboard summary endpoint must be backed by an in-process snapshot cache with a configurable TTL:

- Configuration key: `Dashboard:SummaryTtlSeconds` (default: 30).
- Cache key: `dashboard:summary:{tenantId}`.
- On miss: execute all sub-queries sequentially (no `Task.WhenAll` on shared DbContext) and populate cache.
- On hit: return cached result immediately without touching the database.

The `DashboardSummaryDto.GeneratedAt` field already exists to convey when the snapshot was computed. Its value must reflect the time of the last cache population, not `DateTime.UtcNow`.

### 6.3 Node-count aggregation

`DashboardSummaryDto` counts `ReachableNodes`, `DegradedNodes`, `UnreachableNodes`, `UnknownNodes`. These must use the `GROUP BY connectivity_status` pattern (section 4.3), not four separate `CountAsync` calls. The new `IX_sync_node_connectivity_status` index (section 4.2, Index 2) covers this query.

### 6.4 Event and batch aggregations

`PendingEvents` must be computed as:

```csharp
var pendingEvents = await db.DataEvents
    .AsNoTracking()
    .CountAsync(e => !e.IsProcessed && e.TenantId == tenantId, ct);
```

Covered by the existing `IX_sync_data_event_channel_processed` index on `(channel_id, is_processed)`. For a tenant-scoped count the index is not covering on `tenant_id`, but after the multi-tenancy migration adds `tenant_id` to high-traffic tables, a composite index on `(tenant_id, is_processed)` should be added. This is noted as a follow-up item (Phase 2D.5).

`QueueDepth` is the count of `SyncOutgoingBatch` rows with `status = 0` (New):

```csharp
var queueDepth = await db.OutgoingBatches
    .AsNoTracking()
    .CountAsync(b => b.Status == 0 && b.TenantId == tenantId, ct);
```

Covered by `IX_sync_outgoing_batch_node_status` on `(node_id, status)` — for a tenant-wide count without `node_id` this index is not optimal, but it is a composite index that SQL can still use for status filtering. After the 2D.5 tenant-scoped index pass, add `IX_sync_outgoing_batch_tenant_status` on `(tenant_id, status)`.

---

## 7. Testing

### 7.1 Benchmark test at 1000 simulated nodes

A new test project `MSOSync.Benchmarks` (BenchmarkDotNet) is created with the following benchmarks:

#### Benchmark 1: `TopologyGraphBenchmark`

- Seeds 1000 nodes across 200 groups with 400 router edges into LocalDB (or SQL Server test instance).
- Measures `GetTopologyGraphAsync` wall-clock time.
- Target: < 500 ms at P95.

#### Benchmark 2: `NodeCursorPageBenchmark`

- Seeds 1000 nodes.
- Measures cursor page retrieval (first page, page 5, page 20) for `GET /api/v1/nodes/cursor`.
- Target: < 50 ms per page at P95.

#### Benchmark 3: `BulkFanOutBenchmark`

- Seeds 1000 active nodes.
- Calls `IBulkRoutingService.FanOutAsync` once.
- Measures total wall-clock time.
- Target: < 100 ms for 1000-node fan-out (single bulk insert vs 1000 individual inserts baseline measured and recorded).

#### Benchmark 4: `DashboardSummaryBenchmark`

- Seeds 1000 nodes with mixed connectivity statuses.
- Measures `IDashboardQueryService.GetSummaryAsync` on cache miss.
- Target: < 100 ms for the full summary computation.

### 7.2 Integration tests

In the existing `MSOSync.Tests` project:

**Test class: `NodeCursorPaginationTests`**

- `GetNodesCursor_FirstPage_ReturnsCorrectItems`
- `GetNodesCursor_SubsequentPage_ContinuesFromCursor`
- `GetNodesCursor_TamperedCursor_Returns400`
- `GetNodesCursor_ExhaustedPagination_HasMoreFalse`
- `GetNodes_BelowThreshold_ReturnsFullList`
- `GetNodes_AboveThreshold_ReturnsPaginationRequired`

**Test class: `TopologyGroupNodePaginationTests`**

- `GetGroupNodes_FirstPage_ReturnsPageSizeItems`
- `GetGroupNodes_SubsequentPage_DoesNotDuplicate`
- `GetGroupNodes_LastPage_HasMoreFalse`

**Test class: `BulkRoutingTests`**

- `FanOut_1000Nodes_InsertsAllBatches`
- `FanOut_NoEligibleNodes_ReturnsEmptyList`
- `FanOut_PartiallyDisabledRouters_OnlyInsertsForEnabledRouters`

**Test class: `ClusterSummaryProjectionTests`**

- `QueryNodeStates_DoesNotMaterializeFullEntities` (verified via query log interceptor)

### 7.3 Migration rollback test

An automated test verifies that `M031_ScaleIndexes.Down()` successfully drops all five indexes. This test runs in CI using LocalDB.

---

## 8. Global Constraints

The following constraints apply to all code written in Phase 2D.4. These are non-negotiable.

| Constraint | Rule |
|---|---|
| `AsNoTracking()` | All read queries must use `AsNoTracking()`. No exceptions. |
| No `Task.WhenAll` on DbContext | `AppDbContext` is not thread-safe. Sequential `await` only within a single scope. |
| Cursor is opaque | Raw `node_id`, `batch_id`, or `event_id` must never appear in the `nextCursor` wire value. Always encoded through `CursorSigner`. |
| HMAC verification | `CursorToken.Decode` / `CursorSigner.Decode` must be used on all cursor inputs. Malformed tokens return HTTP 400, not 500. |
| Additive pagination only | No list endpoint removes or renames existing query parameters. Cursor parameters are additions. Offset endpoints are deprecated with `Deprecation` headers, not removed. |
| EF migrations for indexes only | `M031_ScaleIndexes` adds indexes only. No column adds, no table alters, no nullable changes. |
| `ProducesResponseType` | All modified controller actions must declare `ProducesResponseType` for every returned HTTP status code. |
| Projection | No query materializes more columns than required by the DTO. `Select` to projected type before `ToListAsync`. |
| Threshold configuration | `NodeListCursorThreshold` must be read from `IOptions<PaginationOptions>`, not hardcoded. |
| Max page size | All cursor-paginated node endpoints clamp `pageSize` to 200. Topology group node endpoint clamps to 500. |
| `nodeIds` filter validation | `GET /api/v1/topology/graph?nodeIds=` rejects requests with more than 50 node IDs with HTTP 400. |
| Bulk insert | `IBulkRoutingService.FanOutAsync` uses raw SQL via `ExecuteSqlRawAsync` with parameterized inputs only. No string interpolation in SQL. |
| No shared state | `BulkRoutingService` is registered as scoped. It must not share any mutable state across requests. |
| `CursorSigner.EncodeString` / `DecodeString` | Must be added to `MSOSync.Common.Pagination.CursorToken` (not only in `MSOSync.Metadata.Pagination.CursorSigner`) so that any future non-API consumers can encode string-keyed cursors without taking a dependency on the metadata layer. |

---

## 9. Delivery Checklist

All items must be complete before 2D.4 is merged to `main`.

- [ ] `CursorToken.EncodeString` / `DecodeString` added to `MSOSync.Common.Pagination`.
- [ ] `CursorSigner.EncodeString` / `DecodeString` added to `MSOSync.Metadata.Pagination`.
- [ ] `GET /api/v1/nodes/cursor` endpoint implemented and returning `CursorPageResult<NodeDto>`.
- [ ] `GET /api/v1/nodes` threshold gate implemented; `NodeListCursorThreshold` configuration key documented.
- [ ] `GET /api/v1/nodes/paged` `Deprecation` header added.
- [ ] `ITopologyQueryService.GetTopologyGraphAsync(string[]? nodeIdFilter, CancellationToken ct)` overload added.
- [ ] `GetTopologyGraphAsync` implementation uses projection-only node count queries.
- [ ] `GetTopologySummaryAsync` uses single `GROUP BY connectivity_status` query.
- [ ] `GetGroupNodesAsync` returns `CursorPageResult<TopologyGroupNodeDto>` with cursor and pageSize parameters.
- [ ] `ClusterSummaryQueryService.QueryNodeStatesAsync` rewritten to single `GROUP BY` query.
- [ ] `IBulkRoutingService` and `BulkRoutingService` implemented in `MSOSync.Routing`.
- [ ] Batch pipeline caller updated to use `IBulkRoutingService.FanOutAsync`.
- [ ] `DashboardSummaryDto` cache added with `Dashboard:SummaryTtlSeconds` configuration.
- [ ] `BatchErrorQueryService.GetBatchErrorSummaryAsync` collapsed from 3 queries to 1.
- [ ] `M031_ScaleIndexes` migration created with all 5 indexes; `Down()` drops all 5.
- [ ] All integration tests pass.
- [ ] `MSOSync.Benchmarks` project created with 4 benchmarks; baseline numbers recorded in `docs/superpowers/benchmarks/2D-4-baseline.md`.
- [ ] No `Task.WhenAll` on shared `AppDbContext` introduced.
- [ ] All new controller actions have `ProducesResponseType` for all returned status codes.
