# Epic 11G: Performance & Scale — Design Spec

**Date:** 2026-07-02  
**Status:** Approved

---

## Goal

Modernize MSOSync's data access and export patterns before scale demands it. Two sequential tracks: (1) cursor pagination + query cancellation across high-volume streams; (2) persistent background export jobs with a Download Center.

React Flow optimization and server-driven virtualization are explicitly deferred to Epic 11H, pending evidence from real deployments (>200 nodes / >500 edges / measurable layout latency).

---

## Architecture

```
Epic 11G
├── Track 1: Pagination + Cancellation
│   ├── Cursor pagination — Events, IncomingBatches, OutgoingBatches, Audit
│   ├── Offset pagination (bounded) — Nodes (first-time), metadata tables
│   ├── Load More UX — useInfiniteQuery
│   └── Query cancellation — axios signal wired everywhere
│
└── Track 2: Background Export Jobs
    ├── sync_export_job table (M019)
    ├── ExportJobWorker (5s poll, one job at a time)
    ├── ExportCleanupWorker (hourly soft-delete + file removal)
    ├── ExportJobController
    ├── SignalR progress patches
    └── Downloads page (sidebar, EXPORT_DATA permission)
```

---

## Global Constraints

- C# 13, .NET 9, `TreatWarningsAsErrors = true` — zero warnings
- EF Core 9 — `AsNoTracking()` on all reads; `SaveChangesAsync(ct)` on writes
- No new NuGet packages; no new npm packages
- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use `as const`
- All frontend imports relative — no `@/` aliases
- Build env: `$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"` and `$env:PATH = "C:\Users\zmehmood\.dotnet;$env:PATH"`

---

## Track 1: Cursor Pagination + Query Cancellation

### Scope

| Endpoint family | Pagination change |
|---|---|
| Events | Offset → cursor |
| IncomingBatches | Offset → cursor |
| OutgoingBatches | Offset → cursor |
| Audit | Offset → cursor |
| Nodes | None → offset (first-time, max 200) |
| Users, Parameters, Channels, Routers, Triggers | Keep offset, enforce max pageSize |

### Shared Types (`MSOSync.Common`)

```csharp
// CursorPageResult<T> — returned by all cursor-paginated endpoints
public sealed record CursorPageResult<T>(
    IReadOnlyList<T> Items,
    string? NextCursor,   // null when HasMore = false
    bool HasMore,
    int? TotalCount       // null unless ?includeTotalCount=true
);

// CursorToken — opaque base64-encoded versioned cursor
// Format on wire: base64("{id}:{createdAtTicks}")  — v1
public static class CursorToken
{
    public static string Encode(long id, long ticks);
    public static (long Id, long Ticks) Decode(string token);
    // throws ArgumentException on malformed input → 400 via GlobalExceptionHandler
}
```

### Cursor Mechanism

All four stream endpoints sort newest-first on their integer PK (auto-increment). The EF Core query for the second page onward:

```csharp
query = query
    .Where(e => e.Id < decodedId)
    .OrderByDescending(e => e.Id)
    .Take(pageSize + 1);   // fetch one extra to determine HasMore
```

`HasMore = items.Count > pageSize`. NextCursor is encoded from the last item in the trimmed list. No `COUNT(*)` unless `?includeTotalCount=true` is present.

The `Ticks` field in the cursor is reserved for future composite ordering (e.g., timestamp tie-breaking) without a breaking API change.

### API Contract Changes

```
GET /api/v1/events
  Before: ?page=2&pageSize=50
  After:  ?cursor=<token>&pageSize=100&includeTotalCount=false

Response before: { items, totalCount, page, pageSize }
Response after:  { items, nextCursor, hasMore, totalCount }
```

Same pattern for `/api/v1/incoming-batches`, `/api/v1/outgoing-batches`, `/api/v1/audit`.

Existing `page` parameter is dropped — coordinated breaking change with frontend in the same epic.

### Nodes Pagination (first-time)

