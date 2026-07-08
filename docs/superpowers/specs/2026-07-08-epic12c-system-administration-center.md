# Epic 12C — System Administration Center

**Date:** 2026-07-08  
**Status:** Approved  
**Preceded by:** Epic 12B.0 Stabilization Sprint (mandatory gate)  
**Followed by:** Epic 12D — Platform Runtime & Diagnostics

---

## Executive Summary

Epic 12C delivers the **Operations Center** — a unified administrative experience that brings together the operational capabilities introduced across Epics 11, 12A, 12B-1, and 12B-2. Rather than introducing another subsystem, 12C organizes existing capabilities into a cohesive command center with five pillars: a NOC-style Overview dashboard, a unified Jobs registry, a runtime Health center, a Correlation-driven Activity console, and a consolidated Administration section.

After 12C ships, MSOSync CE has all core capabilities expected of a production-grade synchronization system: node onboarding, lifecycle management, configuration management, operational monitoring, security, auditability, and centralized administration.

---

## Prerequisites — 12B.0 Stabilization Sprint

12B.0 is a **mandatory release gate**. 12C does not start until all exit criteria are met.

### Items (execution order)

**1. Security audit**  
Review all structured logs for accidental leakage of: bootstrap tokens, node tokens, JWTs, Authorization headers, connection strings, passwords, configuration secrets. Fix any identified leakage before expanding the platform. Remove `X-Node-Token` value from query-string logging in `NodeTokenAuthMiddleware`.

**2. SignalR validation**  
Validate: reconnect lifecycle (server restart, network drop, tab resume), token refresh flow (access token expiry while connected), push propagation (lifecycle event → UI update, config change → badge refresh). Extended cases: multiple simultaneous browser tabs, 30–60 minute idle session, browser sleep/resume, rapid reconnect storm after API restart, duplicate event detection, event ordering via CorrelationId. Deliverable: signed-off validation checklist. Any failure = blocking defect.

**3. Testcontainers integration**  
Run existing 56 lifecycle/migration tests against SQL Server 2022 Testcontainers. Validate full migration chain from empty database and upgrade path from M018 → latest. Parallel test execution enabled.

**4. Performance cleanup**  
- `ProvisionPackageService`: replace constructor-injected `AppDbContext` with `IServiceScopeFactory`  
- Bulk node operations in `NodeManagementController`: single `SaveChangesAsync` after loop, not per-item  
- Controller permission-check duplication in `NodeManagementController`: extract to shared filter or base method  
- Review lifecycle/configuration pages for N+1 queries  
- Verify no unnecessary `SaveChangesAsync()` calls remain  
- Confirm SignalR broadcasts only on state changes (not every tick)  
- Verify indexes introduced in M022/M023 are optimal

**5. Observability validation**  
Verify every long-running async operation produces: audit event, CorrelationId, SignalR notification, structured log, failure log, duration metric. Every operation must be traceable end-to-end.

**6. Documentation freeze**  
Update: architecture diagrams, API reference, permission matrix, state machine docs, migration history. Verify all Epic 12A/12B specs match implementation.

**7. NodeMetadataAction constants**  
Replace `NodeMetadataAction` string literals with typed constants (matching pattern of `ConfigurationAuditConstants`).

### Exit criteria

- Zero build warnings (`--warnaserror`)
- All unit tests passing
- All integration tests passing, including Testcontainers
- SignalR validation checklist complete with no open defects
- No credential or token leakage in logs
- No scoped lifetime violations
- No per-item `SaveChangesAsync()` loops
- Documentation synchronized with implementation
- QA regression completed

---

## Navigation Architecture

### Top-level routes

```
/                      → role-based redirect (see Role-based Landing)
/overview              → NOC landing (ADMIN, OPERATOR)
/operations/*          → Operations Center shell
/node-management       → existing
/configuration         → existing
/topology              → existing
/monitoring            → existing
/administration/*      → Administration shell
/dashboard/summary     → executive summary (VIEWER landing)
```

### Operations Center sub-routes

```
/operations/nodes          → existing Nodes operational view
/operations/configuration  → existing Configuration operational view
/operations/jobs           → unified operation registry (new)
/operations/health         → runtime health center (new)
/operations/activity       → Activity/Audit (promoted, Correlation added)
```

### Administration sub-routes

```
/administration/users           → existing Users page (relocated)
/administration/roles           → existing Roles page (relocated)
/administration/permissions     → existing Permissions page (relocated)
/administration/feature-flags   → new
/administration/settings        → existing Parameters, consolidated
/administration/retention       → new
/administration/license         → new
/administration/diagnostics     → new
```

Reserved for Epic 12D (nav slots only, no pages shipped in 12C):
```
/administration/runtime
/administration/maintenance
```

Reserved for future optional modules (namespace only, no implementation):
```
/operations/plugins
```
This avoids a navigation redesign if CE ever gains optional modules.

### Redirects

