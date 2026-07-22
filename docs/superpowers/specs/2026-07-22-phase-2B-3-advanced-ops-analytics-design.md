# Phase 2B.3 — Advanced Operations Analytics Design

**Status:** Approved (with minor revisions incorporated)
**Date:** 2026-07-22

---

## Goal

Four operator-facing analytics modules built entirely on existing infrastructure — no new DB migrations. Gives operators a live cluster command center, config version diff, enhanced audit querying, and a Gantt-style operations timeline.

---

## Scope

| Module | New route | Primary deliverable |
|---|---|---|
| Cluster Operations Dashboard | `/operations/cluster` | Live tactical view: active ops, node states, rolling wave progress, replay progress |
| Configuration Comparison | Slide-out panel on TemplatesPage | Side-by-side JSON diff between two template versions |
| Audit Explorer | Enhancement to `/operations/activity` | Multi-value filtering, saved filters via preferences, entity history tab |
| Operations Timeline | `/operations/timeline` | Recharts Gantt of concurrent operations by time range |

**No new database migration.** All modules read from existing tables (`sync_operation`, `sync_node`, `sync_rolling_operation`, `sync_rolling_item`, `sync_replay_request`, `sync_configuration_template_version`, `sync_audit`, `sync_node_lifecycle_history`).

---

## Global Constraints

- All Phase 2A rules (`.github/PULL_REQUEST_TEMPLATE.md`): named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3.
- RULE-CTL-2: no controller injects `AppDbContext` directly.
- No new EF migrations.
- All work commits directly to `main`.
- `ICurrentTenantAccessor` auto-populates `TenantId`; all new queries must be tenant-scoped.
- Project `MSOSync.Metadata` must not reference `MSOSync.Batch` or `MSOSync.Routing`.
- All new query methods: `AsNoTracking()`, projection directly to DTO, no lazy loading, no `Include()` unless required. These are read-only analytics endpoints.
- All timestamps are UTC internally; conversion to local time is frontend-only.

---

## Authorization Table

| Module | Required Permission |
|---|---|
| Cluster Dashboard | `ViewerOrAbove` policy |
| Configuration Comparison | `ViewerOrAbove` policy + `ManageConfigurations` permission |
| Audit Explorer | `ViewerOrAbove` policy |
| Operations Timeline | `ViewerOrAbove` policy |

---

## Module 1 — Cluster Operations Dashboard

### Purpose

Single-screen command center for maintenance windows. Aggregates active operations, rolling wave state, replay progress, and recent node lifecycle changes. Auto-refreshes via SignalR.

`ClusterSummaryQueryService` is an **aggregator**, not a repository. It fires parallel sub-queries and assembles a single response. It must never grow to mix business logic with data access — if it exceeds ~150 lines, extract sub-query helpers.

### Backend

**New files:**
- `src/MSOSync.Metadata/Operations/Cluster/IClusterSummaryQueryService.cs`
- `src/MSOSync.Metadata/Operations/Cluster/ClusterSummaryQueryService.cs`
- `src/MSOSync.Metadata/Operations/Cluster/Dtos/ClusterSummaryDto.cs`
- `src/MSOSync.Api/Controllers/ClusterController.cs`
- Register in `MetadataServiceExtensions.cs`

**API endpoint:**
```
GET /api/v1/cluster/summary
Authorization: ViewerOrAbove
```