`GET /api/v1/nodes?pageNumber=1&pageSize=50`  
- Default: `pageSize=50`, max `pageSize=200`, enforced by `GetNodesRequestValidator`
- Offset-based (Skip/Take) — management dataset, not a stream
- Response: `PagedResult<NodeDto>` (existing type, already used elsewhere)

### Query Cancellation

All `apiClient` functions in the frontend accept and forward `signal`:

```typescript
// Before
export async function getEvents(filter: EventFilter): Promise<CursorPageResult<EventDto>>

// After
export async function getEvents(
  filter: EventFilter,
  options: { signal?: AbortSignal }
): Promise<CursorPageResult<EventDto>> {
  return apiClient.get('/events', { params: filter, signal: options.signal });
}
```

TanStack Query passes `signal` automatically via `queryFn({ signal })`. No manual AbortController management in components.

### Frontend Load More UX

Replace `useQuery` with `useInfiniteQuery` for the four stream pages:

```typescript
useInfiniteQuery({
  queryKey: queryKeys.events.infinite(filter),
  queryFn: ({ pageParam, signal }) =>
    api.getEvents({ ...filter, cursor: pageParam }, { signal }),
  getNextPageParam: (lastPage) =>
    lastPage.hasMore ? lastPage.nextCursor : undefined,
  initialPageParam: undefined,
})
```

Grid footer when `hasMore`:
```
Showing 500 results       [Load 100 More]
```

- "Load 100 More" button disabled + spinner while `isFetchingNextPage`
- Button absent when `!hasMore`
- Counter shows total items accumulated across all loaded pages
- `queryKeys` updated: `events.list(filter)` stays for existing uses; `events.infinite(filter)` is new

---

## Track 2: Background Export Jobs

### Database Schema (`sync_export_job`)

M019 migration adds the table:

```sql
CREATE TABLE sync_export_job (
  job_id            UNIQUEIDENTIFIER  NOT NULL DEFAULT NEWID() PRIMARY KEY,
  parent_job_id     UNIQUEIDENTIFIER  NULL,       -- set on retry; references job_id
  requested_by      NVARCHAR(256)     NOT NULL,
  resource_type     NVARCHAR(50)      NOT NULL,   -- 'events' | 'batches' | 'audit' | ...
  format            NVARCHAR(10)      NOT NULL,   -- 'csv' | 'json'
  filters_json      NVARCHAR(MAX)     NOT NULL,
  status            NVARCHAR(20)      NOT NULL,   -- see Status values below
  progress_percent  INT               NOT NULL DEFAULT 0,
  row_count         BIGINT            NULL,
  output_path       NVARCHAR(500)     NULL,
  error_message     NVARCHAR(1000)    NULL,
  expires_at        DATETIMEOFFSET    NULL,
  created_at        DATETIMEOFFSET    NOT NULL DEFAULT SYSUTCDATETIME(),
  started_at        DATETIMEOFFSET    NULL,
  completed_at      DATETIMEOFFSET    NULL
);

CREATE INDEX IX_export_job_status_created ON sync_export_job (status, created_at);
CREATE INDEX IX_export_job_requested_by   ON sync_export_job (requested_by, created_at DESC);
```

### Status Values

```
Pending   — queued, not yet picked up
Running   — worker executing
Completed — file ready for download
Failed    — worker encountered an error
Deleted   — soft-deleted by user; file removed, row retained
Expired   — past ExpiresAt; file removed by cleanup worker
```

No hard deletes. `ExportCleanupWorker` transitions `Completed`/`Failed` → `Expired` after `RetentionHours`. A future purge job can remove rows older than N days without a new migration.

### Configuration

`appsettings.json`:
```json
"Export": {
  "ImmediateThreshold": 50000,
  "BasePath": "exports",
  "RetentionHours": 24,
  "MaxConcurrentJobs": 1
}
```

Bound to `ExportOptions` POCO via `services.Configure<ExportOptions>(config.GetSection("Export"))`.

### `IExportJobService` / `ExportJobService`

Located in `MSOSync.App`:

