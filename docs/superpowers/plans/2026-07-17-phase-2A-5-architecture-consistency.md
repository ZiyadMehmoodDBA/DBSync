# Phase 2A.5 — Architecture Consistency

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Document the service responsibility map. The audit found no architectural violations — all services have single, clear responsibilities and dependencies flow in the correct direction. This plan commits that map as a Phase 2A deliverable.

**Architecture:** MSOSync uses a layered architecture: `MSOSync.Api` (controllers, middleware) → `MSOSync.Metadata` (services, domain logic) → `MSOSync.Persistence` (EF Core, AppDbContext) → SQL Server. Cross-cutting: `MSOSync.Common` (interfaces, IClock, ICurrentUserService), `MSOSync.Security` (auth/tenancy), `MSOSync.App` (workers, export, admin bootstrap). Scheduler lives in `MSOSync.Scheduler` — its jobs depend on `MSOSync.Metadata` services + `MSOSync.Engine` for apply operations.

**Tech Stack:** C# 13 / .NET 9

## Global Constraints

- No new product features. Scope is strictly documentation.
- Definition of Complete: docs committed + `dotnet test` exits 0.
- RULE-ARCH-1: Services in `MSOSync.Api` must not depend on services from `MSOSync.Scheduler` or `MSOSync.Engine`.
- RULE-ARCH-2: `MSOSync.Metadata` services must not depend on `MSOSync.Api` types.
- RULE-ARCH-3: Persistence access (AppDbContext) only in `MSOSync.Metadata`, `MSOSync.Persistence`, and `MSOSync.Security`.

---

## File Map

**Create:**
- `docs/architecture/service-responsibility-map.md`

---

## Task 1: Write Service Responsibility Map

**Files:**
- Create: `docs/architecture/service-responsibility-map.md`

- [ ] **Step 1: Confirm layer dependency scan**

```powershell
# Verify no Api → Scheduler dependency
grep -rn "MSOSync.Scheduler" D:\MSOSync\src\MSOSync.Api\ --include="*.cs"
```

Expected: No matches.

```powershell
# Verify no Metadata → Api dependency
grep -rn "MSOSync.Api" D:\MSOSync\src\MSOSync.Metadata\ --include="*.cs"
```

Expected: No matches.

```powershell
# Verify AppDbContext not used in Api layer directly
grep -rn "AppDbContext" D:\MSOSync\src\MSOSync.Api\ --include="*.cs"
```

Expected: No matches (Api controllers inject service interfaces only).

- [ ] **Step 2: Create service-responsibility-map.md**

Create `docs/architecture/service-responsibility-map.md`:

```markdown
# Service Responsibility Map

## Layer Overview

```
MSOSync.Api                   — HTTP controllers, middleware, auth, OpenAPI
    ↓ depends on
MSOSync.Metadata              — Domain services, query services, business logic
    ↓ depends on
MSOSync.Persistence           — EF Core entities, AppDbContext, migrations
    ↓ depends on
SQL Server                    — Data store