```
/audit             → /operations/activity
/admin/users       → /administration/users
/admin/roles       → /administration/roles
/admin/permissions → /administration/permissions
/dashboard         → /dashboard/summary
```

### Role-based landing page

| Role     | Landing              |
|----------|----------------------|
| ADMIN    | /overview            |
| OPERATOR | /overview            |
| VIEWER   | /dashboard/summary   |

Implemented via router-level redirect reading user role from JWT claims. Uses 11F RBAC permission infrastructure.

### Sidebar structure

```
Overview

Operations
  ├── Nodes
  ├── Configuration
  ├── Jobs
  ├── Health
  └── Activity

Node Management

Configuration

Topology

Monitoring

Administration
  ├── Users
  ├── Roles
  ├── Permissions
  ├── Feature Flags
  ├── Settings
  ├── Retention
  ├── License
  └── Diagnostics
```

---

## Pillar 1 — Overview (NOC Dashboard)

**Route:** `/overview`  
**Audience:** ADMIN, OPERATOR  
**Purpose:** answer three questions — Is the system healthy? Does anything require action? What changed recently?

### Backend

**`GET /api/v1/system/overview`** backed by `IOverviewQueryService`. Single aggregation endpoint; no new tables.

**`OverviewDto` (widget-based):**

```csharp
Health:
  ClusterHealth:  Healthy | Degraded | Critical   // derived from WorkerHealth + NodeHealth
  WorkerHealth:   Healthy | Warning | Failed       // from IWorkerStatusRegistry
  NodeHealth:     Healthy | Warning | Critical     // from node state distribution

Operations:
  Running:         int
  SucceededToday:  int
  FailedToday:     int
  Queued:          int

Nodes:
  Total:        int
  Active:       int
  Offline:      int
  Maintenance:  int
  Degraded:     int
  PendingRegistrations: int

Configuration:
  DriftedCount:          int
  UpdateAvailableCount:  int
  FailedCount:           int

Warnings: WarningDto[]   // sorted by severity descending

RecentActivity: OverviewEventDto[]   // last 10 events

System:
  Version:           string
  DatabaseMigration: string   // e.g. "M024"
  Environment:       string
  Uptime:            string
  SignalRStatus:     string
```

**`WarningDto`:**

```csharp
Type:          string       // "MissedHeartbeat" | "ConfigDrift" | "WorkerFailed" etc.
Severity:      Critical | High | Medium | Low
Title:         string
Description:   string
TargetRoute:   string       // e.g. "/operations/nodes"
CorrelationId: string?
```

**`OverviewEventDto`:**

```csharp
EventId:       string
OccurredAt:    DateTime
Category:      string       // Lifecycle | Configuration | Operation | Connectivity
Summary:       string
NodeId:        string?
CorrelationId: string?
DeepLink:      string?      // → /operations/activity with correlationId pre-filled
```

**`ClusterHealth` derivation:**
- Critical if any worker is Failed OR >10% of total nodes are offline
- Degraded if any worker is Warning/Delayed OR any Warning exists in node distribution
- Healthy otherwise

**`IOverviewQueryService`** — also powers the Overview widget in the health aggregate; single query service, no duplication.

**Server-side snapshot cache (`OverviewSnapshotCache`):**  
`IOverviewQueryService` wraps results in a 5-second in-memory cache. Invalidated immediately by: `OperationChanged`, `WorkerStatusChanged`, `NodeLifecycleChanged`, `ConfigurationStateChanged`. Without this, every SignalR event would trigger a fresh cross-subsystem aggregation query, making `/api/v1/system/overview` the busiest endpoint in the system.

**Refresh via SignalR:**  
`OverviewRefreshed` message broadcast (after cache invalidation) on the same four events. Frontend debounces 5 seconds before re-fetching.

Additional refresh triggers: initial load, browser tab becomes active, manual refresh button.

### Frontend

**Zone A — Status bar (top):**  
ClusterHealth badge (color-coded), WorkerHealth badge, NodeHealth badge, ActiveJobs count. Always visible.

**Zone B — Action Required (mid):**  
Cards rendered only when count > 0, sorted by severity descending:
- Failed Jobs → `/operations/jobs`
- Nodes Offline / Degraded → `/operations/nodes`
- Pending Registrations → `/node-management`
- Configuration Drift → `/operations/configuration`
- Connectivity Issues → `/operations/nodes`
- System Warnings (structured `WarningDto` with "Open →" link)

Empty state: "System healthy — no action required."

**Zone C — Quick Actions (compact strip):**  
Approve Registrations, View Failed Jobs, Open Drift, Create Node, View Workers.

**Zone D — Recent Activity (bottom):**  
Last 10 events, each row: category badge, summary, timestamp, "View Correlation →" link.

**Zone E — System Info strip (footer or top-right card):**  
Version, Database migration, Environment, Uptime, SignalR status, **Last Refreshed** timestamp (UTC). Operators must always be able to confirm whether they are viewing live data.

---

## Pillar 2 — Jobs Tab (sync_operation)

**Route:** `/operations/jobs`  
**Purpose:** unified view of all long-running and orchestrated operations