**Response DTO:**
```csharp
// src/MSOSync.Metadata/Operations/Cluster/Dtos/ClusterSummaryDto.cs
namespace MSOSync.Metadata.Operations.Cluster.Dtos;

public sealed record ClusterSummaryDto(
    NodeStateCountsDto                       NodeStates,
    OperationCountsDto                       OperationCounts,
    IReadOnlyList<ActiveOperationSummaryDto> ActiveOperations,
    IReadOnlyList<RollingWaveSummaryDto>     ActiveRollingOps,
    IReadOnlyList<ReplayOperationSummaryDto> ActiveReplays,
    IReadOnlyList<NodeStateChangeDto>        RecentNodeChanges);

public sealed record NodeStateCountsDto(
    int Total, int Active, int Maintenance, int Draining, int Offline, int Failed);

public sealed record OperationCountsDto(
    int Running, int Pending, int SucceededToday, int FailedToday);

public sealed record ActiveOperationSummaryDto(
    Guid    OperationId,
    string  Type,      // serialized as string; internally OperationType enum
    string  Status,    // serialized as string; internally OperationStatus enum
    string? NodeId,
    int?    ProgressPercent,
    string? ProgressMessage,
    DateTime StartedAt);

public sealed record RollingWaveSummaryDto(
    Guid   OperationId,
    string Mode,         // "Maintenance" | "Upgrade"
    string Status,
    int    WaveIndex,
    int    TotalWaves,
    int    NodesDone,
    int    NodesTotal,
    int    NodesFailed);

// Previously named ReplayProgressDto — renamed to ReplayOperationSummaryDto
public sealed record ReplayOperationSummaryDto(
    Guid   OperationId,
    string ReplayMode,   // "FailedDelivery" | "MissedData" | "Both"
    string Status,
    int    ItemsDone,
    int    ItemsTotal,
    int    ItemsFailed);

public sealed record NodeStateChangeDto(
    string   NodeId,
    string   FromState,
    string   ToState,
    string   Trigger,
    DateTime OccurredAt);
```

**Service implementation:**
`ClusterSummaryQueryService` fires 6 queries via `Task.WhenAll`:

1. **NodeQuery** — `sync_node` — count by `status` column (maps `Active`→Active, `Maintenance`→Maintenance, `Draining`→Draining, `Decommissioning`+`Decommissioned`→Offline, `Offline`→Offline, `Failed`→Failed). Note: `SyncNode.Status` stores the lifecycle state string per Epic 12B.1.
2. **OperationQuery** — `sync_operation` — count Running + Pending; count Completed/Failed where `completed_at >= UTC today midnight`.
3. **ActiveOpsQuery** — `sync_operation` WHERE `status IN ('Pending','Running')` ORDER BY `started_at DESC` LIMIT 50.
4. **RollingQuery** — `sync_rolling_operation` WHERE `status IN ('Pending','Running')` — join `sync_rolling_item` for done/total/failed counts. LIMIT 10.
5. **ReplayQuery** — `sync_replay_request` WHERE `status IN ('Pending','Running')` — join `sync_replay_item` for counts. LIMIT 10.
6. **LifecycleQuery** — `sync_node_lifecycle_history` WHERE `occurred_at >= UTC NOW - 15 minutes` ORDER BY `occurred_at DESC` LIMIT 50.

All queries tenant-scoped via EF global filters. All `AsNoTracking()`, project directly to DTO.

**Controller:**
```csharp
// src/MSOSync.Api/Controllers/ClusterController.cs
[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ClusterController(IClusterSummaryQueryService svc) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ClusterSummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await svc.GetSummaryAsync(ct));
}
```

### Frontend

**New files:**
- `src/MSOSync.Frontend/src/features/operations/cluster/ClusterPage.tsx`
- `src/MSOSync.Frontend/src/shared/api/cluster.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useClusterSummary.ts`
- `src/MSOSync.Frontend/src/shared/types/cluster.ts`
- `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/ClusterPage.test.tsx`

**Layout (4 panels in 2×2 grid + event strip):**
```
┌──────────────────────────┬──────────────────────────────────────────┐
│  Node State Distribution │  Active Operations (scrollable list)     │
│  Badge counts: Active/   │  Each row: type chip + progress bar +    │
│  Maintenance/Draining/   │  started time + cancel button (if auth)  │
│  Offline/Failed          │                                          │
├──────────────────────────┼──────────────────────────────────────────┤
│  Rolling Operations      │  Replay Operations                       │
│  Wave progress bars      │  Item progress bars                      │
│  nodes done/total/failed │  items done/total/failed                 │
└──────────────────────────┴──────────────────────────────────────────┘
│  Recent Node State Changes (horizontal event strip, newest left,    │
│  shows last 15 min of lifecycle transitions)                        │
└─────────────────────────────────────────────────────────────────────┘
```

