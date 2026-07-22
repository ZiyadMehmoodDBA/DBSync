# Phase 2B.3 — Advanced Operations Analytics Design

**Status:** Approved  
**Date:** 2026-07-22  
**Spec author:** AI + CTO review

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

**No new database migration.** All modules read from existing tables (`sync_operation`, `sync_node`, `sync_rolling_operation`, `sync_rolling_item`, `sync_replay_request`, `sync_configuration_template_version`, `sync_audit`).

---

## Global Constraints

- All Phase 2A rules (`.github/PULL_REQUEST_TEMPLATE.md`): named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3.
- RULE-CTL-2: no controller injects `AppDbContext` directly.
- No new EF migrations.
- All work commits directly to `main`.
- `ICurrentTenantAccessor` auto-populates `TenantId`; all new queries must be tenant-scoped.
- Project `MSOSync.Metadata` must not reference `MSOSync.Batch` or `MSOSync.Routing`.

---

## Module 1 — Cluster Operations Dashboard

### Purpose

Single-screen command center for maintenance windows. Aggregates active operations, rolling wave state, replay progress, and recent node lifecycle changes. Auto-refreshes via SignalR.

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
    IReadOnlyList<ReplayProgressDto>         ActiveReplays,
    IReadOnlyList<NodeStateChangeDto>        RecentNodeChanges);

public sealed record NodeStateCountsDto(
    int Total, int Active, int Maintenance, int Draining, int Offline, int Failed);

public sealed record OperationCountsDto(
    int Running, int Pending, int SucceededToday, int FailedToday);

