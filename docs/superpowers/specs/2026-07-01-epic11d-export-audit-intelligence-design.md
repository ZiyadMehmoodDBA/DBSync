# Epic 11D: Export + Audit Intelligence — Design Spec

**Date:** 2026-07-01  
**Status:** Approved (CTO review with 7 modifications applied)

---

## Overview

Two tracks delivered in four tasks:

1. **Export** — streaming CSV/JSON download from every server-paginated table  
2. **Audit Intelligence** — summary statistics + visualizations on the audit log

**Guiding principle:** Everything an operator or administrator can reasonably manage must be configurable from the frontend. No database edits or developer intervention for normal operations.

---

## Track 1: Export

### Scope

**Server-side streaming** applies to the four tables that are server-paginated (because they can grow unboundedly):

| Table | Endpoint |
|-------|----------|
| Events | `GET /api/v1/events/export` |
| Incoming Batches | `GET /api/v1/incoming-batches/export` |
| Outgoing Batches | `GET /api/v1/batch/export` |
| Audit Log | `GET /api/v1/audit/export` |

**Client-side AG Grid export** (browser built-in) applies to the six small tables that are fully loaded into the grid:

- Nodes, Channels, Triggers, Routers, Users, Parameters

These six tables need no backend changes. The frontend ExportMenu calls AG Grid's built-in export APIs.

### Backend Architecture

#### `IExportService<TFilter>`

```csharp
public interface IExportService<TFilter>
{
    Task ExportCsvAsync(Stream output, TFilter filter, CancellationToken ct);
    Task ExportJsonAsync(Stream output, TFilter filter, CancellationToken ct);
}
```

Filter types reuse the existing read-endpoint filters:
- `EventFilter` (existing)
- `IncomingBatchFilter` (existing)
- `BatchFilter` (existing, or `OutgoingBatchFilter`)
- `AuditFilter` (existing)

Each implementation queries via `IAsyncEnumerable<T>` and streams rows to `output` without buffering the full result set. CSV uses a minimal hand-written serializer (no third-party CSV lib). JSON uses `System.Text.Json.Utf8JsonWriter`.

#### Four concrete implementations

All four live in their respective metadata/service namespaces:

- `EventExportService : IExportService<EventFilter>`
- `IncomingBatchExportService : IExportService<IncomingBatchFilter>`
- `OutgoingBatchExportService : IExportService<BatchFilter>`
- `AuditExportService : IExportService<AuditFilter>`

#### `StreamingExportResult : IActionResult`

```csharp
public sealed class StreamingExportResult : IActionResult
{
    private readonly Func<Stream, CancellationToken, Task> _writer;
    private readonly string _contentType;    // "text/csv" or "application/json"
    private readonly string _fileName;       // e.g. "events-2026-07-01.csv"

    public async Task ExecuteResultAsync(ActionContext context)
    {
        var response = context.HttpContext.Response;
        response.ContentType = _contentType;
        response.Headers["Content-Disposition"] = $"attachment; filename=\"{_fileName}\"";
        await _writer(response.Body, context.HttpContext.RequestAborted);
    }
}
```

#### Export endpoints — added to existing controllers

Each export action:
- Accepts `format=csv|json` query param (default: `csv`)
- Accepts same filter params as the corresponding list endpoint
- Requires `[Authorize(Policy = "ViewerOrAbove")]`
- Returns `StreamingExportResult`

Example (EventsController):
```csharp
[HttpGet("export")]
[Authorize(Policy = "ViewerOrAbove")]
public IActionResult ExportEvents([FromQuery] EventFilter filter, [FromQuery] string format = "csv", CancellationToken ct = default)
{
    // Wrap export service call in StreamingExportResult
    // Log export audit entry after stream completes (use response finished callback)
}
```

#### Export audit logging

Every export action logs a record to `sync_audit`. Action names use `SCREAMING_SNAKE_CASE` (matching the existing convention):

| Resource | `action_name` |
|----------|---------------|
| Events | `EXPORT_EVENTS` |
| Incoming Batches | `EXPORT_INCOMING_BATCHES` |
| Outgoing Batches | `EXPORT_OUTGOING_BATCHES` |
| Audit | `EXPORT_AUDIT` |

- `object_name`: format string, `"csv"` or `"json"` (varchar(100) fits)
- `username`: from `ICurrentUserService`
- `correlation_id`: from `HttpContext`
- `create_time`: `DateTime.UtcNow`

Filter parameters are NOT stored in `sync_audit` — `ObjectName` is `varchar(100)` (too small for JSON). The format and username are sufficient for compliance purposes.

Row count and duration are captured via a response-finished callback (no buffering needed — EF can return a count from a separate lightweight query before streaming, or it can be tracked during streaming).

#### Future scale: rows > 1M

When row count estimate exceeds 1,000,000, the endpoint returns:
```
HTTP 202 Accepted
{ "mode": "background-required", "estimatedRows": N }
```
This is a placeholder for a future background job export system. No implementation in 11D.

### Frontend Architecture

#### `useExport` hook