**Cache policy:**
```typescript
staleTime: 10_000,    // 10s
gcTime:    60_000,    // 60s
refetchInterval: 15_000,
```

**SignalR:** `OperationChanged` and `NodeLifecycleChanged` events call `queryClient.invalidateQueries(clusterKeys.summary)`. No payload broadcasting — invalidation only.

**Route:** `/operations/cluster` — add to router and left-nav under Operations section after Jobs.

**Unit tests:** Mock `getClusterSummary`. Assert: node state badges render, active ops list renders, empty state ("No active operations") renders, node change strip renders last 15 min header.

---

## Module 2 — Configuration Comparison

### Purpose

Side-by-side diff view of two template versions. Accessible from version history on the Templates page without a route change. Lets admins review what changed before rolling out a new version.

### Backend

**New files:**
- `src/MSOSync.Metadata/Configuration/JsonDiffEngine.cs` — internal static class, pure diff algorithm
- `src/MSOSync.Metadata/Configuration/IConfigurationComparisonService.cs`
- `src/MSOSync.Metadata/Configuration/ConfigurationComparisonService.cs` — delegates diff to `JsonDiffEngine`
- `src/MSOSync.Metadata/Configuration/Dtos/ConfigVersionDiffDto.cs`
- Register in `MetadataServiceExtensions.cs`

**Separation of concerns:**
```
ConfigurationComparisonService
  → loads versions from DB
  → delegates to JsonDiffEngine.Diff(json1, json2)
  → returns ConfigVersionDiffDto

JsonDiffEngine (internal static)
  → FlattenJson(JsonElement) → Dictionary<string, string>
  → Diff(json1, json2) → IReadOnlyList<DiffEntryDto>
  → reusable by rollout preview, node override comparison, drift analysis
```

**New endpoint on existing `ConfigurationTemplateController`:**
```
GET /api/v1/configuration/templates/{id}/compare?v1={versionNumber}&v2={versionNumber}
```
**API contract:** Returns `400` if `v1 == v2` (identical versions). Returns `404` if either version does not exist for the given template.

**Response DTO:**
```csharp
// src/MSOSync.Metadata/Configuration/Dtos/ConfigVersionDiffDto.cs
namespace MSOSync.Metadata.Configuration.Dtos;

public sealed record ConfigVersionDiffDto(
    Guid                        TemplateId,
    int                         V1,
    int                         V2,
    string                      V1Label,   // e.g. "v3 (Published 2026-07-20)"
    string                      V2Label,
    IReadOnlyList<DiffEntryDto> Entries,
    bool                        HasChanges);

public sealed record DiffEntryDto(
    string  Key,
    string  ChangeType,   // "Added" | "Removed" | "Changed" | "Unchanged"
    string? OldValue,
    string? NewValue);
```

**`JsonDiffEngine` algorithm:**
1. Flatten both `JsonDocument`s to `Dictionary<string, string>` using dot-notation keys (e.g., `"database.host"`). Recurse into objects. Arrays are atomic — serialize to compact JSON string. Do not attempt deep array diffs.
2. Produce `DiffEntryDto` per key: `Removed` (in V1 only), `Added` (in V2 only), `Changed` (both, different value), `Unchanged` (both, same value).
3. Sort order: Changed first, then Added, then Removed, then Unchanged.
4. Return `HasChanges = entries.Any(e => e.ChangeType != "Unchanged")`.

**Inject into `ConfigurationTemplateController`:**
```csharp
[HttpGet("{id:guid}/compare")]
[ProducesResponseType(typeof(ConfigVersionDiffDto), 200)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
[ProducesResponseType(typeof(ProblemDetails), 404)]
public async Task<IActionResult> Compare(
    Guid id, [FromQuery] int v1, [FromQuery] int v2, CancellationToken ct)
{
    await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
    if (v1 == v2) return BadRequest(new ProblemDetails { Title = "v1 and v2 must differ." });
    var diff = await comparisonSvc.CompareAsync(id, v1, v2, ct);
    return Ok(diff);
}
```

