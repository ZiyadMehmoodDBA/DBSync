# Phase 2B.4 — Cluster Health Monitoring, Disaster Recovery Dashboard, Cluster Diagnostics

## Overview

Three operator-facing analytics modules extending the `ClusterController` introduced in Phase 2B.3. All modules build on existing EF entities — no new migrations. Delivered as a single release (same pattern as 2B.3).

**Modules:**
1. **Cluster Health Monitoring** — time-series connectivity trends (bucket aggregation over `SyncNodeConnectivityHistory`)
2. **Disaster Recovery Dashboard** — live view of nodes in Recovery state with RTO tracking
3. **Cluster Diagnostics** — runtime stats, active locks, slow operations

**Last migration:** M034. No new migrations in this phase.

---

## Architecture

All 3 modules add endpoints to the existing `ClusterController` (`src/MSOSync.Api/Controllers/ClusterController.cs`).

### New Services

| Service | Interface | Project | Endpoint |
|---|---|---|---|
| `ClusterHealthTrendService` | `IClusterHealthTrendService` | `MSOSync.Metadata` | `GET /api/v1/cluster/health-trends` |
| `RecoveryDashboardQueryService` | `IRecoveryDashboardQueryService` | `MSOSync.Metadata` | `GET /api/v1/cluster/recovery` |
| `ClusterDiagnosticsQueryService` | `IClusterDiagnosticsQueryService` | `MSOSync.Metadata` | `GET /api/v1/cluster/diagnostics` |

All services: `AsNoTracking()`, project directly to DTO, no lazy loading, no `Include()` unless required.

### Existing entities used (no modifications)

- `SyncNodeConnectivityHistory` — connectivity snapshots with probe results; source for health trend buckets
- `SyncNodeLifecycleHistory` — lifecycle state transitions; recovery start/end detection
- `SyncOperation` — replay operations linked to nodes during recovery
- `SyncRuntimeStats` — heap/CPU/GC snapshots; may be empty
- `DistributedLock` (or equivalent lock table) — active distributed locks
- `WorkerStatusRegistry` — not EF entity; injected as `IWorkerStatusRegistry` singleton for worker health

---

## Module 1 — Cluster Health Monitoring

### Endpoint

```
GET /api/v1/cluster/health-trends?window=6h&nodeId=
```

**Parameters:**
- `window`: `1h` | `6h` | `24h` | `7d` (default `6h`); any other value → 400
- `nodeId`: optional string filter

**Authorization:** `ViewCluster` permission (same as existing cluster summary endpoint).

### Service Interface

```csharp
public interface IClusterHealthTrendService
{
    Task<ClusterHealthTrendDto> GetTrendsAsync(string window, string? nodeId, CancellationToken ct);
}
```

### DTOs

```csharp
public record ClusterHealthTrendDto(
    string Window,
    int BucketCount,
    IReadOnlyList<HealthBucketDto> Buckets,
    IReadOnlyList<NodeProbeStatsDto> NodeProbeStats);

public record HealthBucketDto(
    DateTime BucketStart,
    int ReachableCount,
    int DegradedCount,
    int UnreachableCount,
    int TotalNodes,
    int TransitionCount);

public record NodeProbeStatsDto(
    string NodeId,
    string ConnectivityStatus,
    int? LastProbeLatencyMs,
    int ConsecutiveProbeFailures,
    double UptimePct);
```

### Bucket Granularity

| Window | Bucket size | BucketCount |
|---|---|---|
| `1h` | 5 min | 12 |
| `6h` | 30 min | 12 |
| `24h` | 2 h | 12 |
| `7d` | 12 h | 14 |

Implementation: compute `BucketStart` values from `DateTime.UtcNow - window`, group `SyncNodeConnectivityHistory` rows by truncating `RecordedAt` to bucket boundary, aggregate counts per bucket.