```csharp
public interface IExportJobService
{
    Task<SyncExportJob> CreateJobAsync(string requestedBy, string resourceType,
        string format, string filtersJson, CancellationToken ct);
    Task<SyncExportJob?> GetJobAsync(Guid jobId, CancellationToken ct);
    Task<IReadOnlyList<SyncExportJob>> GetJobsForUserAsync(string username,
        CancellationToken ct);
    Task<IReadOnlyList<SyncExportJob>> GetAllJobsAsync(CancellationToken ct);
    Task UpdateProgressAsync(Guid jobId, int progressPercent, CancellationToken ct);
    Task CompleteJobAsync(Guid jobId, string outputPath, long rowCount, CancellationToken ct);
    Task FailJobAsync(Guid jobId, string errorMessage, CancellationToken ct);
    Task SoftDeleteJobAsync(Guid jobId, CancellationToken ct);
    // Atomic claim — single UPDATE...OUTPUT statement; safe for future multi-worker
    Task<SyncExportJob?> ClaimNextPendingJobAsync(CancellationToken ct);
}
```

`ClaimNextPendingJobAsync` must execute atomically — no read-then-update. Implementation uses a raw SQL `UPDATE TOP(1) ... SET status='Running', started_at=SYSUTCDATETIME() OUTPUT inserted.* WHERE status='Pending' ORDER BY created_at` so future multiple workers never double-claim the same job.

### `ExportJobWorker : BackgroundService`

`MSOSync.App.Workers.ExportJobWorker`. Polling interval: 5 seconds.

```
Loop:
  1. ClaimNextPendingJobAsync — SET status = Running, started_at = now (one UPDATE)
  2. If null → sleep 5s, continue
  3. Deserialize filters_json → filter object for resource_type
  4. Open file at {BasePath}/{JobId}.{format}
  5. Stream rows from query service (same services as existing export)
     Every 1,000 rows: UpdateProgressAsync
  6. On success: CompleteJobAsync → set status, output_path, row_count, expires_at = now + RetentionHours
              → publish ExportJobChangedNotification via MediatR
  7. On exception: FailJobAsync → set status, error_message
              → publish ExportJobChangedNotification
  8. Sleep 5s
```

`MaxConcurrentJobs = 1` enforced by single worker. No Semaphore needed. Future: spawn N workers from config.

### `ExportCleanupWorker : BackgroundService`

`MSOSync.App.Workers.ExportCleanupWorker`. Runs every 60 minutes.

```
Loop:
  1. SELECT jobs WHERE expires_at <= now AND status IN (Completed, Failed)
  2. For each: delete file if exists, SET status = Expired
  3. SELECT jobs WHERE status = Deleted AND completed_at < now - 7 days
     (optional future purge — skip in 11G, just soft-delete)
  4. Sleep 60 minutes
```

Runs independently of `ExportJobWorker`. Failure in cleanup never blocks export execution.

### SignalR: `ExportJobChangedNotification`

`ExportJobChangedPublisher : INotificationHandler<ExportJobChangedNotification>` sends only to the job owner — not broadcast to all users:

```
hub.Clients.User(job.RequestedBy).SendAsync("ExportJobEvent", {
  jobId, status, progressPercent, rowCount
})
```

Other operators must not see each other's export progress. `Clients.User(username)` maps to the authenticated user's connection(s) via the hub's user ID provider (ASP.NET Core Identity integration already wired in `OperationsHub`).

Frontend strategy:
- **Running** (progress update): `queryClient.setQueryData(queryKeys.exportJobs, ...)` — patch in-place for smooth progress bars, no refetch
- **Completed / Failed / Deleted / Expired**: `queryClient.invalidateQueries(queryKeys.exportJobs)` — full refresh to get final state

### `ExportJobController`