**Unit tests** (`MSOSync.MetadataTests`): added keys, removed keys, changed values, unchanged, nested object flattening, array as atomic, empty configs, `v1 == v2` handled at controller, missing version throws `NotFoundException`.

### Frontend

**New files:**
- `src/MSOSync.Frontend/src/features/operations/configuration/components/ConfigComparePanel.tsx`
- `src/MSOSync.Frontend/src/shared/api/configComparison.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useConfigComparison.ts`
- `src/MSOSync.Frontend/src/features/operations/configuration/components/__tests__/ConfigComparePanel.test.tsx`

**Trigger:** "Compare versions" button added to existing TemplatesPage version history drawer.

**Panel layout (slide-out):**
```
┌─────────────────────────────────────────────────────────────┐
│ Compare Versions — Template: "Production DB Config"         │
│ [Version dropdown v1 ▼]  ←→  [Version dropdown v2 ▼]       │
├────────────────┬──────────┬───────────────┬─────────────────┤
│ Key            │ Change   │ Old Value     │ New Value       │
│ database.host  │ Changed  │ server01      │ server02        │ ← yellow
│ database.port  │ Added    │ —             │ 5432            │ ← green
│ cache.ttl      │ Removed  │ 300           │ —               │ ← red
│ app.name       │ ─        │ MyApp         │ MyApp           │ ← default
└────────────────┴──────────┴───────────────┴─────────────────┘
```

Unchanged rows hidden by default with a "Show X unchanged" toggle.

**Hook:** `useConfigComparison(templateId, v1, v2)` — `useQuery` enabled when both v1 and v2 are selected and differ.

**Tests:** Diff renders correctly, empty diff shows "No differences found", version picker updates query, same-version selection shows disabled state.

---

## Module 3 — Audit Explorer

### Purpose

Enhances the existing `/operations/activity` Audit tab with multi-value filter chips (replacing single text inputs), saved filter sets via user preferences, and a new "Entity History" tab showing all audit events for a specific entity.

### Backend

**Modified files:**
- `src/MSOSync.Metadata/Audit/AuditFilter.cs` — add multi-value fields
- `src/MSOSync.Metadata/Audit/AuditFilterValidator.cs` — extend validator with array size limits
- `src/MSOSync.Metadata/Audit/IAuditQueryService.cs` — add `GetEntityHistoryAsync`
- `src/MSOSync.Metadata/Audit/AuditQueryService.cs` — implement multi-value + entity history
- `src/MSOSync.Api/Controllers/AuditController.cs` — add entity history endpoint

**Extended `AuditFilter`:**
```csharp
// src/MSOSync.Metadata/Audit/AuditFilter.cs
namespace MSOSync.Metadata.Audit;

public sealed class AuditFilter
{
    // Existing single-value fields (kept for backward compat)
    public string?   Username          { get; set; }
    public string?   ActionName        { get; set; }
    // New multi-value fields (take precedence when non-empty)
    public string[]? Usernames         { get; set; }  // OR within group
    public string[]? ActionNames       { get; set; }  // OR within group
    public string[]? EntityTypes       { get; set; }  // OR within group
    public string[]? Sources           { get; set; }  // OR within group
    // Existing
    public DateTime? From              { get; set; }
    public DateTime? To                { get; set; }
    public string?   Cursor            { get; set; }
    public bool      IncludeTotalCount { get; set; }
    public int       PageSize          { get; set; } = 50;
}
```

**Filter size limits (in `AuditFilterValidator`):**
- `Usernames` ≤ 10 items
- `ActionNames` ≤ 10 items
- `EntityTypes` ≤ 10 items
- `Sources` ≤ 10 items
- Combined total filter values ≤ 40 (prevents enormous SQL `IN (...)` clauses)
- `pageSize` 1–200