`UptimePct` = (rows where `ConnectivityStatus == "Reachable"`) / (total rows for that node in window) × 100. If no rows: `100.0`.

`ConsecutiveProbeFailures`: count most-recent consecutive rows where `ConnectivityStatus != "Reachable"`, per node, within the window.

### Request DTO + Validator

```csharp
public sealed record GetHealthTrendsRequest(string Window = "6h", string? NodeId = null);
```

Validator: `Window` must be one of `1h`, `6h`, `24h`, `7d`.

---

## Module 2 — Disaster Recovery Dashboard

### Endpoint

```
GET /api/v1/cluster/recovery
```

No query parameters. Returns current recovery state snapshot.

**Authorization:** `ViewCluster` permission.

### Service Interface

```csharp
public interface IRecoveryDashboardQueryService
{
    Task<RecoveryDashboardDto> GetRecoveryDashboardAsync(CancellationToken ct);
}
```

### DTOs

```csharp
public record RecoveryDashboardDto(
    RecoverySummaryDto Summary,
    IReadOnlyList<ActiveRecoveryDto> ActiveRecoveries,
    IReadOnlyList<CompletedRecoveryDto> RecentCompletedRecoveries);

public record RecoverySummaryDto(
    int ActiveCount,
    double? AvgRtoMinutes,
    double? MaxRtoMinutes,
    int CompletedLast30Days);

public record ActiveRecoveryDto(
    string NodeId,
    DateTime? FailureDetectedAt,
    DateTime RecoveryStartedAt,
    double ElapsedMinutes,
    IReadOnlyList<ReplayOpRefDto> AssociatedReplayOps);

public record CompletedRecoveryDto(
    string NodeId,
    DateTime? FailureDetectedAt,
    DateTime RecoveryStartedAt,
    DateTime RestoredAt,
    double RtoMinutes);

public record ReplayOpRefDto(
    Guid OperationId,
    string Status,
    int ItemsDone,
    int ItemsTotal);
```

### Query Logic

**Active recoveries:** nodes currently in `Recovery` lifecycle state. Join `SyncNodeLifecycleHistory` to find when each node entered `Recovery` state (`RecoveryStartedAt`). `FailureDetectedAt` = most recent transition *to* a failure state (`Offline`, `Unreachable`, `Degraded`) before `RecoveryStartedAt` in the same node's history (nullable — may not exist). `ElapsedMinutes` = `(UtcNow - RecoveryStartedAt).TotalMinutes`. `AssociatedReplayOps` = `SyncOperation` rows of type `Replay` linked to the node created after `RecoveryStartedAt`.

**Completed recoveries (last 30 days):** nodes that transitioned *out of* `Recovery` to `Active` within last 30 days. `RestoredAt` = timestamp of that transition. `RtoMinutes` = `(RestoredAt - RecoveryStartedAt).TotalMinutes`. Return up to 50, ordered by `RestoredAt DESC`.

**Summary:** `ActiveCount` = `ActiveRecoveries.Count`. `AvgRtoMinutes` / `MaxRtoMinutes` = computed from `RecentCompletedRecoveries` (null if none). `CompletedLast30Days` = count of completed recoveries in last 30 days.

---

## Module 3 — Cluster Diagnostics

### Endpoint

```
GET /api/v1/cluster/diagnostics
```

No query parameters.

**Authorization:** `ViewCluster` permission.

### Service Interface

```csharp
public interface IClusterDiagnosticsQueryService
{
    Task<ClusterDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct);
}
```

### DTOs

```csharp
public record ClusterDiagnosticsDto(
    IReadOnlyList<RuntimeStatsDto> RuntimeStats,
    IReadOnlyList<ActiveLockDto> ActiveLocks,
    IReadOnlyList<SlowOperationDto> SlowOperations);

public record RuntimeStatsDto(
    long StatId,
    double? HeapUsedMb,
    double? HeapMaxMb,
    double? CpuPercent,
    int? ThreadCount,
    long? GcCount,
    double? UptimeHours,
    DateTime CapturedAt);

public record ActiveLockDto(
    string LockName,
    string LockOwner,
    double AgeSeconds,
    bool IsStale);

public record SlowOperationDto(
    Guid OperationId,
    string OperationType,
    string Status,
    double DurationMinutes,
    int? ProgressPercent);
```