```typescript
function useExport(resource: ExportResource) {
  // Returns: { exportFile, isExporting, error }
  // exportFile(scope: 'view' | 'all', format: 'csv' | 'json', filters?: FilterParams): Promise<void>
}
```

Behavior on "All Matching Rows":
1. POST/GET to export endpoint with no pagination limit
2. On success: browser `<a download>` trigger
3. On failure (network error, 202, 5xx): show `ExportFailureDialog` with two actions:
   - **[Retry]** — re-attempts the same "all rows" export
   - **[Export Current View]** — falls back to AG Grid client-side export of whatever is currently loaded in the grid

No silent page-fetching fallback. The dialog is always shown on failure.

#### `ExportMenu` component

Dropdown menu with two sections:

```
Current View
  ├── Download CSV
  └── Download JSON
All Matching Rows
  ├── Download CSV
  └── Download JSON
```

"Current View" calls AG Grid's export API directly (works for both fully-loaded small tables and the current page of server-paginated tables).

"All Matching Rows" calls `useExport` → streams from backend.

#### Pages with ExportMenu added

- EventsPage
- IncomingBatchesPage
- BatchPage (outgoing)
- AuditPage (Log tab)

Small-table pages (Nodes, Channels, Triggers, Routers, Users, Parameters): ExportMenu present but "All Matching Rows" section is hidden (or disabled) — only "Current View" available since all data is already in the grid.

---

## Track 2: Audit Intelligence

### Audit Summary Endpoint

```
GET /api/v1/audit/summary?from=2026-06-24T00:00:00Z&to=2026-07-01T23:59:59Z
```

- Auth: `ViewerOrAbove`
- Dates: absolute ISO 8601 UTC (frontend computes from presets)
- Frontend date presets: Last 24 h, Last 7 days, Last 30 days, Last 90 days, Custom range

#### `AuditSummaryDto`

```csharp
public sealed record AuditSummaryDto(
    int TotalActions,
    int FailedOperations,
    int PermissionChanges,
    IReadOnlyList<DayBucket> ByDay,
    IReadOnlyList<UserBucket> ByUser,
    IReadOnlyList<EntityTypeBucket> ByEntityType,
    IReadOnlyList<ParameterBucket> TopParameters
);

public sealed record DayBucket(DateOnly Date, int Total, int Failed);
public sealed record UserBucket(string Username, int Count);
public sealed record EntityTypeBucket(string EntityType, int Count);
public sealed record ParameterBucket(string ParameterName, int Count);
```

`ByDay` contains one entry per calendar day in the `from`–`to` range (days with zero activity are included with count 0 so the chart renders a continuous x-axis).

`ByUser`: top 10 by count, descending.  
`ByEntityType`: all entity types seen in range, descending.  
`TopParameters`: top 10 parameter names by change count, descending.

#### `IAuditSummaryService` / `AuditSummaryService`

```csharp
public interface IAuditSummaryService
{
    Task<AuditSummaryDto> GetSummaryAsync(DateTime from, DateTime to, CancellationToken ct);
}
```

Implemented with grouped EF Core queries against `sync_audit`. Each group-by is a separate async query (no single mega-query). Results assembled in memory.

`FailedOperations`: count of rows where `ActionName` matches `LIKE '%_FAILURE%' OR LIKE '%_FAILED%' OR LIKE '%_ERROR%'` (covers `LOGIN_FAILURE`, `ACCOUNT_LOCKED`, etc. in the `SCREAMING_SNAKE_CASE` convention used by `AuditService.cs`).

`PermissionChanges`: count of rows where `ActionName` matches `LIKE '%_PERMISSION%' OR LIKE '%_ROLE%' OR LIKE '%_GRANT%' OR LIKE '%_REVOKE%'`.

### Frontend: AuditPage Tabs

AuditPage gains two tabs:

- **Log** — existing AG Grid table (no change to current behavior)
- **Insights** — new tab with date range picker + 8 visualizations

#### Date range picker

Preset buttons: **24h | 7d | 30d | 90d | Custom**

Custom shows two `<input type="date">` fields. All presets compute absolute `from`/`to` on the client and pass them as ISO 8601 query params.

#### 8 visualizations

| # | Type | Data source |
|---|------|-------------|
| 1 | Stat card: Total Actions | `totalActions` |
| 2 | Stat card: Failed Operations | `failedOperations` |
| 3 | Stat card: Permission Changes | `permissionChanges` |
| 4 | Line chart: Activity Timeline | `byDay[].total` |
| 5 | Line chart: Failed Actions Timeline | `byDay[].failed` |
| 6 | Horizontal bar: Top Users | `byUser[]` |
| 7 | Bar chart: Entity Types | `byEntityType[]` |
| 8 | Bar chart: Top Parameters | `topParameters[]` |

Charts 4 and 5 share x-axis (same `byDay[]` array). They are separate chart instances (not a multi-series chart) to keep the failure trend visually distinct.

All charts use `react-apexcharts` (already installed: `apexcharts ^5.15.2`, `react-apexcharts ^2.1.1`).

#### `getAuditSummary` API function

```typescript
async function getAuditSummary(from: string, to: string): Promise<AuditSummaryDto>
```