**Query logic in `AuditQueryService`:**
```
effectiveUsernames   = Usernames?.Length > 0  ? Usernames  : (Username   != null ? [Username]   : null)
effectiveActions     = ActionNames?.Length > 0 ? ActionNames : (ActionName != null ? [ActionName] : null)
effectiveEntityTypes = EntityTypes  // null = all
effectiveSources     = Sources      // null = all

WHERE (effectiveUsernames   is null OR username     IN effectiveUsernames)
  AND (effectiveActions     is null OR action_name  IN effectiveActions)
  AND (effectiveEntityTypes is null OR entity_type  IN effectiveEntityTypes)
  AND (effectiveSources     is null OR source       IN effectiveSources)
  AND (From is null OR occurred_at >= From)
  AND (To   is null OR occurred_at <= To)
```

**New method on `IAuditQueryService`:**
```csharp
Task<CursorPageResult<AuditEventDto>> GetEntityHistoryAsync(
    string entityType, string entityId,
    string? cursor, int pageSize,
    CancellationToken ct);
```
Query: `WHERE entity_type = @entityType AND entity_id = @entityId ORDER BY occurred_at DESC`.

**New endpoint on `AuditController`:**
```csharp
// GET /api/v1/audit/entity/{entityType}/{entityId}?cursor=&pageSize=50
[HttpGet("entity/{entityType}/{entityId}")]
[ProducesResponseType(typeof(CursorPageResult<AuditEventDto>), 200)]
public async Task<IActionResult> GetEntityHistory(
    string entityType, string entityId,
    [FromQuery] string? cursor, [FromQuery] int pageSize = 50,
    CancellationToken ct = default)
{
    var result = await audit.GetEntityHistoryAsync(entityType, entityId, cursor, pageSize, ct);
    return Ok(result);
}
```

**Saved filters:** Stored via existing `PUT /api/v1/preferences/audit.savedFilters` with value `Array<{ name: string; filter: AuditFilterParams }>`. Frontend reads `GET /api/v1/preferences` → `audit.savedFilters` key. No new backend needed.

### Frontend

**Modified files:**
- `src/MSOSync.Frontend/src/features/operations/activity/AuditPage.tsx` — add multi-select filter chips, saved filters sidebar, Entity History tab
- `src/MSOSync.Frontend/src/shared/api/audit.ts` — extend `getAudits` params + add `getEntityHistory`
- `src/MSOSync.Frontend/src/shared/hooks/useAudit.ts` — extend hook params

**New files:**
- `src/MSOSync.Frontend/src/features/operations/activity/components/AuditFilterBar.tsx`
- `src/MSOSync.Frontend/src/features/operations/activity/components/SavedFiltersPanel.tsx`
- `src/MSOSync.Frontend/src/features/operations/activity/components/EntityHistoryTab.tsx`
- `src/MSOSync.Frontend/src/features/operations/activity/components/__tests__/AuditFilterBar.test.tsx`

**Filter bar layout:**
```
[Entity Types ▼ ×Node ×Channel]  [Actions ▼ ×NODE_APPROVED]  [Usernames ▼]  [Sources ▼]
[From: ──────]  [To: ──────]  [Save Filter]  [Saved ▼]  [Clear All]
```

**Entity History tab:**
```
Entity Type: [dropdown]  Entity ID: [text input]  [Load]
[cursor-paginated grid of audit events for that entity]
```

**Saved filter persistence:**
```typescript
// Save:  PUT /api/v1/preferences/audit.savedFilters
// Value: Array<{ name: string; filter: AuditFilterParams }>
// Load:  GET /api/v1/preferences → ['audit.savedFilters']
```

**SignalR:** No SignalR integration — audit is not real-time. Manual refresh button only.

---

## Module 4 — Operations Timeline

### Purpose

Gantt-style view of operations by time range. Operators use it to understand operation overlap, duration, and sequencing. Click a bar to open the existing detail panel. All timestamps are UTC.

### Backend

**New files:**
- `src/MSOSync.Metadata/Operations/Timeline/IOperationTimelineService.cs`
- `src/MSOSync.Metadata/Operations/Timeline/OperationTimelineService.cs`
- `src/MSOSync.Metadata/Operations/Timeline/Dtos/OperationTimelineDto.cs`
- Register in `MetadataServiceExtensions.cs`

**New endpoint on existing `OperationsController`:**
```
GET /api/v1/operations/timeline?from={ISO-UTC}&to={ISO-UTC}&types[]={csv}&limit=200
Authorization: ViewerOrAbove
```

