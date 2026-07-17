# Epic 15B: Complete Domain TenantId Migration — Design Spec

**Date:** 2026-07-17
**Status:** Approved
**Depends on:** Epic 15A (multi-tenancy foundation — row-level isolation, EF filters, JWT claims, TenantResolver, M031)

---

## Goal

Migrate all 21 remaining `[TenantScoped]` entities to full `ITenantScoped` status — adding `TenantId NOT NULL` to their tables, activating EF Core global query filters, and auditing every service and background worker for correct tenant vs platform scope.

After 15B, all 33 tenant-scoped entities are fully migrated. The tenant migration is complete.

---

## Architecture

### What 15A Established (Do Not Repeat)

- Row-level isolation via EF Core global query filters on `ITenantScoped` entities
- `ICurrentTenantAccessor` (singleton, reads `IHttpContextAccessor` → `TenantContextHolder`)
- `IPlatformRepository<T>` — the ONLY class that may call `IgnoreQueryFilters()`
- `TenantRepository<T>` — base class for tenant-scoped repository access
- M031 migrated 12 core topology tables
- 21 entities marked `[TenantScoped]` but NOT yet implementing `ITenantScoped` (no `TenantId` column yet)

### What 15B Adds

1. All 21 deferred entities implement `ITenantScoped` + carry `Guid TenantId { get; set; }`
2. M032 migrates all 21 tables using the safe migration contract (see below)
3. EF Core global query filters activate automatically for all 21 entities (via `ApplyTenantFilters` in `AppDbContext`)
4. Services with cross-tenant read requirements are audited and updated to use `IPlatformRepository<T>`
5. Background workers are confirmed operating in platform context (no code changes required)

### Background Worker Auto-Platform-Context

Background workers (all `BackgroundService` / `IHostedService` implementations) operate outside HTTP request scope. `IHttpContextAccessor.HttpContext` returns null → `HttpContextCurrentTenantAccessor.TenantId` returns null → EF filter evaluates to `true` (passes all rows).

**Result:** Background workers automatically see all tenants' data. No `IPlatformRepository<T>` required for background workers. No code changes needed for: `DecommissionWorker`, `ConnectivityEvaluator`, `ProbeWorker`, `PurgeJob`, `SyncJob`, `PullJob`, `RetryJob`, `ExportJobWorker`, `ExportCleanupWorker`, `AdminBootstrapper`, `LifecycleStartupValidator`.

---

## Entity Groups

### Group 1: Node Management (4 entities)

| Entity | Table | Composite Index |
|--------|-------|----------------|
| SyncRegistrationRequest | sync_registration_request | `(TenantId, Status)` |
| SyncNodeBootstrapToken | sync_node_bootstrap_token | `(TenantId, NodeId)` |
| SyncNodeLifecycleHistory | sync_node_lifecycle_history | `(TenantId, NodeId)` |
| SyncNodeConnectivityHistory | sync_node_connectivity_history | `(TenantId, NodeId)` |

### Group 2: Synchronization Engine (5 entities)

| Entity | Table | Composite Index |
|--------|-------|----------------|
| SyncDataEvent | sync_data_event | `(TenantId, CreateTime DESC)` |
| SyncDataEventBatch | sync_data_event_batch | `(TenantId, Status)` |
| SyncOutgoingBatch | sync_outgoing_batch | `(TenantId, Status)` |
| SyncIncomingBatch | sync_incoming_batch | `(TenantId, Status)` |
| SyncBatchError | sync_batch_error | `(TenantId, CreateTime DESC)` |

> **Note:** Verify `SyncBatchError` has a `CreateTime` column; use the actual timestamp column name if different.

### Group 3: Configuration Management (5 entities)

| Entity | Table | Composite Index |
|--------|-------|----------------|
| SyncConfigurationTemplate | sync_configuration_template | `TenantId` alone |
| SyncConfigurationTemplateVersion | sync_configuration_template_version | `(TenantId, TemplateId)` |
| SyncNodeConfigurationOverride | sync_node_configuration_override | `(TenantId, NodeId)` |
| SyncNodeConfigurationHistory | sync_node_configuration_history | `(TenantId, NodeId)` |
| SyncConfigurationRollout | sync_configuration_rollout | `(TenantId, Status)` |

> **Note:** `SyncConfigurationTemplate` is a root aggregate in this group; a single-column `TenantId` index suffices because templates are most often fetched by name within the current tenant (the existing unique index on `Name` still applies per-tenant after migration).

### Group 4: Operations & Audit (3 entities)

| Entity | Table | Composite Index |
|--------|-------|----------------|
| SyncAudit | sync_audit | `(TenantId, CreateTime DESC)` |
| SyncOperation | sync_operation | `(TenantId, Status)` |
| SyncExportJob | sync_export_job | `(TenantId, Status)` |

**Admin views for SyncAudit are cross-tenant.** `AuditQueryService`, `CorrelationTimelineAssembler`, `AuditSummaryService`, and `ExportAuditService` must use `IPlatformRepository<SyncAudit>` for platform-admin reads. Split API:

```
Tenant audit API   → TenantRepository / EF filter (current tenant)
Platform audit API → IPlatformRepository<SyncAudit> (all tenants, platform-admin role required)
```

No runtime switching — the call site determines which repository to inject.

### Group 5: User & Runtime (4 entities)

| Entity | Table | Composite Index |
|--------|-------|----------------|
| SyncRuntimeStats | sync_runtime_stats | `(TenantId, CreateTime DESC)` |
| SyncNotification | sync_notification | `(TenantId, CreateTime DESC)` |
| SyncUserNotification | sync_user_notification | `(TenantId, UserId)` |
| SyncUserRefreshToken | sync_user_refresh_token | `(TenantId, UserId)` |

**SyncRuntimeStats** is per-node telemetry (heap, CPU, GC, uptime per `SyncNode`). Nodes are tenant-scoped → runtime stats are tenant-scoped. Confirmed.

**SyncUserRefreshToken** remains tenant-scoped. Refresh token flow:
- Login (unauthenticated) → middleware skips → `accessor.TenantId == null` → EF filter passes all rows → AuthenticationService issues token with `tenantId` claim
- Refresh (authenticated JWT with `tenantId` claim) → middleware sets tenant context → EF filter scopes to that tenant's tokens
- Logout → revokes current-tenant tokens only (correct multi-tenant semantics: logging out of TenantA does not revoke TenantB tokens)

No service changes required for `SyncUserRefreshToken`.

---

## Migration Contract: M032

**File:** `src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.cs`
**SQL script:** `.superpowers/apply-m032.sql`

### Safe Migration Pattern (per table)

```sql
-- Step 1: Add nullable column
ALTER TABLE [msosync].[<table>] ADD [tenant_id] uniqueidentifier NULL;

-- Step 2: Backfill all existing rows to SystemTenant
UPDATE [msosync].[<table>]
   SET [tenant_id] = '00000000-0000-0000-0000-000000000001'
 WHERE [tenant_id] IS NULL;

-- Step 3: Verify — must return 0 before proceeding
-- SELECT COUNT(*) FROM [msosync].[<table>] WHERE [tenant_id] IS NULL;

-- Step 4: Enforce NOT NULL
ALTER TABLE [msosync].[<table>] ALTER COLUMN [tenant_id] uniqueidentifier NOT NULL;

-- Step 5: Create nonclustered composite index
CREATE NONCLUSTERED INDEX [IX_<table>_<index_suffix>]
    ON [msosync].[<table>] ([tenant_id] ASC, [<second_column>] DESC/ASC);

-- Step 6: Add FK → Tenant
ALTER TABLE [msosync].[<table>]
    ADD CONSTRAINT [FK_<table>_tenant_id]
    FOREIGN KEY ([tenant_id])
    REFERENCES [msosync].[tenant]([tenant_id]);
```

> **Never add NOT NULL before backfilling.** Add as NULL, backfill, verify, then alter to NOT NULL.

### Index Naming Convention

```
IX_<table_name>_TenantId_<SecondColumnName>
```

Examples:
- `IX_sync_registration_request_TenantId_Status`
- `IX_sync_audit_TenantId_CreateTime`
- `IX_sync_outgoing_batch_TenantId_Status`

Single-column indexes (SyncConfigurationTemplate):
- `IX_sync_configuration_template_TenantId`

### FK Naming Convention

```
FK_<table_name>_tenant_id
```

All 21 tables get a FK to `[msosync].[tenant](tenant_id)`.

### `__EFMigrationsHistory` Tracking

Following project convention, the `.superpowers/apply-m032.sql` script inserts into `[__EFMigrationsHistory]` after all DDL completes:

```sql
INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES ('M032_DomainTenantIdMigration', '9.0.0');
```

---

## EF Configuration Pattern

For each entity configuration class, add inside `Configure()`:

```csharp
// Single-column index variant
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => e.TenantId)
    .HasDatabaseName("IX_<table>_TenantId");

// Composite index variant (most entities)
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => new { e.TenantId, e.Status })
    .HasDatabaseName("IX_<table>_TenantId_Status");
```

---

## Service Audit

### Services Requiring `IPlatformRepository<T>`

| Service | Entity | Reason |
|---------|--------|--------|
| `AuditQueryService` | `SyncAudit` | Platform-admin cross-tenant audit reads |
| `CorrelationTimelineAssembler` | `SyncAudit` | Timeline spans all tenants |
| `AuditSummaryService` | `SyncAudit` | Summary may span all tenants |
| `ExportAuditService` | `SyncAudit` | Export may include all tenant audit data |

All other services querying the 21 entities are either:
- **Already tenant-scoped** (EF filter handles them automatically), or
- **Background workers** (auto-platform-context via null `accessor.TenantId`)

### Services Confirmed No Change Required