```
[Authorize(Policy = "ViewerOrAbove")]
POST   /api/v1/export-jobs
  Body: { resourceType, format, filtersJson }
  Returns: 202 { jobId }

GET    /api/v1/export-jobs
  Returns caller's jobs (newest first)
  Admin with ?all=true returns all users' jobs

GET    /api/v1/export-jobs/{id}/download
  Authorization: job.RequestedBy == currentUser OR MANAGE_USERS permission
  Returns: file stream (Content-Disposition: attachment)
  404 if status is Expired, Deleted, or file missing

DELETE /api/v1/export-jobs/{id}
  Authorization: same as download
  Soft-deletes: status → Deleted, file removed, row retained
```

### Retry Semantics

Retry creates a new job:

```
POST /api/v1/export-jobs
Body: { resourceType, format, filtersJson, parentJobId: "<failedJobId>" }
```

The backend sets `ParentJobId` on the new record. Failed job remains immutable. UI shows "Retry" as a new row with linkage visible in audit trail.

### Frontend — Downloads Page

New top-level sidebar entry:

```
Sidebar
├── Dashboard
├── Events
├── Batches (Incoming / Outgoing)
├── Audit
├── Downloads      ← NEW (requires EXPORT_DATA permission, hidden if lacking)
└── Administration
    ├── Roles
    └── Users
```

`src/MSOSync.Frontend/src/features/downloads/DownloadsPage.tsx`:

| Column | Notes |
|---|---|
| Resource | "Events", "Batches", etc. |
| Format | CSV / JSON badge |
| Status | Color-coded badge: gray/blue/green/red/slate |
| Progress | Bar (animated for Running, 0-100%) |
| Rows | Numeric, null until Completed |
| Created | Relative timestamp |
| Completed | Relative timestamp or — |
| Actions | Download (Completed), Retry (Failed), Delete (Completed/Failed) |

SignalR `ExportJobEvent` patches progress in real time via `setQueryData`.

### ExportMenu changes

"All Matching Rows" items no longer trigger streaming. They call `createExportJob`:

```
Export ▼
├── Current View (CSV)       — client-side, unchanged
├── Current View (JSON)      — client-side, unchanged
├── All Matching (CSV)       → POST /api/v1/export-jobs
└── All Matching (JSON)      → POST /api/v1/export-jobs
```

On job creation: Sonner toast — "Export queued. View progress in Downloads." with link to `/downloads`.

No row-count estimation on the frontend. The threshold check (`ImmediateThreshold`) lives in the backend for future use (e.g., return `{ mode: "stream" }` for small datasets) but is not implemented in 11G — all "All Matching" exports go through jobs.

---

## File Structure

### New files — backend

```
src/MSOSync.Common/Pagination/CursorPageResult.cs
src/MSOSync.Common/Pagination/CursorToken.cs
src/MSOSync.Persistence/Entities/SyncExportJob.cs
src/MSOSync.Persistence/Configurations/SyncExportJobConfiguration.cs
src/MSOSync.Persistence/Migrations/                      — M019_ExportJobs via dotnet ef
src/MSOSync.App/Export/IExportJobService.cs
src/MSOSync.App/Export/ExportJobService.cs
src/MSOSync.App/Export/ExportOptions.cs
src/MSOSync.App/Export/ExportJobChangedNotification.cs
src/MSOSync.App/Workers/ExportJobWorker.cs
src/MSOSync.App/Workers/ExportCleanupWorker.cs
src/MSOSync.App/SignalR/ExportJobChangedPublisher.cs
src/MSOSync.Api/Controllers/ExportJobController.cs
tests/MSOSync.MetadataTests/Pagination/CursorTokenTests.cs
tests/MSOSync.IntegrationTests/Export/ExportJobIntegrationTests.cs
```

### Modified files — backend