### Query Logic

**RuntimeStats:** `SyncRuntimeStats` ORDER BY `CreateTime DESC` TAKE 50. Empty list if table has no rows (not an error).

**ActiveLocks:** all rows in the distributed lock table where lock is currently held. `AgeSeconds` = `(UtcNow - AcquiredAt).TotalSeconds`. `IsStale` = `AgeSeconds > 300`.

**SlowOperations:** `SyncOperation` rows where `Status IN ('Running', 'Pending')` ORDER BY `StartedAt ASC` TAKE 20. `DurationMinutes` = `(UtcNow - StartedAt).TotalMinutes`. `ProgressPercent` nullable (null if operation type has no progress tracking).

---

## Frontend

### Pages

**`HealthTrendsPage.tsx`** (`src/MSOSync.Frontend/src/features/cluster/HealthTrendsPage.tsx`)
- Window selector: `1h | 6h | 24h | 7d` buttons (default `6h`), optional node filter `<select>` populated from `NodeProbeStats`
- Top panel: Recharts `AreaChart` — stacked areas for `ReachableCount` / `DegradedCount` / `UnreachableCount` over `BucketStart`; `TransitionCount` as secondary `Line`
- Bottom panel: per-node probe stats table — NodeId, ConnectivityStatus (colored badge), LastProbeLatencyMs, ConsecutiveProbeFailures, UptimePct
- TanStack Query: `useHealthTrends(window, nodeId)` — no polling; refetch on SignalR `node-connectivity-changed`

**`RecoveryDashboardPage.tsx`** (`src/MSOSync.Frontend/src/features/cluster/RecoveryDashboardPage.tsx`)
- 4 summary cards: Active Recoveries, Avg RTO (min), Max RTO (min), Completed Last 30d
- Active recoveries table: NodeId, Failure Detected, Recovery Started, Elapsed (minutes), replay op chips (Status + progress)
- Completed recoveries table: NodeId, Failure→Recovery→Restored timestamps, RTO column; sortable by RTO DESC
- TanStack Query: `useRecoveryDashboard()` — 30s polling

**`DiagnosticsPage.tsx`** (`src/MSOSync.Frontend/src/features/cluster/DiagnosticsPage.tsx`)
- Three collapsible panels:
  1. **Runtime Stats** — summary card (latest entry heap/CPU) + expandable table of last 50
  2. **Active Locks** — table: LockName, LockOwner, Age (seconds), Stale badge; stale rows highlighted red
  3. **Slow Operations** — table: OperationType, Status, Duration (min), Progress % bar, OperationId
- TanStack Query: `useClusterDiagnostics()` — 15s polling

### Navigation

`AppLayout.tsx`: three new entries alongside existing cluster nav — `Activity` icon (HealthTrends), `ShieldAlert` icon (RecoveryDashboard), `Stethoscope` icon (Diagnostics) from lucide-react.

### API Hooks + Query Keys

```typescript
// src/MSOSync.Frontend/src/features/cluster/api.ts (extend existing)
['cluster', 'health-trends', window, nodeId]
['cluster', 'recovery']
['cluster', 'diagnostics']
```

### SignalR Invalidation (`eventRouter.ts`)

- `node-connectivity-changed` → invalidate `['cluster', 'health-trends']`
- `node-state-changed` → invalidate `['cluster', 'recovery']`
- Diagnostics: polling only (no event-driven invalidation)

---

## Testing

### Unit Tests (`MSOSync.MetadataTests`)