### M024 Migration — sync_operation table

```sql
CREATE TABLE [msosync].[sync_operation] (
    operation_id     UNIQUEIDENTIFIER NOT NULL DEFAULT NEWID(),
    operation_type   VARCHAR(50)      NOT NULL,   -- Export|Rollout|Decommission|Recovery
    reference_id     UNIQUEIDENTIFIER NULL,        -- FK to domain table
    status           VARCHAR(30)      NOT NULL,   -- Pending|Running|Completed|Failed|Cancelled
    result           VARCHAR(30)      NULL,        -- Success|PartialSuccess|Failure|Cancelled
    source           VARCHAR(30)      NOT NULL,   -- User|System|Scheduler|Worker|API
    progress_percent INT              NULL,
    progress_message VARCHAR(500)     NULL,        -- "Processing node 41 of 120"
    correlation_id   VARCHAR(100)     NULL,
    initiated_by     UNIQUEIDENTIFIER NULL,        -- FK sync_user; NULL when source != User
    metadata_json    NVARCHAR(2000)   NULL,        -- display-only: {"template":"Clinic v4"}
    summary          VARCHAR(500)     NULL,
    can_cancel       BIT              NOT NULL DEFAULT 0,
    can_retry        BIT              NOT NULL DEFAULT 0,
    started_at       DATETIME2        NOT NULL,
    completed_at     DATETIME2        NULL,
    CONSTRAINT PK_sync_operation PRIMARY KEY (operation_id)
);

CREATE INDEX IX_sync_operation_status        ON [msosync].[sync_operation] (status);
CREATE INDEX IX_sync_operation_type          ON [msosync].[sync_operation] (operation_type);
CREATE INDEX IX_sync_operation_started_at    ON [msosync].[sync_operation] (started_at DESC);
CREATE INDEX IX_sync_operation_correlation   ON [msosync].[sync_operation] (correlation_id);
```

### Design invariant — sync_operation ownership

> `sync_operation` **never owns domain state.** Its sole purpose is the orchestration index. Future contributors must not add business-specific fields (e.g., `TemplateId`, `ExportFormat`, `GracePeriod`) to this table. Such data belongs in the domain table referenced by `reference_id`. Violations make the table a second source of truth and break the handler dispatch model.

### Domain integration

Existing services write a `sync_operation` row when initiating async work. Domain tables remain authoritative for business data; `sync_operation` is the status index only.

| Service | OperationType | ReferenceId |
|---|---|---|
| `ExportJobService.CreateJobAsync` | `Export` | ExportJobId |
| `RolloutService.StartRolloutAsync` | `Rollout` | RolloutId |
| `NodeLifecycleService` (Decommission) | `Decommission` | NodeId |

### IOperationService

```csharp
interface IOperationService
{
    Task<Guid> CreateAsync(OperationType type, Guid? referenceId, Guid? initiatedBy,
                           OperationSource source, string correlationId,
                           bool canCancel, bool canRetry, string summary,
                           string? metadataJson, CancellationToken ct);

    Task UpdateProgressAsync(Guid operationId, int percent, string? message, CancellationToken ct);
    Task CompleteAsync(Guid operationId, OperationResult result, string? summary, CancellationToken ct);
    Task CancelAsync(Guid operationId, Guid actorId, CancellationToken ct);
    Task RetryAsync(Guid operationId, Guid actorId, CancellationToken ct);
}
```

### IOperationHandler registry (replaces switch dispatch)

```csharp
interface IOperationHandler
{
    OperationType OperationType { get; }
    Task CancelAsync(Guid referenceId, Guid actorId, CancellationToken ct);
    Task RetryAsync(Guid referenceId, Guid actorId, CancellationToken ct);
}
```

Implementations registered with DI keyed by `OperationType`:
- `ExportOperationHandler`
- `RolloutOperationHandler`
- `DecommissionOperationHandler`

`IOperationService.CancelAsync/RetryAsync` resolves handler via keyed DI — no central switch. Adding a new operation type = add a new `IOperationHandler` implementation.

### SignalR

`IOperationService` publishes `OperationChangedEvent` (MediatR) after every status change. `OperationChangedPublisher` broadcasts to "operators" group:

```csharp
record OperationChangedEvent(
    Guid OperationId, string OperationType, string Status, string? Result,
    int? ProgressPercent, string? ProgressMessage, string? CorrelationId);
```

Domain-specific publishers (`ExportJobChangedEvent`) may coexist for domain-specific consumers (e.g., per-user export toast). The Operations Jobs page subscribes to `OperationChanged` only.

### OperationsController — new

```
GET  /api/v1/operations                    ViewerOrAbove — cursor-paginated list
GET  /api/v1/operations/{id}               ViewerOrAbove — detail + execution timeline
POST /api/v1/operations/{id}/cancel        ManageConfigurations | ManageNodeLifecycle
POST /api/v1/operations/{id}/retry         ManageConfigurations | ManageNodeLifecycle
```