| Service | Entity | Reason |
|---------|--------|--------|
| `AuthenticationService` | `SyncUserRefreshToken` | Refresh path is authenticated; filter scopes correctly |
| `NotificationService` | `SyncNotification` / `SyncUserNotification` | Tenant-scoped by design |
| `MetricsQueryService` | `SyncRuntimeStats` | Tenant-scoped reads for dashboard |
| `ExportJobService` | `SyncExportJob` | Background worker = auto-platform-context |
| All node lifecycle services | `SyncNodeLifecycleHistory`, etc. | Tenant-scoped per current request |

---

## Migration Acceptance Criteria (per entity)

Every migrated entity must satisfy all 6 checks before the epic is considered complete:

| Check | Verification |
|-------|-------------|
| ✅ Implements `ITenantScoped` | Entity class: `public class X : ITenantScoped` |
| ✅ `TenantId` NOT NULL in DB | `SELECT * FROM information_schema.columns WHERE column_name = 'tenant_id' AND is_nullable = 'NO'` |
| ✅ FK → `tenant` table | `sp_fkeys @fktable_name = '<table>'` shows FK |
| ✅ Composite index exists | `sp_helpindex '<table>'` shows index |
| ✅ EF global query filter active | `AppDbContext.OnModelCreating` calls `ApplyTenantFilters`; entity type implements `ITenantScoped` |
| ✅ Integration test exists | `CrossTenantIsolationTests` covers the entity |

---

## Testing Strategy

### Task 6 Integration Tests

**Isolation tests (per entity group)**
```
TenantA creates SyncAudit record
TenantB queries SyncAudit
→ Expected: TenantB sees 0 rows
```

**Platform repository tests**
```
PlatformRepository<SyncAudit>.QueryAll()
→ Expected: sees both TenantA and TenantB rows
```

**Background worker context test**
```
No HTTP context (simulated)
→ accessor.TenantId returns null
→ EF filter passes all rows
→ Expected: DecommissionWorker-style query sees all tenants' nodes
```

**Migration smoke test**
```
Apply M032 to a DB with pre-existing rows
→ Expected: all rows have tenant_id = SystemTenant
→ Expected: zero NULL rows after migration
```

**Query plan test (performance)**
```
INSERT 10k rows across 2 tenants for SyncAudit / SyncOutgoingBatch
Run filtered query on each entity
→ Expected: query plan uses the composite tenant index (not a scan)
```

---

## Definition of Done

15B is complete when:

1. All 33 tenant-scoped entities are fully migrated (12 from 15A + 21 from 15B)
2. Every tenant-scoped entity implements `ITenantScoped` with `Guid TenantId { get; set; }`
3. Every tenant-scoped entity has an active EF Core global query filter (no manual `WHERE TenantId = ?` predicates anywhere)
4. All platform-wide audit services use `IPlatformRepository<SyncAudit>`
5. CE upgrade path verified: existing rows → M032 → all backfilled to SystemTenant → zero NULL rows
6. Cross-tenant isolation integration tests pass for all 5 entity groups
7. Build: 0 errors, 0 warnings
8. All existing unit tests continue to pass

---

## Task Breakdown

| Task | Scope | Files |
|------|-------|-------|
| **Task 1** | Add `ITenantScoped` + `Guid TenantId` to 21 entities | 21 entity files in `MSOSync.Persistence/Entities/` |
| **Task 2** | Update 21 EF configurations (column + composite index) | 21 config files in `MSOSync.Persistence/Configurations/` |
| **Task 3** | Write M032 migration + apply `.superpowers/apply-m032.sql` | M032 migration class, apply SQL, `AppDbContextModelSnapshot.cs` |
| **Task 4** | Platform service migration: inject `IPlatformRepository<SyncAudit>` into 4 audit services | `AuditQueryService.cs`, `CorrelationTimelineAssembler.cs`, `AuditSummaryService.cs`, `ExportAuditService.cs` |
| **Task 5** | Tenant service verification: smoke-test `SyncUserRefreshToken`, `SyncRuntimeStats`, `SyncNotification` filter behavior | Integration smoke tests, no entity changes expected |
| **Task 6** | Integration tests: isolation, platform repo, background worker, migration smoke, query plan | New test class in `MSOSync.IntegrationTests/MultiTenancy/` |

---

## Global Constraints

- C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / SQL Server
- `IPlatformRepository<T>` internal implementation; public interface — only way to call `IgnoreQueryFilters()`
- `TenantId` is set once at INSERT, never updated; no repository accepts `TenantId` as a method parameter
- Manual SQL migrations via `.superpowers/*.sql` (not `dotnet ef database update`) with `__EFMigrationsHistory` tracking
- Migration contract order: NULL → backfill → verify zero NULL → NOT NULL → composite index → FK
- Index naming: `IX_<table>_TenantId_<SecondColumn>` (or `IX_<table>_TenantId` for single-column)
- FK naming: `FK_<table>_tenant_id` → references `[msosync].[tenant](tenant_id)`
- All work lands directly on `main` branch
- `TreatWarningsAsErrors` — build must remain 0 errors, 0 warnings