public sealed record ActiveOperationSummaryDto(
    Guid    OperationId,
    string  Type,
    string  Status,
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

public sealed record ReplayProgressDto(
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
`ClusterSummaryQueryService` performs 6 queries in parallel:
1. `sync_node` — count by `lifecycle_state` (Active/Maintenance/Draining/Offline/Failed)
2. `sync_operation` — count Running + Pending; count Completed/Failed where `completed_at >= today UTC`
3. `sync_operation` WHERE `status IN ('Pending','Running')` — all active ops (limit 50)
4. `sync_rolling_operation` + `sync_rolling_item` WHERE `status IN ('Pending','Running')` — wave summary (limit 10)
5. `sync_replay_request` WHERE `status IN ('Pending','Running')` — replay progress via item counts (limit 10)
6. `sync_node_lifecycle_history` ORDER BY `occurred_at DESC` LIMIT 20 — recent state changes

Use `Task.WhenAll` for parallelism. All queries tenant-scoped via EF global filters.

**Node lifecycle state values** (from Epic 12B.1): `Active`, `Maintenance`, `Draining`, `Decommissioning`, `Decommissioned`, `Offline`, `Failed` — map Decommissioning/Decommissioned to `Offline` bucket for display simplicity.

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

**Layout (4 panels in 2×2 grid):**

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
│  Recent Node State Changes (horizontal event strip, newest left)    │
└─────────────────────────────────────────────────────────────────────┘
```

**Query:**
```typescript
// cluster.ts
export const clusterKeys = {
  summary: ['cluster', 'summary'] as const,
};
export async function getClusterSummary(): Promise<ClusterSummaryDto> { ... }
```

**Hook:** `useClusterSummary` — `useQuery` with `staleTime: 10_000`, `refetchInterval: 15_000`. SignalR `OperationChanged` and `NodeLifecycleChanged` events call `queryClient.invalidateQueries(clusterKeys.summary)`.

**Route:** `/operations/cluster` — add to router and left-nav under Operations section after Jobs.

**Unit tests:** Mock `getClusterSummary`. Assert: node state badges render, active ops list renders, empty state ("No active operations") renders.

---

## Module 2 — Configuration Comparison

### Purpose

Side-by-side diff view of two template versions. Accessible from version history on the Templates page without a route change. Lets admins review what changed before rolling out a new version.

### Backend

**New files:**
- `src/MSOSync.Metadata/Configuration/IConfigurationComparisonService.cs`
- `src/MSOSync.Metadata/Configuration/ConfigurationComparisonService.cs`
- `src/MSOSync.Metadata/Configuration/Dtos/ConfigVersionDiffDto.cs`
- Register in `MetadataServiceExtensions.cs`

**New endpoint on existing `ConfigurationTemplateController`:**
```
GET /api/v1/configuration/templates/{id}/compare?v1={versionNumber}&v2={versionNumber}
Authorization: ViewerOrAbove + ManageConfigurations
```

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

**Service implementation:**
`ConfigurationComparisonService.CompareAsync(Guid templateId, int v1, int v2, CancellationToken ct)`:
1. Load both `SyncConfigurationTemplateVersion` rows; throw `NotFoundException` if either missing.
2. Parse both `ConfigJson` as `JsonDocument`.
3. Flatten JSON to dot-notation key-value pairs (e.g., `"database.host"` = `"server01"`). Recurse into objects; arrays treated as atomic values (serialize to compact JSON).
4. Produce `DiffEntryDto` per key:
   - In V1 but not V2 → `Removed`
   - In V2 but not V1 → `Added`
   - In both, same value → `Unchanged`
   - In both, different value → `Changed`
5. Sort: Changed first, then Added, then Removed, then Unchanged.
6. Return `HasChanges = entries.Any(e => e.ChangeType != "Unchanged")`.

**Inject into `ConfigurationTemplateController`:**
```csharp
[HttpGet("{id:guid}/compare")]
[ProducesResponseType(typeof(ConfigVersionDiffDto), 200)]
[ProducesResponseType(400)]
[ProducesResponseType(404)]
public async Task<IActionResult> Compare(
    Guid id,
    [FromQuery] int v1,
    [FromQuery] int v2,
    CancellationToken ct)
{
    await authz.EnsurePermissionAsync(SystemPermissions.ManageConfigurations, ct);
    if (v1 == v2) return BadRequest(new ProblemDetails { Title = "v1 and v2 must differ." });
    var diff = await comparisonSvc.CompareAsync(id, v1, v2, ct);
    return Ok(diff);
}
```

**Unit tests** (`MSOSync.MetadataTests`): added, removed, changed, unchanged, nested object keys, empty configs.

### Frontend

**New files:**
- `src/MSOSync.Frontend/src/features/operations/configuration/components/ConfigComparePanel.tsx`
- `src/MSOSync.Frontend/src/shared/api/configComparison.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useConfigComparison.ts`
- `src/MSOSync.Frontend/src/features/operations/configuration/components/__tests__/ConfigComparePanel.test.tsx`

**Trigger:** "Compare versions" button added to existing TemplatesPage version history drawer.

**Panel layout (slide-out drawer):**
```
┌─────────────────────────────────────────────────────────────┐
│ Compare Versions — Template: "Production DB Config"         │
│ [Version dropdown v1 ▼]  ←→  [Version dropdown v2 ▼]       │
├────────────────┬────────────────────────────────────────────┤
│ Key            │ Change  │ Old Value         │ New Value     │
│ database.host  │ Changed │ server01          │ server02      │
│ database.port  │ Added   │ —                 │ 5432          │
│ cache.ttl      │ Removed │ 300               │ —             │
│ app.name       │ ─       │ MyApp             │ MyApp         │
└────────────────┴─────────────────────────────────────────────┘
```

Color coding: Changed = yellow row, Added = green row, Removed = red row, Unchanged = default.

**Hook:** `useConfigComparison(templateId, v1, v2)` — `useQuery` enabled when both v1 and v2 are selected and differ.

**Unchanged rows hidden by default** with a "Show X unchanged" toggle.

**Tests:** Diff renders correctly, empty diff shows "No differences found", version picker updates query.

---

## Module 3 — Audit Explorer

### Purpose

Enhances the existing `/operations/activity` Audit tab with multi-value filter chips (replacing single text inputs), saved filter sets via user preferences, and a new "Entity History" tab showing all audit events for a specific entity.

### Backend

**Modified files:**
- `src/MSOSync.Metadata/Audit/AuditFilter.cs` — add multi-value fields
- `src/MSOSync.Metadata/Audit/AuditFilterValidator.cs` — extend validator
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
    public string[]? Usernames         { get; set; }
    public string[]? ActionNames       { get; set; }
    public string[]? EntityTypes       { get; set; }
    public string[]? Sources           { get; set; }
    // Existing
    public DateTime? From              { get; set; }
    public DateTime? To                { get; set; }
    public string?   Cursor            { get; set; }
    public bool      IncludeTotalCount { get; set; }
    public int       PageSize          { get; set; } = 50;
}
```

**Query logic in `AuditQueryService`:**
```
effectiveUsernames  = Usernames?.Length > 0 ? Usernames : (Username != null ? [Username] : null)
effectiveActions    = ActionNames?.Length > 0 ? ActionNames : (ActionName != null ? [ActionName] : null)
effectiveEntityTypes = EntityTypes
effectiveSources    = Sources

WHERE (effectiveUsernames is null OR username IN effectiveUsernames)
  AND (effectiveActions   is null OR action_name IN effectiveActions)
  AND (effectiveEntityTypes is null OR entity_type IN effectiveEntityTypes)
  AND (effectiveSources   is null OR source IN effectiveSources)
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

Queries: `WHERE entity_type = @entityType AND entity_id = @entityId ORDER BY occurred_at DESC`.

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

**Saved filters:** No new backend endpoint. Stored via existing `PUT /api/v1/preferences/audit.savedFilters` with value `[{ name, filter }]`. Frontend reads `GET /api/v1/preferences` → `audit.savedFilters` key.

**`AuditFilterValidator` extension:** validate `EntityTypes` array ≤ 10 items, `Usernames` array ≤ 10 items, `ActionNames` array ≤ 10 items, `Sources` array ≤ 10 items, `pageSize` 1–200.

### Frontend

**Modified files:**
- `src/MSOSync.Frontend/src/features/operations/activity/AuditPage.tsx` — add multi-select filter chips, saved filters sidebar, Entity History tab
- `src/MSOSync.Frontend/src/shared/api/audit.ts` — extend `getAudits` params + add `getEntityHistory`
- `src/MSOSync.Frontend/src/shared/hooks/useAudit.ts` — extend hook params

**New files:**
- `src/MSOSync.Frontend/src/features/operations/activity/components/AuditFilterBar.tsx` — multi-select chip bar
- `src/MSOSync.Frontend/src/features/operations/activity/components/SavedFiltersPanel.tsx` — saved filter list
- `src/MSOSync.Frontend/src/features/operations/activity/components/EntityHistoryTab.tsx` — entity type + ID pickers + grid

**Filter bar layout:**
```
[Entity Types ▼ ×Node ×Channel]  [Actions ▼ ×NODE_APPROVED]  [Usernames ▼]  [Sources ▼]
[From: ──────]  [To: ──────]  [Save Filter]  [Saved ▼]  [Clear All]
```

Each multi-select opens a `<select multiple>` dropdown populated with known values (EntityTypes and ActionNames are static enum-like lists; Usernames fetched from `GET /api/v1/users`).

**Entity History tab:**
```
Entity Type: [dropdown]  Entity ID: [text input]  [Load]
[cursor-paginated grid of audit events for that entity]
```

**Saved filter persistence:**
```typescript
// Save: PUT /api/v1/preferences/audit.savedFilters
// Value: Array<{ name: string; filter: AuditFilterParams }>
// Load: GET /api/v1/preferences → ['audit.savedFilters']
```

---

## Module 4 — Operations Timeline

### Purpose

Gantt-style view of operations by time range. Operators use it to understand operation overlap, duration, and sequencing. Click a bar to open the existing detail panel.

### Backend

**New files:**
- `src/MSOSync.Metadata/Operations/Timeline/IOperationTimelineService.cs`
- `src/MSOSync.Metadata/Operations/Timeline/OperationTimelineService.cs`
- `src/MSOSync.Metadata/Operations/Timeline/Dtos/OperationTimelineDto.cs`
- Register in `MetadataServiceExtensions.cs`

**New endpoint on existing `OperationsController`:**
```
GET /api/v1/operations/timeline?from={ISO}&to={ISO}&types[]={csv}&limit=200
Authorization: ViewerOrAbove
```

**Response DTO:**
```csharp
// src/MSOSync.Metadata/Operations/Timeline/Dtos/OperationTimelineDto.cs
namespace MSOSync.Metadata.Operations.Timeline.Dtos;

public sealed record OperationTimelineDto(
    IReadOnlyList<OperationTimelineItemDto> Items,
    DateTime                                From,
    DateTime                                To);

public sealed record OperationTimelineItemDto(
    Guid      OperationId,
    string    Type,
    string    Status,
    string?   NodeId,
    string?   Label,
    DateTime  StartedAt,
    DateTime? CompletedAt,
    int?      ProgressPercent);
```

**Service implementation:**
`OperationTimelineService.GetTimelineAsync(DateTime from, DateTime to, string[]? types, int limit, CancellationToken ct)`:
- Query `sync_operation` WHERE `started_at >= from AND started_at <= to`
- If `types` non-null: AND `operation_type IN types`
- ORDER BY `started_at ASC`
- LIMIT `limit` (max 500, default 200)
- Return `OperationTimelineItemDto` for each row
- `Label` = `ProgressMessage ?? Summary ?? Type`

**Validation:** `from` must be before `to`; max range 30 days; `limit` 1–500; `types` must be valid OperationType values.

**Inject into `OperationsController`:**
```csharp
// Constructor: add IOperationTimelineService timelineSvc

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
[From: ──] [To: ──] [Types: ▼ Export Rollout Replay ×] [Refresh]
─────────────────────────────────────────────────────────────────
Gantt chart (Recharts ComposedChart):
  Y axis: operation type groups (Export / Rollout / BatchReplay / ...)
  X axis: time (hours/days depending on range)
  Bars: each operation as a horizontal bar from StartedAt → CompletedAt
        (for in-progress ops: bar extends to "now")
  Color: by Status (Running=blue, Completed=green, Failed=red, Cancelled=grey)
  Tooltip: OperationId, NodeId, Label, Duration, Status
─────────────────────────────────────────────────────────────────
Click bar → open existing RollingOperationDetailPanel or ReplayDetailPanel
            (fallback: navigate to /operations/jobs?id={operationId})
```

**Gantt implementation approach:**
Use `recharts` `BarChart` in horizontal layout:
- `layout="vertical"` with custom `Bar` shape renderer
- Each data point: `{ y: groupLabel, x0: startMs, x1: endMs ?? nowMs, ...meta }`
- Custom `shape` prop renders a `<rect>` positioned by `x0`/`x1` within the chart domain

**Default date range:** Last 24 hours. Max selectable range: 30 days.

**Route:** `/operations/timeline` — add to router and left-nav under Operations section.

**Tests:** Timeline renders bars for mock data, empty state renders ("No operations in this range"), date range picker updates query, clicking bar opens detail panel.

---

## Service Registration

Add to `MetadataServiceExtensions.cs`:
```csharp
// Phase 2B.3 — Advanced Operations Analytics
services.AddScoped<IClusterSummaryQueryService, ClusterSummaryQueryService>();
services.AddScoped<IConfigurationComparisonService, ConfigurationComparisonService>();
services.AddScoped<IOperationTimelineService, OperationTimelineService>();
// IAuditQueryService already registered; extend impl with new methods
```

No new `IOptions<T>` needed — no configurable parameters for these modules.

---

## Testing Strategy

### Backend unit tests

| Test class | Project | Count |
|---|---|---|
| `ClusterSummaryQueryServiceTests` | `MSOSync.MetadataTests` | ~8 tests |
| `ConfigurationComparisonServiceTests` | `MSOSync.MetadataTests` | ~8 tests |
| `OperationTimelineServiceTests` | `MSOSync.MetadataTests` | ~6 tests |
| `AuditQueryServiceMultiFilterTests` | `MSOSync.MetadataTests` | ~6 tests |

All use `TestDbContext.Create()` (SQLite in-memory).

### Backend integration tests

| Test class | File |
|---|---|
| `ClusterApiTests` | `tests/MSOSync.IntegrationTests/Operations/ClusterApiTests.cs` |
| `ConfigCompareApiTests` | `tests/MSOSync.IntegrationTests/Configuration/ConfigCompareApiTests.cs` |
| `AuditExplorerApiTests` | `tests/MSOSync.IntegrationTests/Operations/AuditExplorerApiTests.cs` |
| `OperationTimelineApiTests` | `tests/MSOSync.IntegrationTests/Operations/OperationTimelineApiTests.cs` |

All use `[Collection("Lifecycle")]` + `LifecycleFixture`. Environmental failures (no SQL Server) are acceptable; build must pass.

### Frontend tests (React Testing Library + Vitest + MSW)

| Test file | Tests |
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