Operation detail response includes: domain reference deep-link, correlation deep-link, execution timeline (derived from timestamps), metadata.

### Retention

`sync_parameter` key `Retention.OperationDays` (default 180). Extended `PurgeJob` deletes completed/failed/cancelled `sync_operation` rows older than configured retention.

### Frontend

Table columns: Type badge, Summary, Status badge, Progress bar + message, **Queue Position** (shown only for Pending operations — indicates position in pending queue; useful during large rollouts), Source, Started by, Started, Duration, Actions (Cancel / Retry / View Correlation).

Filter bar: type multi-select, status multi-select, source multi-select, date range, initiator.

Clicking any row or "View Correlation" opens Activity Correlation tab seeded with `correlationId`.

---

## Pillar 3 — Health Tab (IWorkerStatusRegistry)

**Route:** `/operations/health`  
**Purpose:** runtime diagnostics center for workers, database, and infrastructure

### IWorkerStatusRegistry (in-memory singleton)

**Startup registration invariant:**  
`IWorkerStatusRegistry.Register()` must be called in each worker's `StartAsync`. If a hosted worker starts without calling `Register()`, the Health page silently omits it — a hidden availability gap. The service startup sequence should validate that all expected workers are registered. Consider a startup health check that lists registered vs expected worker names and fails fast on discrepancy.

```csharp
interface IWorkerStatusRegistry
{
    void Register(string workerName, TimeSpan expectedInterval);
    void RecordTickStart(string workerName, TickTrigger trigger = TickTrigger.Scheduled);
    void RecordTickComplete(string workerName);
    void RecordTickFailed(string workerName, Exception ex);
    WorkerStatusDto GetOne(string workerName);
    WorkerStatusDto[] GetAll();
}
```
```

Workers call `RecordTickStart` / `RecordTickComplete` / `RecordTickFailed` on each iteration. No other changes to worker logic required. Registry is thread-safe: `ConcurrentDictionary<string, WorkerState>`, lock-free reads, per-worker lock only when updating rolling averages.

### WorkerStatusDto

```csharp
record WorkerStatusDto(
    string WorkerName,
    string WorkerVersion,
    TimeSpan ExpectedInterval,
    DateTime RegisteredAt,
    bool Enabled,

    // combined UI state (derived from ExecutionState + HealthState)
    WorkerState State,          // Running|Idle|Warning|Delayed|Failed|Disabled

    // internal separation
    WorkerExecutionState ExecutionState,  // Running|Idle
    WorkerHealthState HealthState,        // Healthy|Warning|Delayed|Failed|Disabled

    DateTime? LastStarted,
    DateTime? LastCompleted,
    DateTime? LastSuccessfulRun,
    DateTime? NextExpected,
    long AverageDurationMs,
    long LastDurationMs,
    long ExecutionCount,
    int ConsecutiveFailures,
    string? LastError,
    DateTime LastHeartbeat,

    // summary statistics
    double SuccessRatePct,
    long MaxDurationMs,
    int FailureCount,
    DateTime? LastFailureAt,

    TickRecord[] RecentTicks    // last 100 executions
);

record TickRecord(
    DateTime StartedAt,
    DateTime CompletedAt,
    long DurationMs,
    bool Success,
    string? Error,
    TickTrigger Trigger         // Scheduled|Manual|Startup|Retry
);
```

### State derivation

```
ConsecutiveFailures >= 5        → Failed
ConsecutiveFailures >= 3        → Warning
Now - LastCompleted > Interval × 3  → Delayed   (stuck detection)
ExecutionCount == 0 AND Now - RegisteredAt > Interval × 2  → Warning ("Never started")
LastStarted != null AND LastCompleted < LastStarted  → Running
otherwise                       → Idle
```

State transitions emit `WorkerStatusChangedEvent` (MediatR) → `WorkerStatusChangedPublisher` broadcasts to "operators" group on transitions only (not every tick).

### ISystemHealthContributor pattern

```csharp
interface ISystemHealthContributor
{
    string Name { get; }
    Task<HealthContribution> GetHealthAsync(CancellationToken ct);
}