**Response DTO:**
```csharp
// src/MSOSync.Metadata/Operations/Timeline/Dtos/OperationTimelineDto.cs
namespace MSOSync.Metadata.Operations.Timeline.Dtos;

public sealed record OperationTimelineDto(
    IReadOnlyList<OperationTimelineItemDto> Items,
    DateTime  From,
    DateTime  To,
    bool      HasMore,
    int       ReturnedCount);

public sealed record OperationTimelineItemDto(
    Guid      OperationId,
    string    Type,    // serialized as string
    string    Status,  // serialized as string
    string?   NodeId,
    string?   Label,
    DateTime  StartedAt,
    DateTime? CompletedAt,  // null = still running
    int?      ProgressPercent);
```

**`HasMore`:** `true` when the total number of matching rows exceeds `limit`. Signals to frontend that the timeline is truncated.

**Service implementation:**
`OperationTimelineService.GetTimelineAsync(DateTime from, DateTime to, string[]? types, int limit, CancellationToken ct)`:
- Query `sync_operation` WHERE `started_at >= from AND started_at <= to`
- If `types` non-null: AND `operation_type IN types`
- ORDER BY `started_at ASC, operation_id ASC` (secondary sort for deterministic ordering)
- Fetch `limit + 1` rows; if count > limit, set `HasMore = true`, return only `limit` rows
- `Label` = `ProgressMessage ?? Summary ?? Type`
- All timestamps UTC. `AsNoTracking()`, project to DTO.

**Validation in endpoint:**
- `from` must be before `to`; max range 30 days; `limit` 1–500 (default 200)
- `types` values must be valid operation type strings

**Inject into `OperationsController`:**
```csharp
[HttpGet("timeline")]
[ProducesResponseType(typeof(OperationTimelineDto), 200)]
[ProducesResponseType(typeof(ProblemDetails), 400)]
public async Task<IActionResult> GetTimeline(
    [FromQuery] DateTime  from,
    [FromQuery] DateTime  to,
    [FromQuery] string?   types  = null,
    [FromQuery] int       limit  = 200,
    CancellationToken ct = default)
{
    if (from >= to) return BadRequest(new ProblemDetails { Title = "from must be before to." });
    if ((to - from).TotalDays > 30) return BadRequest(new ProblemDetails { Title = "Range cannot exceed 30 days." });
    var typeArray = SplitCsv(types);
    var result = await timelineSvc.GetTimelineAsync(from, to, typeArray, Math.Min(limit, 500), ct);
    return Ok(result);
}
```

### Frontend

**New files:**
- `src/MSOSync.Frontend/src/features/operations/timeline/TimelinePage.tsx`
- `src/MSOSync.Frontend/src/shared/api/operationTimeline.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useOperationTimeline.ts`
- `src/MSOSync.Frontend/src/shared/types/timeline.ts`
- `src/MSOSync.Frontend/src/features/operations/timeline/__tests__/TimelinePage.test.tsx`

**Layout:**
```
[From: ──UTC──] [To: ──UTC──] [Types: ▼ Export Rollout Replay ×] [Refresh]
─────────────────────────────────────────────────────────────────────────────
Gantt chart (Recharts ComposedChart, layout="vertical"):
  Y axis: operation type groups (Export / RollingMaintenance / BatchReplay / ...)
  X axis: time in ms (domain=[minStartMs, max(endMs, nowMs)])
  Bars: custom <shape> renders a <rect> from x0=startedAt to x1=completedAt ?? now
  Color by Status: Running=blue, Completed=green, Failed=red, Cancelled=grey
  Tooltip: OperationId, NodeId, Label, Duration, Status (all in UTC)
─────────────────────────────────────────────────────────────────────────────
[⚠ Showing 200 of 347 operations — narrow range or add type filters to see all]
─────────────────────────────────────────────────────────────────────────────
Click bar → open RollingOperationDetailPanel | ReplayDetailPanel
            (fallback: navigate to /operations/jobs?id={operationId})
```

