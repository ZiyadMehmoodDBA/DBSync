# Epic 15B: Complete Domain TenantId Migration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate all 21 remaining `[TenantScoped]` entities to full `ITenantScoped` status, activate EF Core global query filters on them, and update audit services to use `IPlatformRepository<SyncAudit>` for cross-tenant admin reads.

**Architecture:** 15A established the full pattern (EF global query filters via `ApplyTenantFilters`, `IPlatformRepository<T>`, `MutableTenantAccessor` for tests, manual SQL migrations via `.superpowers/*.sql`). 15B applies that pattern mechanically to the remaining 21 entities. After 15B all 33 tenant-scoped entities are fully migrated and the domain TenantId migration is complete.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / SQL Server (`msosync` schema for most entities, `dbo` for `SyncExportJob`)

## Global Constraints

- C# 13 / .NET 9 / ASP.NET Core / EF Core 9 — `TreatWarningsAsErrors` — build must stay 0 errors, 0 warnings
- All 21 entities already carry `[TenantScoped]` attribute — **do not remove it, add `ITenantScoped` interface alongside**
- `ITenantScoped` interface lives in `MSOSync.Common/Tenancy/ITenantScoped.cs` — `Guid TenantId { get; set; }`
- `IPlatformRepository<T>` lives in `MSOSync.Persistence/Tenancy/PlatformRepository.cs` — public interface, internal sealed implementation
- Migration contract: `ADD NULL → backfill SystemTenant → verify zero NULLs → ALTER NOT NULL → CREATE INDEX → ADD FK`
- SystemTenant GUID = `00000000-0000-0000-0000-000000000001` (`WellKnownTenantIds.SystemTenant`)
- Manual SQL migrations only — **never run `dotnet ef database update`** — apply via `.superpowers/apply-m032.sql` and insert into `__EFMigrationsHistory`
- Index naming: `IX_<table>_TenantId_<SecondColumn>` (composite) or `IX_<table>_TenantId` (single)
- FK naming: `FK_<table>_tenant_id` → references `[msosync].[tenant](tenant_id)`
- `SyncExportJob` is in `dbo` schema (no schema prefix) — use `dbo` for its migration DDL
- All work lands on `main` branch

## Entity Reference Table

The 21 entities to migrate, with their DB table names, schemas, and composite index design:

| # | Entity | Schema | Table | Composite Index 2nd Column |
|---|--------|--------|-------|---------------------------|
| 1 | SyncRegistrationRequest | msosync | sync_registration_request | `registration_status` |
| 2 | SyncNodeBootstrapToken | msosync | sync_node_bootstrap_token | `node_id` |
| 3 | SyncNodeLifecycleHistory | msosync | sync_node_lifecycle_history | `node_id` |
| 4 | SyncNodeConnectivityHistory | msosync | sync_node_connectivity_history | `node_id` |
| 5 | SyncDataEvent | msosync | sync_data_event | `create_time DESC` |
| 6 | SyncDataEventBatch | msosync | sync_data_event_batch | `batch_id` |
| 7 | SyncOutgoingBatch | msosync | sync_outgoing_batch | `status` |
| 8 | SyncIncomingBatch | msosync | sync_incoming_batch | `status` |
| 9 | SyncBatchError | msosync | sync_batch_error | `create_time DESC` |
| 10 | SyncConfigurationTemplate | msosync | sync_configuration_template | *(single-column; unique constraint migrated)* |
| 11 | SyncConfigurationTemplateVersion | msosync | sync_configuration_template_version | `template_id` |
| 12 | SyncNodeConfigurationOverride | msosync | sync_node_configuration_override | `node_id` |
| 13 | SyncNodeConfigurationHistory | msosync | sync_node_configuration_history | `node_id` |
| 14 | SyncConfigurationRollout | msosync | sync_configuration_rollout | `status` |
| 15 | SyncRuntimeStats | msosync | sync_runtime_stats | `create_time DESC` |
| 16 | SyncAudit | msosync | sync_audit | `create_time DESC` |
| 17 | SyncOperation | msosync | sync_operation | `status` |
| 18 | SyncExportJob | **dbo** | sync_export_job | `status` |
| 19 | SyncNotification | msosync | sync_notification | `create_time DESC` |
| 20 | SyncUserNotification | msosync | sync_user_notification | `user_id` |
| 21 | SyncUserRefreshToken | msosync | sync_user_refresh_token | `user_id` |

> **SyncConfigurationTemplate** unique constraint `UX_sync_configuration_template_name` must be converted to composite `UX_sync_configuration_template_tenant_id_name` as part of M032.

---

## Tasks

| Task | What it delivers | Brief file |
|------|-----------------|-----------|
| **Task 1** | `ITenantScoped` + `Guid TenantId` on 21 entities | [task-1-entity-interfaces.md](2026-07-17-epic15b-task-1-entity-interfaces.md) |
| **Task 2** | 21 EF configs: `tenant_id` column + composite index | [task-2-ef-configurations.md](2026-07-17-epic15b-task-2-ef-configurations.md) |
| **Task 3** | M032 migration (NULL→backfill→NOT NULL→FK) + apply | [task-3-migration.md](2026-07-17-epic15b-task-3-migration.md) |
| **Task 4** | Platform service migration — inject `IPlatformRepository<SyncAudit>` into 4 audit services | [task-4-platform-services.md](2026-07-17-epic15b-task-4-platform-services.md) |
| **Task 5** | Tenant service verification — confirm EF filter correct on SyncUserRefreshToken, SyncRuntimeStats, SyncNotification | [task-5-verification.md](2026-07-17-epic15b-task-5-verification.md) |
| **Task 6** | Integration tests: isolation, platform repo, migration smoke, query plans | [task-6-integration-tests.md](2026-07-17-epic15b-task-6-integration-tests.md) |