record HealthContribution(string Name, HealthLevel Level, string Summary, object? Detail);
```

Implementations registered and resolved by `ISystemHealthService`:
- `WorkerHealthContributor` — reads `IWorkerStatusRegistry`
- `DatabaseHealthContributor` — checks connection + latency
- `SignalRHealthContributor` — counts connected clients
- `ApiHealthContributor` — version + uptime

### Backend endpoints

```
GET /api/v1/system/workers          ViewerOrAbove — WorkerStatusDto[] (all, no history)
GET /api/v1/system/workers/{name}   ViewerOrAbove — WorkerStatusDto + full tick history
POST /api/v1/system/workers/{name}/diagnostics   AdminOnly — read-only probe: dep resolution, config state, registry snapshot. No execution, no writes, no retries, no side effects.
GET /api/v1/system/health           ViewerOrAbove — ISystemHealthContributor[] aggregated
GET /api/v1/system/info             ViewerOrAbove — version, build, migration, runtime, env, uptime
```

### ASP.NET Core health integration

`WorkerHealthCheck : IHealthCheck` reads from `IWorkerStatusRegistry`. Maps Failed → Unhealthy; Warning/Delayed → Degraded; else Healthy.

`/health/live` = process alive (no dependency check).  
`/health/ready` = `PersistenceHealthCheck` + `WorkerHealthCheck` combined.

`/api/v1/system/health` is separate: rich structured JSON for the UI, not the infrastructure probe format.

### Frontend

**Workers summary bar** (above grid): total worker count, counts by state, **Longest Running Worker** (name + duration of current tick if Running). Surfacing the longest-running worker early makes it easy to spot stuck or slow workers at a glance.

**Workers grid:** sorted Failed → Warning → Delayed → Running → Idle → Disabled.  
Card per worker: Name, State badge, Last Run (relative), Avg Duration, Failures badge, Next Expected.

Clicking a card opens an inline detail panel:
- State + health reason
- Last successful execution / last failure
- Execution statistics (success rate %, avg/max duration, failure count)
- Tick history chart (last 100 runs: bars colored green/red, height = duration)
- Recent errors list
- Configuration (expected interval, registered at, enabled)

**Database panel:** connection status, last query latency, migration version.

**SignalR panel:** connection state, connected clients count, last event timestamp.

**Refresh:** SignalR `WorkerStatusChanged` → immediate card update. Full re-fetch on tab activate.

---

## Pillar 4 — Activity Tab (Correlation)

**Route:** `/operations/activity`  
**Redirect:** `/audit` → `/operations/activity`

### Tab structure

Three tabs on the Activity page:

- **Log** — unchanged from existing AuditPage (cursor pagination, filters, export, preference persistence)
- **Correlation** — new CorrelationId-based timeline
- **Insights** — unchanged from existing AuditPage (charts: activity over time, top users, entity changes)

### Correlation tab — backend

**New indexes on M024:**

```sql
CREATE INDEX IX_sync_audit_correlation_time
  ON [msosync].[sync_audit] (correlation_id, create_time);

CREATE INDEX IX_sync_operation_correlation
  ON [msosync].[sync_operation] (correlation_id);           -- already in Pillar 2

CREATE INDEX IX_sync_node_lifecycle_history_correlation
  ON [msosync].[sync_node_lifecycle_history] (correlation_id);

CREATE INDEX IX_sync_node_configuration_history_correlation
  ON [msosync].[sync_node_configuration_history] (correlation_id);
```

**New endpoints on `AuditController`:**

```
GET /api/v1/audit/correlation/{correlationId}
GET /api/v1/audit/correlation/search?nodeId=&operationId=&templateId=&userId=&correlationId=&from=&to=
```

**`CorrelationTimelineDto`:**

```csharp
record CorrelationTimelineDto(
    string CorrelationId,
    Guid? OperationId,
    string? OperationType,
    string? OperationStatus,
    string? OperationResult,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    TimeSpan? Duration,

    // Summary card
    string? InitiatedBy,
    EntityChipDto[] EntityChips,   // Node, Template, Operation, User chips with deep links
    int TotalEventCount,
    bool IsFailedWorkflow,
    string? FailureSummary,        // "Workflow ended with failure. Last step: Config Assigned"

    // Phase-grouped timeline
    CorrelationPhaseDto[] Phases
);

record CorrelationPhaseDto(
    string PhaseName,              // "Registration" | "Lifecycle" | "Configuration" | "Operations"
    CorrelationEventDto[] Events
);

record CorrelationEventDto(
    long AuditId,
    DateTime OccurredAt,
    TimeSpan? DurationSincePrevious,
    string ActionName,
    string Summary,
    string? ActorUsername,
    CorrelationCategory Category,
    CorrelationSeverity Severity,
    string? EntityType,
    string? EntityId,
    string? DeepLink                // server-derived navigation target
);

record EntityChipDto(string Type, string Label, string DeepLink);