Exposed via TanStack Query with query key `['audit', 'summary', from, to]`.

---

## Data Flow Summary

```
User clicks Export (All Rows)
  → useExport hook
  → GET /api/v1/{resource}/export?format=csv&[filters]
  → Controller → IExportService<TFilter>
  → IAsyncEnumerable<T> from DB
  → StreamingExportResult writes to Response.Body
  → Browser receives stream → triggers download
  → On failure → ExportFailureDialog

User opens Insights tab
  → Date preset selected → from/to computed client-side
  → GET /api/v1/audit/summary?from=&to=
  → IAuditSummaryService → grouped EF queries
  → AuditSummaryDto → TanStack Query cache
  → 3 stat cards + 5 ApexCharts render
```

---

## Task Breakdown

### Task 1: Backend export streaming
**Files:**
- Create: `src/MSOSync.Metadata/Export/IExportService.cs`
- Create: `src/MSOSync.Metadata/Export/StreamingExportResult.cs`
- Create: `src/MSOSync.Metadata/Export/EventExportService.cs`
- Create: `src/MSOSync.Metadata/Export/IncomingBatchExportService.cs`
- Create: `src/MSOSync.Metadata/Export/OutgoingBatchExportService.cs`
- Create: `src/MSOSync.Metadata/Export/AuditExportService.cs`
- Modify: `src/MSOSync.Api/Controllers/EventsController.cs` (add export action)
- Modify: `src/MSOSync.Api/Controllers/IncomingBatchesController.cs` (add export action)
- Modify: `src/MSOSync.Api/Controllers/BatchController.cs` (add export action)
- Modify: `src/MSOSync.Api/Controllers/AuditController.cs` (add export action)
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs` (register export services)
- Create: `tests/MSOSync.MetadataTests/Export/EventExportServiceTests.cs`
- Create: `tests/MSOSync.MetadataTests/Export/AuditExportServiceTests.cs`

### Task 2: Audit summary backend
**Files:**
- Create: `src/MSOSync.Metadata/Audit/IAuditSummaryService.cs`
- Create: `src/MSOSync.Metadata/Audit/AuditSummaryService.cs`
- Create: `src/MSOSync.Metadata/Audit/AuditSummaryDto.cs`
- Modify: `src/MSOSync.Api/Controllers/AuditController.cs` (add summary action)
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs` (register summary service)
- Create: `tests/MSOSync.MetadataTests/Audit/AuditSummaryServiceTests.cs`

### Task 3: Frontend export UX
**Files:**
- Create: `src/MSOSync.Frontend/src/shared/hooks/useExport.ts`
- Create: `src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx`
- Create: `src/MSOSync.Frontend/src/shared/components/ExportFailureDialog.tsx`
- Modify: `src/MSOSync.Frontend/src/features/events/EventsPage.tsx`
- Modify: `src/MSOSync.Frontend/src/features/batches/IncomingBatchesPage.tsx`
- Modify: `src/MSOSync.Frontend/src/features/batches/BatchPage.tsx`
- Modify: `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx` (export only — tabs in Task 4)
- Modify: `src/MSOSync.Frontend/src/shared/api/` (add export API functions)
- Modify: `src/MSOSync.Frontend/src/shared/queryKeys.ts` (if needed)

### Task 4: Audit visualizations
**Files:**
- Create: `src/MSOSync.Frontend/src/shared/types/audit-summary.ts`
- Modify: `src/MSOSync.Frontend/src/shared/api/audit.ts` (add getAuditSummary)
- Create: `src/MSOSync.Frontend/src/features/audit/AuditInsightsTab.tsx`
- Modify: `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx` (add tabs, wire Insights)

---

## Global Constraints

- C# 13 / .NET 9, `TreatWarningsAsErrors = true`
- EF Core 9.0.0; query against existing `sync_audit` table (no schema changes)
- No new NuGet packages — use `System.Text.Json` for JSON streaming, hand-rolled CSV (no CsvHelper)
- React 19, TypeScript `erasableSyntaxOnly = true` (no enums — use `as const` objects or string unions)
- No new npm packages — use `apexcharts ^5.15.2` + `react-apexcharts ^2.1.1` (already installed)
- AG Grid already installed — use its export API for client-side export
- TanStack Query v5 — query keys follow existing `queryKeys.ts` pattern
- All frontend imports relative (no `@/` aliases)
- `ViewerOrAbove` auth policy on all export and summary endpoints
- No buffering of full result sets in export services — `IAsyncEnumerable<T>` throughout
- Export audit logging on every export action (entity, format, filter snapshot, user, timestamp)
- No UI changes to the existing Audit Log tab — additive only (new Insights tab alongside)
- `DayBucket` must include zero-count days to ensure continuous chart x-axis

---

## Out of Scope (Future Epics)

- Background job export for >1M rows (reserved: `HTTP 202 + { mode: "background-required", estimatedRows: N }`)
- Export scheduling / email delivery
- Saved filter presets
- Fine-grained RBAC beyond `ViewerOrAbove`
- Real-time audit feed via SignalR