```
src/MSOSync.Common/Pagination/PagedResult.cs             — keep (used by Nodes/Users/etc.)
src/MSOSync.Persistence/AppDbContext.cs                  — add DbSet<SyncExportJob>
src/MSOSync.Metadata/Events/EventQueryService.cs         — add cursor support
src/MSOSync.Metadata/IncomingBatches/IncomingBatchQueryService.cs
src/MSOSync.Metadata/OutgoingBatches/OutgoingBatchQueryService.cs
src/MSOSync.Metadata/Audit/AuditQueryService.cs
src/MSOSync.Metadata/Nodes/NodeQueryService.cs           — add offset pagination
src/MSOSync.Api/Controllers/EventsController.cs          — cursor params
src/MSOSync.Api/Controllers/IncomingBatchesController.cs
src/MSOSync.Api/Controllers/OutgoingBatchesController.cs
src/MSOSync.Api/Controllers/AuditController.cs
src/MSOSync.Api/Controllers/NodesController.cs           — add pagination
src/MSOSync.App/Program.cs                              — register workers + ExportJobService + ExportOptions
```

### New files — frontend

```
src/MSOSync.Frontend/src/shared/types/export.ts
src/MSOSync.Frontend/src/shared/api/exportJobs.ts
src/MSOSync.Frontend/src/shared/hooks/useExportJobs.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteEvents.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteIncomingBatches.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteOutgoingBatches.ts
src/MSOSync.Frontend/src/shared/hooks/useInfiniteAudit.ts
src/MSOSync.Frontend/src/features/downloads/DownloadsPage.tsx
```

### Modified files — frontend

```
src/MSOSync.Frontend/src/shared/api/events.ts
src/MSOSync.Frontend/src/shared/api/batches.ts
src/MSOSync.Frontend/src/shared/api/audit.ts
src/MSOSync.Frontend/src/shared/api/nodes.ts
src/MSOSync.Frontend/src/shared/queryKeys.ts
src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts
src/MSOSync.Frontend/src/shared/signalr/types.ts
src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx
src/MSOSync.Frontend/src/features/events/EventsPage.tsx
src/MSOSync.Frontend/src/features/events/EventsGrid.tsx
src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx
src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesGrid.tsx
src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx
src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesGrid.tsx
src/MSOSync.Frontend/src/features/audit/AuditPage.tsx
src/MSOSync.Frontend/src/features/audit/AuditGrid.tsx
src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx
src/MSOSync.Frontend/src/app/router.tsx
src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
```

---

## Task Breakdown

| # | Name | Deliverable |
|---|---|---|
| 1 | Cursor pagination — backend | `CursorPageResult<T>`, `CursorToken`, 4 query services updated, 4 controllers updated, `NodesController` bounded, unit tests |
| 2 | Cursor pagination — frontend | 4 `useInfinite*` hooks, Load More grids, query cancellation wired, `queryKeys` updated |
| 3 | Export job backend | M019 migration, `SyncExportJob`, `IExportJobService`, `ExportJobWorker`, `ExportCleanupWorker`, `ExportJobController`, SignalR publisher |
| 4 | Downloads frontend | `ExportJobsPage`, `ExportMenu` changes, SignalR patch, sidebar wiring |

Tasks 1 and 2 are sequential (backend before frontend). Tasks 3 and 4 are sequential. Tasks 1-2 and 3-4 tracks can be developed in order: finish Track 1 completely before starting Track 2.

---

## Operational Metrics

Export workers emit counters via existing `IMetricsService` (or `System.Diagnostics.Metrics` if IMetricsService not yet abstracted):

```
msosync_export_jobs_created_total      — incremented in CreateJobAsync
msosync_export_jobs_completed_total    — incremented in CompleteJobAsync
msosync_export_jobs_failed_total       — incremented in FailJobAsync
msosync_export_job_duration_seconds    — histogram recorded in ExportJobWorker (started_at → completed_at)
msosync_export_rows_written_total      — incremented by row_count in CompleteJobAsync
```

These fit the observability pattern established in earlier epics and allow early diagnosis of throughput or failure-rate issues without waiting for user reports.

---

## Out of Scope (11H+)

- React Flow optimization (>200 nodes threshold not yet observed)
- Server-driven virtualization (AG Grid DOM virtualization already sufficient)
- Topology subgraphs
- Scheduled exports
- Multi-instance shared storage for export files
- Configurable concurrency (`MaxConcurrentJobs > 1`)