enum CorrelationCategory { Registration, Lifecycle, Configuration, Operation, Connectivity, Security, Audit, System }
enum CorrelationSeverity { Information, Warning, Error, Critical }
```

**Timeline data sources:**

| Source | Role |
|---|---|
| `sync_audit` | Primary — authoritative event log |
| `sync_operation` | Enrichment — operation metadata, status, result |
| `sync_node_lifecycle_history` | Enrichment — lifecycle transitions |
| `sync_node_configuration_history` | Enrichment — configuration state changes |

`AuditQueryService` assembles the timeline; `sync_audit.correlation_id` is the join key.

**`DeepLink` server-side derivation** from `ActionName` prefix:
- `NODE_*` → `/operations/nodes/{entityId}`
- `ROLLOUT_*` → `/operations/jobs/{operationId}`
- `CONFIGURATION_*` → `/configuration/templates/{entityId}`
- `EXPORT_*` → `/operations/jobs/{operationId}`
- `AUTH_*` → `/administration/users/{entityId}`

**Failed workflow detection:** `IsFailedWorkflow = true` when `OperationResult` is Failure/Cancelled OR last event `Severity` is Error/Critical.

**Correlation export:**  
`GET /api/v1/audit/correlation/{correlationId}/export?format=json|pdf|markdown`

### Frontend

**Search bar:** accepts CorrelationId, OperationId, NodeId. Backend resolves to CorrelationId via `/correlation/search`. If multiple matches returned, prompt with result list.

**Correlation Summary card** (above timeline):

```
CorrelationId   0F3A...
Operation       Configuration Rollout
Status          Completed
Result          PartialSuccess
Duration        2m 41s
Started by      admin
Entities        [Node Clinic-07] [Template Clinic v4] [Operation Rollout-102]
Total events    24
```

**Timeline rendering:**  
Phase headers (bold) → chronological events within phase → elapsed-time gap badge between events ("+15s").

```
Configuration
  ● [Config/Info]   Template v4 Assigned      14:32:01  admin
                                               +15s
  ● [Config/Info]   Config Downloaded          14:32:16  node
  ● [Config/Info]   Config Applied             14:32:17  node
  ● [Config/Info]   Node → Current             14:32:49  node
```

Failed workflow banner (when `IsFailedWorkflow`):

```
⚠ Workflow ended with failure
Last successful step: Configuration Assigned
Failure: Node never acknowledged configuration.
```

**Phase collapse:** each phase header is collapsible. Collapsed state shows phase name, event count, and outcome badge ("4 events ✓"). Long workflows (20+ events across 5+ phases) become easy to scan with irrelevant phases collapsed.

**Cross-navigation** (EntityChips): above timeline, each chip links to the domain entity.

**Deep linking:** every row with a `DeepLink` is clickable. Every surface that carries a `correlationId` (Jobs table, Overview Recent Activity, Node detail, Configuration assignments) includes "View Correlation →".

**Permissions:** `ViewerOrAbove` (same as existing Audit). Export requires `EXPORT_DATA` (existing).

---

## Pillar 5 — Administration

### What moves (no rework)

| Current route | New route | Change |
|---|---|---|
| `/admin/users` | `/administration/users` | redirect only |
| `/admin/roles` | `/administration/roles` | redirect only |
| `/admin/permissions` | `/administration/permissions` | redirect only |

Existing controllers, services, and components unchanged.

### SyncParameter — extended (M024)

New nullable columns on `sync_parameter`:

```sql
ALTER TABLE [msosync].[sync_parameter] ADD
    category       VARCHAR(50)   NULL,   -- FeatureFlag | Heartbeat | Export | Configuration | Retention | System
    display_name   VARCHAR(200)  NULL,
    description    VARCHAR(1000) NULL,
    display_order  INT           NULL,
    value_type     VARCHAR(30)   NULL,   -- Boolean | Integer | String | TimeSpan | Duration | Enum
    minimum_value  VARCHAR(100)  NULL,
    maximum_value  VARCHAR(100)  NULL,
    allowed_values NVARCHAR(MAX) NULL,   -- JSON array for Enum type
    depends_on     VARCHAR(200)  NULL,   -- parameter name
    conflicts_with VARCHAR(200)  NULL;   -- parameter name