Cross-cutting (any layer may depend on these):
MSOSync.Common                — IClock, ICurrentUserService, IWorkerStatusRegistry, ISystemHealthService
MSOSync.Security              — IUserService, ITenantResolver, JWT, BCrypt
MSOSync.Engine                — IApplyService, ISqlConnectionFactory (Scheduler only)
MSOSync.Transport             — INodeHttpClient (Scheduler + Metadata only)
MSOSync.Batch                 — IBatchCreator, IBatchStateMachine, IBatchTransportQueryService
MSOSync.Plugin                — IPluginServices, IPluginManager (App layer only)
```

## Rules

- **RULE-ARCH-1:** `MSOSync.Api` must not reference `MSOSync.Scheduler` or `MSOSync.Engine`.
- **RULE-ARCH-2:** `MSOSync.Metadata` must not reference `MSOSync.Api`.
- **RULE-ARCH-3:** `AppDbContext` only in `MSOSync.Metadata`, `MSOSync.Persistence`, `MSOSync.Security`, `MSOSync.App` (export workers).

## Service Catalog

### MSOSync.Api (Controllers)

| Controller | Responsibility | Key Dependencies |
|---|---|---|
| `AuthController` | Login, logout, token refresh | `IUserService` |
| `SyncController` | Pull/push/ack for batch transport | `IBatchTransportQueryService`, `IApplyService`, `IBatchStateMachine`, `ITopologyService`, `INodeAuthorizationService` |
| `NodesController` | Node CRUD, lifecycle commands | `INodeManagementService`, `INodeLifecycleService`, `INodeAuthorizationService` |
| `ChannelsController` | Channel definitions CRUD | `IChannelMetadataService` |
| `EventsController` | Paginated event reads | `IEventQueryService` |
| `IncomingBatchesController` | Paginated batch reads | `IIncomingBatchQueryService` |
| `DashboardController` | Dashboard aggregation | `IDashboardQueryService` |
| `AuditController` | Audit log reads, lock admin | `IAuditQueryService`, `ILockAdminService` |
| `MetricsController` | System metrics | `IMetricsQueryService` |
| `TopologyController` | Hub/child graph | `ITopologyQueryService` |
| `OperationsController` | Async operation tracking | `IOperationService`, `IOperationQueryService` |
| `PermissionsController` | Role/permission management | `IPermissionService` |
| `UsersController` | User management | `IUsersManagementService` |
| `PreferencesController` | User preferences | `IUserPreferencesService` |
| `ExportJobController` | Export job lifecycle | `IExportJobService`, `IPermissionService` |
| `ConfigurationController` | Node config, assignments, rollouts | `INodeConfigurationService`, `IConfigurationAssignmentService`, `IRolloutService` |
| `SystemController` | Health, overview, admin | `ISystemHealthService`, `IOverviewQueryService` |

### MSOSync.Metadata (Domain Services)

| Service | Responsibility | Key Dependencies |
|---|---|---|
| `AuditService` | Write audit records | `AppDbContext` |
| `AuditQueryService` | Read audit records with filters | `AppDbContext` |
| `PermissionService` | Resolve effective permissions | `AppDbContext`, `IMemoryCache`, `IMediator` |
| `UserPreferencesService` | Get/set user preferences | `AppDbContext`, `ICurrentUserService` |
| `TopologyQueryService` | Hub/child topology graph | `AppDbContext`, `IMemoryCache` |
| `UsersManagementService` | User CRUD, role assignment | `AppDbContext`, `IUserService` |
| `NodeLifecycleService` | Node state machine commands | `AppDbContext`, `IMediator`, `ICurrentUserService` |
| `NodeManagementService` | Node CRUD and registration | `AppDbContext` |
| `NodeConfigurationService` | Effective config computation | `AppDbContext`, `IEffectiveConfigurationComputer` |
| `ConfigurationAssignmentService` | Template assignment workflow | `AppDbContext`, `IMediator` |
| `RolloutService` | Configuration rollout orchestration | `AppDbContext`, `IMediator` |
| `OperationService` | Async operation tracking and dispatch | `AppDbContext`, `IPublisher`, `IServiceProvider` (keyed handlers) |
| `EventExportService` | Export events to CSV/JSON | `AppDbContext` |
| `AuditExportService` | Export audit to CSV/JSON | `AppDbContext` |
| `NotificationService` | Node notification writes | `AppDbContext`, `IPublisher` |
| `ChannelMetadataService` | Channel definitions with cache | `AppDbContext`, `IMemoryCache` |
| `NodeMetadataService` | Node definitions with warm lookup | `AppDbContext`, `IMemoryCache`, `IHybridLookupService` |

### MSOSync.App (Background Workers)

| Worker | Responsibility | Schedule |
|---|---|---|
| `ExportJobWorker` | Process queued export jobs | Polling (5s) |
| `ExportCleanupWorker` | Expire/delete old export files | Polling (30s) |

### MSOSync.Scheduler (Scheduler Workers)

| Worker | Responsibility | Schedule |
|---|---|---|
| `SyncJob` | Trigger → outgoing batch apply | `IOptions<SyncOptions>.IntervalSeconds` |
| `PullJob` | Pull batches from child nodes | `IOptions<SyncOptions>.PullIntervalSeconds` |
| `RetryJob` | Requeue failed batch sends | Fixed 5-minute cadence |
| `PurgeJob` | Delete old events/batches | Wall-clock 02:00 UTC daily |
| `HeartbeatWorker` | Send heartbeat to hub | `IOptions<HeartbeatOptions>.IntervalSeconds` |
| `ProbeWorker` | Probe child node connectivity | `IOptions<HeartbeatOptions>.ProbeIntervalSeconds` |
| `ConnectivityEvaluator` | Evaluate child status from probe results | Same period as ProbeWorker |
| `DecommissionWorker` | Finalize decommissioned nodes | 30s cadence |
| `SchedulerRecovery` | On startup: recover stale Sending batches | Startup once |

## Dependency Flow Invariants

These invariants are verified by `MSOSync.ArchTests.DependencyTests`:

1. `Api` does not reference `Scheduler` or `Engine` namespaces.
2. `Metadata` does not reference `Api` namespaces.
3. `Persistence` does not reference `Metadata`, `Api`, or `Scheduler`.
4. `Common` does not reference any MSOSync project (pure abstractions only).
```

- [ ] **Step 3: Run all tests**

```
dotnet test D:\MSOSync\MSOSync.sln -v n
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add docs/architecture/service-responsibility-map.md
git commit -m "docs(2A.5): service responsibility map"
```

---

## Completion Criteria

2A.5 is **Complete** when:
1. Layer dependency scan returns no violations (no Api→Scheduler, no Metadata→Api, no Api→AppDbContext direct).
2. `dotnet test` exits 0.
3. `docs/architecture/service-responsibility-map.md` committed with accurate service catalog.