**`HasMore` banner:** Shown when `response.hasMore === true`. Instructs operator to narrow range or filter by type.

**Default date range:** Last 24 hours (UTC). Max selectable: 30 days.

**SignalR:** `OperationChanged` invalidates `timelineKeys.list(from, to, types)`. No payload broadcasting.

**Route:** `/operations/timeline` — add to router and left-nav under Operations section.

**Tests:** Bars render for mock data, empty state renders ("No operations in this range"), date range picker updates query, `HasMore` banner shown when `hasMore: true`, click handler calls detail panel.

---

## Service Registration

Add to `MetadataServiceExtensions.cs` (Phase 2B.3 block):
```csharp
// Phase 2B.3 — Advanced Operations Analytics
services.AddScoped<IClusterSummaryQueryService, ClusterSummaryQueryService>();
services.AddScoped<IConfigurationComparisonService, ConfigurationComparisonService>();
services.AddScoped<IOperationTimelineService, OperationTimelineService>();
// IAuditQueryService: existing registration, impl extended with new methods
```

---

## Testing Strategy

### Backend unit tests

| Test class | Project | Approx. tests |
|---|---|---|
| `ClusterSummaryQueryServiceTests` | `MSOSync.MetadataTests` | ~8 |
| `ConfigurationComparisonServiceTests` | `MSOSync.MetadataTests` | ~8 |
| `JsonDiffEngineTests` | `MSOSync.MetadataTests` | ~6 |
| `OperationTimelineServiceTests` | `MSOSync.MetadataTests` | ~6 |
| `AuditQueryServiceMultiFilterTests` | `MSOSync.MetadataTests` | ~6 |

All use `TestDbContext.Create()` (SQLite in-memory).

### Backend integration tests

| Test class | File | Notes |
|---|---|---|
| `ClusterApiTests` | `tests/MSOSync.IntegrationTests/Operations/ClusterApiTests.cs` | Includes tenant isolation test |
| `ConfigCompareApiTests` | `tests/MSOSync.IntegrationTests/Configuration/ConfigCompareApiTests.cs` | Includes tenant isolation test |
| `AuditExplorerApiTests` | `tests/MSOSync.IntegrationTests/Operations/AuditExplorerApiTests.cs` | Includes tenant isolation test |
| `OperationTimelineApiTests` | `tests/MSOSync.IntegrationTests/Operations/OperationTimelineApiTests.cs` | Includes tenant isolation test |

All use `[Collection("Lifecycle")]` + `LifecycleFixture`. Environmental failures (no SQL Server) acceptable; build must pass.

**Tenant isolation test pattern (required for all 4 modules):**
Each integration test file must include a test verifying that Tenant A data does not appear in Tenant B's response. Pattern: seed two tenant scopes (SystemTenant + a second tenant), query as Tenant A, assert Tenant B's records are absent.

### Frontend tests (React Testing Library + Vitest + MSW)

| Test file | Approx. tests |
|---|---|
| `ClusterPage.test.tsx` | ~5 |
| `ConfigComparePanel.test.tsx` | ~5 |
| `AuditFilterBar.test.tsx` | ~5 |
| `TimelinePage.test.tsx` | ~5 |

---

## Completion Criteria

1. All plan tasks complete, committed to `main`.
2. `dotnet test D:\MSOSync\MSOSync.sln` — all unit assemblies green; only accepted environmental integration failures.
3. `npm run build` in `src/MSOSync.Frontend` — 0 TypeScript errors.
4. `docs/architecture/service-responsibility-map.md` updated with new services.
5. `docs/architecture/test-infrastructure.md` updated with new test counts.
6. **Performance targets** (verified via integration test stopwatch assertions):
   - `GET /api/v1/cluster/summary` — p95 < 250 ms under typical load (< 1000 nodes, < 50 active ops)
   - `GET /api/v1/operations/timeline` — p95 < 300 ms for 30-day range
   - `GET /api/v1/audit` with multi-value filters — cursor pagination maintained, first page < 200 ms
   - `GET /api/v1/configuration/templates/{id}/compare` — < 100 ms for typical template sizes (≤ 100 keys)