```

Existing `ParametersController` gains `?category=` filter. UI becomes fully metadata-driven — no frontend changes required for new parameters.

Every parameter update audits `PARAMETER_UPDATED` with old value, new value, actor, CorrelationId, timestamp. Existing `ParameterChangedEvent` (MediatR) extended with `OldValue`.

### Feature Flags

Parameters with `Category = 'FeatureFlag'`. Seeded in M025:

| Name | Default | Description |
|---|---|---|
| `EnableConfigurationRollout` | true | Enables the rollout pipeline |
| `EnableTopologyEditing` | false | Allows editing topology via UI |
| `EnableExperimentalUI` | false | Unlocks experimental frontend features |
| `EnableBackgroundCleanup` | true | Activates nightly cleanup workers |
| `EnableExportJobs` | true | Enables background export job processing |

`/administration/feature-flags`: card per flag showing name, description, boolean toggle, last modified, modified by. `IsDynamic = true` → applied immediately. `RequiresRestart = true` → shows "Restart Required" badge.

Filter/search bar: **Search by name**, **Category** multi-select, **Recently Modified** sort (last 7 days highlighted). Once there are 50+ flags, a flat toggle list becomes unusable without filtering.

**Permission:** `AdminOnly`.

### System Settings

`/administration/settings`: existing parameters grouped by `Category`. Existing parameters gain `Category`, `DisplayName`, `Description`, `DisplayOrder`, `ValueType`, validation bounds in migration. UI renders groups with field types driven by `ValueType`.

Badge per parameter: `✓ Live` (IsDynamic=true) or `⚠ Restart Required` (RequiresRestart=true).

**Permission:** `AdminOnly`.

### Retention Policies

New `sync_parameter` keys seeded in M025:

| Key | Default | Description |
|---|---|---|
| `Retention.AuditDays` | 90 | Days to retain audit records |
| `Retention.OperationDays` | 180 | Days to retain completed operations |
| `Retention.ConnectivityHistoryDays` | 30 | Days to retain connectivity history |
| `Retention.LifecycleHistoryDays` | 365 | Days to retain lifecycle history |
| `Retention.ExportJobHours` | 24 | Hours to retain completed export job records |

`PurgeJob` reads these at runtime. `/administration/retention`: editable number fields per policy with description and estimated storage impact (rough estimate: rows/day × avg row size).

**Permission:** `AdminOnly`.

### License / About

`GET /api/v1/system/info` (no auth required beyond ViewerOrAbove):

```csharp
record SystemInfoDto(
    string Version, string BuildDate, string GitCommit,
    string DotNetRuntime, string OperatingSystem,
    string DatabaseMigration, string Edition,   // "Community"
    string Environment, string ServerTime, string ProcessUptime
);
```

`/administration/license`: single card. Useful for QA and support workflows.

**Permission:** `ViewerOrAbove`.

### Diagnostics

`/administration/diagnostics`: index page, not a dashboard.

Tiles with live state badge + one key metric from `GET /api/v1/system/health`:

| Tile | Metric example | Links to |
|---|---|---|
| Workers | "9/9 Running" | `/operations/health` |
| Database | "42 ms" | `/operations/health` |
| SignalR | "18 Clients" | `/operations/health` |
| API Info | version inline | inline |
| Activity | "Last event: 2m ago" | `/operations/activity` |

No data duplication — state comes from `ISystemHealthContributor` aggregation. Tiles are navigational; data lives at destination.

**Permission:** `ViewerOrAbove` (destination pages enforce their own policies).

### Permission matrix

| Page | Required Permission |
|---|---|
| Users | MANAGE_USERS |
| Roles | MANAGE_USERS |
| Permissions | MANAGE_USERS |
| Feature Flags | AdminOnly |
| Settings | AdminOnly |
| Retention | AdminOnly |
| License / About | ViewerOrAbove |
| Diagnostics | ViewerOrAbove |

---

## Database Migrations

### M024 — Operations Foundation

Scoped to the `sync_operation` registry and correlation performance. Kept small for safe rollback.

1. Create `sync_operation` table (Pillar 2, see DDL above)
2. Add composite index `IX_sync_audit_correlation_time` on `sync_audit (correlation_id, create_time)`
3. Add index `IX_sync_node_lifecycle_history_correlation` on `sync_node_lifecycle_history (correlation_id)`
4. Add index `IX_sync_node_configuration_history_correlation` on `sync_node_configuration_history (correlation_id)`

### M025 — Parameter Metadata & Administration Seeds

Separate migration for `sync_parameter` schema and data changes. Isolating from M024 means a failed seed rollback does not affect the operation registry.

1. Add nullable columns to `sync_parameter`: `category`, `display_name`, `description`, `display_order`, `value_type`, `minimum_value`, `maximum_value`, `allowed_values`, `depends_on`, `conflicts_with`
2. Seed Feature Flag parameters (Category = 'FeatureFlag')
3. Seed Retention Policy parameters (Category = 'Retention')
4. Update existing system parameters with category groupings and metadata

---

## API Summary

### New endpoints

```
GET  /api/v1/system/overview
GET  /api/v1/system/health
GET  /api/v1/system/info
GET  /api/v1/system/workers
GET  /api/v1/system/workers/{name}
POST /api/v1/system/workers/{name}/diagnostics

GET  /api/v1/operations
GET  /api/v1/operations/{id}
POST /api/v1/operations/{id}/cancel
POST /api/v1/operations/{id}/retry

