# Service Responsibility Map

## Layer Overview

```
MSOSync.Api                   — HTTP controllers, middleware, auth policies, DTOs, validators
    ↓ depends on
MSOSync.Metadata              — Domain services, query services, business logic
    ↓ depends on
MSOSync.Persistence           — EF Core entities, AppDbContext, migrations, platform repos
    ↓ depends on
SQL Server                    — Data store

Cross-cutting (any layer may depend on these):
MSOSync.Common                — IClock, exceptions, tenancy abstractions (no internal deps)
MSOSync.Security              — IUserService, ITenantResolver, JWT, BCrypt, audit
MSOSync.Engine                — IApplyService, ISqlConnectionFactory
MSOSync.Transport             — INodeHttpClient, push/pull clients, compression
MSOSync.Batch                 — IBatchCreator, IBatchStateMachine, IBatchTransportQueryService
MSOSync.Plugin / MSOSync.Sdk  — Plugin host, registry, SDK abstractions (App layer only)
MSOSync.App                   — Composition root (Program.cs), background workers, worker registry
MSOSync.Scheduler             — Scheduled jobs (referenced by App only)
```

## Rules

- **RULE-ARCH-1:** `MSOSync.Api` must not reference `MSOSync.Scheduler`.
  Verified: no matches. One approved `MSOSync.Engine` reference exists —
  `SyncController` injects `IApplyService` to apply incoming pushed batches
  (the transport apply path). Recorded as 2A-030 (Accepted).
- **RULE-ARCH-2:** `MSOSync.Metadata` must not reference `MSOSync.Api`.
  Verified: no matches.
- **RULE-ARCH-3:** `AppDbContext` access is confined to `MSOSync.Metadata`,
  `MSOSync.Persistence`, `MSOSync.Security`, and `MSOSync.App` workers.
  Three controller-level exceptions exist (`AuthController` switch-tenant
  membership lookup, `BatchController` outgoing-batch queries,
  `NodeLifecycleController` node read) — recorded as 2A-029 (Deferred:
  extract to query services in Phase 2B).

Codified invariants (`tests/MSOSync.ArchTests/DependencyTests.cs`, NetArchTest):

1. `MSOSync.Common` has no dependency on any other MSOSync project.
2. No domain module depends on `MSOSync.Api` or `MSOSync.App`.

## Service Catalog

### MSOSync.Api — Controllers (32)

| Area | Controllers | Key Dependencies |
|---|---|---|
| Auth & users | `AuthController`, `UsersController`, `RolesController`, `PermissionsController` | `AuthenticationService`, `IUsersManagementService`, `IPermissionService` |
| Sync transport | `SyncController` | `IBatchTransportQueryService`, `IBatchStateMachine`, `IApplyService`, `INodeAuthorizationService` |
| Batches & events | `BatchController`, `BatchErrorsController`, `IncomingBatchesController`, `EventsController` | `IIncomingBatchQueryService`, `IBatchErrorQueryService`, `IEventQueryService` |
| Metadata CRUD | `ChannelsController`, `TriggersController`, `RoutersController`, `ParametersController`, `MetadataController` | `I*MetadataService` per resource |
| Nodes & lifecycle | `NodesController`, `NodeManagementController`, `NodeLifecycleController`, `NodeScopeController` | `INodeManagementService`, `INodeLifecycleService`, `IBootstrapTokenService` |
| Configuration | `NodeConfigurationController`, `ConfigurationTemplateController`, `ConfigurationAssignmentController` | configuration services, rollout services |
| Dashboard & observability | `DashboardController`, `MetricsController`, `TopologyController`, `SystemController`, `AuditController`, `LocksController` | `IDashboardQueryService`, `IMetricsQueryService`, `ITopologyQueryService`, `ISystemHealthService`, `IAuditQueryService`, `ILockAdminService` |
| Operations & export | `OperationsController`, `ExportJobController` | `IOperationService`, `IOperationQueryService`, `IExportJobService` |
| User-facing | `PreferencesController`, `NotificationController`, `PluginController` | `IUserPreferencesService`, notification services, `IPluginRuntimeManager` |

### MSOSync.Metadata — Domain Services (selection)

| Service | Responsibility | Key Dependencies |
|---|---|---|
| `AuditService` / `AuditQueryService` | Write / read audit records | `AppDbContext` |
| `PermissionService` | Resolve effective permissions | `AppDbContext`, `IMemoryCache` |
| `UserPreferencesService` | Get/set user preferences | `AppDbContext`, `ICurrentUserService` |
| `TopologyQueryService` | Hub/child topology graph | `AppDbContext`, `IMemoryCache` |
| `UsersManagementService` | User CRUD, role assignment | `AppDbContext`, `IUserService` |
| `NodeLifecycleService` | Node state machine commands | `AppDbContext`, `INodeLifecycleStateMachine`, `NodeLifecycleLockRegistry` |
| `NodeManagementService` | Node registration workflow | `AppDbContext`, `IRegistrationDiffService` |
| `OperationService` | Async operation tracking and dispatch | `AppDbContext`, `IPublisher`, keyed `IOperationHandler` |
| `*ExportService` (Event, IncomingBatch, OutgoingBatch, Audit) | CSV/JSON export streams | `AppDbContext` |
| `DashboardQueryService` | Dashboard aggregation | `AppDbContext` |
| `*MetadataService` (Node, Trigger, Router, Channel, Parameter) | Metadata CRUD with cache | `AppDbContext`, `IMemoryCache` |

### MSOSync.App — Background Workers

| Worker | Responsibility | Schedule |
|---|---|---|
| `ExportJobWorker` | Process queued export jobs | Polling (5s) |
| `ExportCleanupWorker` | Expire/delete old export files | Polling |
| `WorkerStatusRegistry` | Singleton tick/status registry for all workers | — |

### MSOSync.Scheduler — Scheduled Jobs

| Worker | Responsibility | Schedule |
|---|---|---|
| `SyncJob` | Trigger events → outgoing batches | `SyncOptions.IntervalSeconds` |
| `PullJob` | Pull batches from peer nodes | `SyncOptions.PullIntervalSeconds` |
| `RetryJob` | Requeue failed batch sends | Fixed 5-minute cadence |
| `PurgeJob` | Delete old events/batches | Wall-clock 02:00 UTC daily |
| `SchedulerRecovery` | Recover stale Sending batches | Startup once |
| `HeartbeatWorker` | Send heartbeat to hub | `HeartbeatOptions.IntervalSeconds` |
| `ProbeWorker` | Probe child node connectivity | `HeartbeatOptions.ProbeIntervalSeconds` |
| `ConnectivityEvaluator` | Evaluate node status from heartbeats/probes | `HeartbeatOptions.ProbeIntervalSeconds` |
| `DecommissionWorker` | Finalize decommissioned nodes | Fixed cadence |

All workers register with `IWorkerStatusRegistry` (see
`docs/architecture/background-workers.md`); interval-driven workers read
their cadence from typed options (`SyncOptions`, `HeartbeatOptions`) —
see 2A.8/2A.9 backlog entries.