**`ClusterHealthTrendServiceTests`** (~8 tests):
- Bucket granularity correct per window (5min/30min/2h/12h)
- Empty `SyncNodeConnectivityHistory` → empty `Buckets`, `NodeProbeStats` empty list
- `UptimePct` computed correctly (all reachable → 100.0, half reachable → 50.0)
- `ConsecutiveProbeFailures` counted from most recent backwards
- `nodeId` filter scopes results to single node

**`RecoveryDashboardQueryServiceTests`** (~7 tests):
- No active recovery nodes → `ActiveRecoveries` empty, `ActiveCount` 0
- `ElapsedMinutes` computed from `RecoveryStartedAt`
- `RtoMinutes` = `RestoredAt - RecoveryStartedAt`
- `AvgRtoMinutes` / `MaxRtoMinutes` null when zero completed recoveries
- Replay ops joined by NodeId + after `RecoveryStartedAt`
- `CompletedLast30Days` excludes completions older than 30 days

**`ClusterDiagnosticsQueryServiceTests`** (~6 tests):
- Empty `SyncRuntimeStats` → empty list (not error)
- `IsStale` true when `AgeSeconds > 300`
- Slow ops filtered to Running/Pending only
- RuntimeStats TOP 50 applied (ordered by CreateTime DESC)
- SlowOperations TOP 20 applied (ordered by StartedAt ASC)

### Integration Tests (`MSOSync.IntegrationTests`, Collection: "Operations")

**`ClusterHealthTrendsApiTests`** (4 tests):
- GET returns 200 with correct shape (`Window`, `BucketCount`, `Buckets`, `NodeProbeStats`)
- All four window values accepted (no 400)
- Invalid window value returns 400
- Unauthenticated returns 401

**`RecoveryDashboardApiTests`** (4 tests):
- GET returns 200 with `Summary`, `ActiveRecoveries`, `RecentCompletedRecoveries`
- Seeded recovery node appears in `ActiveRecoveries`
- Completed recovery (transitioned to Active within 30d) appears in `RecentCompletedRecoveries`
- Unauthenticated returns 401

**`ClusterDiagnosticsApiTests`** (4 tests):
- GET returns 200 with `RuntimeStats`, `ActiveLocks`, `SlowOperations` lists
- Empty DB → all three lists empty (not 500)
- Stale lock (`AgeSeconds > 300`) has `IsStale: true`
- Unauthenticated returns 401

### Frontend (Vitest)

**`HealthTrendsPage.test.tsx`** (~5 tests): renders chart, window selector changes query param, nodeId filter passed to hook, empty bucket state shown, probe stats table renders.

**`RecoveryDashboardPage.test.tsx`** (~4 tests): summary cards render correct counts, active recovery row shows elapsed, replay op chips visible, completed recovery RTO shown.

**`DiagnosticsPage.test.tsx`** (~5 tests): stale lock row highlighted, slow op progress bar renders, runtime stats card shows latest entry, empty runtime stats shows empty state, diagnostics panels collapsible.

---

## Global Constraints

- All Phase 2A rules (`.github/PULL_REQUEST_TEMPLATE.md`): named DTOs, `ProducesResponseType`, FluentValidation only, exceptions via `GlobalExceptionHandler`, structured logging, `IOptions<T>` config, RULE-WRK-1/2/3, RULE-TEST-1/2/3.
- RULE-CTL-2: no controller injects `AppDbContext` directly.
- No new EF migrations (M034 was last).
- All work commits directly to `main`.
- All new query methods: `AsNoTracking()`, project directly to DTO, no lazy loading, no `Include()` unless required.
- All timestamps UTC internally; frontend converts for display only.
- `MSOSync.Metadata` must not reference `MSOSync.Batch` or `MSOSync.Routing`.
- `SyncRuntimeStats` may be empty — all three diagnostic sub-lists must return empty list gracefully (never 500).
- No `Task.WhenAll` on queries sharing the same `AppDbContext` instance (EF DbContext not thread-safe).