GET  /api/v1/audit/correlation/{correlationId}
GET  /api/v1/audit/correlation/search
GET  /api/v1/audit/correlation/{correlationId}/export
```

### Extended endpoints

```
GET  /api/v1/parameters?category=  (new filter)
```

---

## SignalR Events

New events added to `OperationsEventType`:

```
OverviewRefreshed       — debounced aggregate refresh signal
WorkerStatusChanged     — on worker health state transition only
OperationChanged        — on every sync_operation status update
```

Existing events unchanged.

---

## Frontend pages

### New pages

| Page | Route | Component |
|---|---|---|
| Overview | /overview | `OverviewPage.tsx` |
| Jobs | /operations/jobs | `JobsPage.tsx` |
| Health | /operations/health | `HealthPage.tsx` |
| Feature Flags | /administration/feature-flags | `FeatureFlagsPage.tsx` |
| Retention | /administration/retention | `RetentionPage.tsx` |
| License | /administration/license | `LicensePage.tsx` |
| Diagnostics | /administration/diagnostics | `DiagnosticsPage.tsx` |

### Modified pages

| Page | Change |
|---|---|
| AuditPage | Promoted to `/operations/activity`; Correlation tab added; `/audit` redirects |
| DashboardPage | Route becomes `/dashboard/summary`; simplified for VIEWER |
| AppLayout / Sidebar | Nav restructured; role-based landing redirect |
| Administration section | Users/Roles/Permissions relocated under `/administration/*` |

### New shared components

- `CorrelationTimeline` — phase-grouped timeline with severity badges and elapsed-time gaps
- `WorkerCard` — state badge, stats, expandable tick history chart
- `OperationStatusBadge` — unified status+result chip
- `OverviewWarningCard` — structured `WarningDto` with "Open →" action

---

## Testing Strategy

### Unit tests (MSOSync.Metadata / MSOSync.App)

- `IOverviewQueryService` — health derivation logic, ClusterHealth rules
- `IWorkerStatusRegistry` — state derivation, stuck detection, startup validation, concurrent updates
- `IOperationService` — create, update, complete, handler dispatch via `IOperationHandler`
- `CorrelationTimelineAssembler` — phase grouping, severity mapping, DeepLink derivation
- `ISystemHealthContributor` implementations — each contributor independently

### Integration tests

- `OverviewTests` — end-to-end: seed nodes + rollout + drift → GET /overview validates all widget fields
- `OperationRegistryTests` — create Export/Rollout/Decommission operations; verify cancel/retry dispatch; verify SignalR event; verify PurgeJob retention
- `WorkerStatusTests` — register workers, simulate ticks and failures, verify state transitions and SignalR
- `CorrelationTimelineTests` — seed audit events with shared correlationId + operation row; GET /audit/correlation/{id}; verify phase grouping, entity chips, failure banner
- `AdministrationTests` — feature flag toggle; parameter validation; retention policy update + PurgeJob; PARAMETER_UPDATED audit generation
- `NavigationTests` — verify all redirects (/audit, /admin/*, /dashboard) return 301/302 to correct targets
- `OverviewPerformanceTests` — seed 1000 operations, 100 nodes (varied states), 100 workers (varied states); `GET /api/v1/system/overview` must respond in under 500 ms. Not a hard SLA but establishes a regression baseline before the endpoint becomes production load.

---

## Deferred to Epic 12D — Platform Runtime & Diagnostics

The following are explicitly out of scope for 12C:

- Dynamic worker interval configuration (requires worker loop redesign)
- Maintenance windows (affects scheduler, retry, export, lifecycle, rollout workers)
- Worker pause/resume controls
- Runtime parameter reload without restart
- Scheduler control panel
- Advanced performance profiling
- Alert rules and notification routing

These naturally group together as a runtime management capability and are reserved for 12D.

---

## Roadmap position

```
Epic 12A  ✓ Node Registration & Provisioning
Epic 12B-1 ✓ Node Lifecycle Engine
Epic 12B-2 ✓ Configuration Management
Epic 12B.0   Stabilization Sprint  ← mandatory gate
Epic 12C     System Administration Center  ← this spec
Epic 12D     Platform Runtime & Diagnostics
Epic 13      Enterprise capabilities
```

---

## Appendix — Correlation Lifecycle Example

A complete workflow trace from node approval to configuration current, all linked by one `CorrelationId`.

```
Phase: Registration
  ● [Registration/Info]  Registration Received           09:14:01  actor: node
  ● [Registration/Info]  Registration Approved           09:14:45  actor: admin

Phase: Lifecycle
  ● [Lifecycle/Info]     Bootstrap Token Generated       09:14:46  actor: system
  ● [Lifecycle/Info]     Node Activated                  09:14:47  actor: system
                                                         +2s
  ● [Lifecycle/Info]     Heartbeat Received              09:14:49  actor: node
  ● [Lifecycle/Info]     Node → Active                   09:14:49  actor: system

Phase: Configuration
  ● [Configuration/Info] Template v4 Assigned            09:15:03  actor: admin
  ● [Configuration/Info] Node → UpdateAvailable          09:15:03  actor: system
                                                         +18s
  ● [Configuration/Info] Configuration Downloaded        09:15:21  actor: node
  ● [Configuration/Info] Configuration Applied           09:15:22  actor: node

Phase: Operations
                                                         +27s
  ● [Configuration/Info] Heartbeat: hash match           09:15:49  actor: node
  ● [Configuration/Info] Node → Current                  09:15:49  actor: system

EntityChips: [Node Clinic-07] [Template Clinic v4] [Operation Rollout-102] [User admin]
Duration: 1m 48s   Result: Success
```

This trace is assembled from `sync_audit` (primary), `sync_node_lifecycle_history` (Lifecycle phase enrichment), `sync_node_configuration_history` (Configuration phase enrichment), and `sync_operation` (operation metadata). The Activity page reconstructs it in a single indexed query per table; no cross-table join is needed because each table is indexed on `correlation_id`.

After 12C ships, MSOSync CE has all core capabilities of a production-grade synchronization platform. Subsequent work (12D, 13) extends the platform for advanced operational control and enterprise requirements rather than filling architectural gaps.
