# Epic 15A: Multi-Tenancy Foundation — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver a complete vertical slice of row-level multi-tenancy — Tenant entity, JWT tenant claim, EF Core global query filters, and core sync topology tenant-isolated — as the reference implementation for all remaining domain migrations.

**Architecture:** Single database, row-level isolation. Every Tenant Scoped entity carries a `TenantId` (Guid) column enforced by EF Core global query filters registered via `ITenantScoped` marker interface. `ITenantContext` (scoped per-request) is populated by `TenantResolverMiddleware` from JWT claims. `ICurrentTenantAccessor` (singleton) bridges the EF model cache boundary, reading from `IHttpContextAccessor` → `ITenantContext` at query time. CE is multi-tenant with one pre-seeded `SystemTenant`.

**Tech Stack:** C# 13 / .NET 9 / ASP.NET Core / EF Core 9 / SQL Server / xUnit / FluentAssertions

**Spec:** `docs/superpowers/specs/2026-07-16-epic15a-multi-tenancy-foundation-design.md`

## Global Constraints

- Target framework: net9.0
- Schema constant: `Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync"` — use in all migrations
- Table naming: `snake_case` with `sync_` prefix for sync entities; `tenant` and `tenant_membership` use no prefix
- Column naming: `snake_case` (e.g., `tenant_id`, `created_at_utc`)
- `SystemTenant` TenantId is the fixed well-known GUID `00000000-0000-0000-0000-000000000001` — hardcoded in `WellKnownTenantIds.SystemTenant`
- Migration naming: `M###_FeatureName` — next is `M030`, then `M031`
- `IgnoreQueryFilters()` permitted ONLY in `PlatformRepository<T>` implementations — never elsewhere
- Repositories NEVER accept `TenantId` as a method parameter — tenant always from `ITenantContext`
- Hybrid entities (`SyncRole`, `SyncUserRole`, `SyncParameter`, `SyncParameterHist`, `SyncUserPreference`) MUST NOT use EF global query filters
- Middleware order invariant: `UseAuthentication()` → `TenantResolverMiddleware` → `UseAuthorization()` → `MapControllers()`
- DO NOT run `dotnet ef migrations add` between Tasks 1–6 — property additions are staged for migration in Tasks 2 and 7
- All tests use `dotnet test` from repo root or specific project path

---

## Task Index

| Task | Deliverable | Key Files |
|------|-------------|-----------|
| [Task 1](2026-07-16-epic15a-task-1-abstractions.md) | Entity ownership abstractions + markers on all entities + gate test | `MSOSync.Common/Tenancy/`, 44 entity files |
| [Task 2](2026-07-16-epic15a-task-2-tenant-entities.md) | Tenant + TenantMembership entities + M030 migration + SystemTenantSeeder | `Entities/Tenant.cs`, `M030_MultiTenancyFoundation.cs` |
| [Task 3](2026-07-16-epic15a-task-3-tenant-context.md) | TenantContext, PlatformTenantContext, TenantAccessException + unit tests | `MSOSync.Security/Tenancy/` |
| [Task 4](2026-07-16-epic15a-task-4-resolver-validator.md) | ITenantResolver + TenantResolver + ITenantAccessValidator + unit tests | `MSOSync.Security/Tenancy/TenantResolver.cs` |
| [Task 5](2026-07-16-epic15a-task-5-middleware-jwt.md) | TenantResolverMiddleware + JWT tenantId claim + POST /auth/switch-tenant | `TenantResolverMiddleware.cs`, `JwtService.cs` |
| [Task 6](2026-07-16-epic15a-task-6-ef-infrastructure.md) | EF filter infra, TenantRepository, IPlatformRepository, IHybridLookupService | `AppDbContext.cs`, `TenantRepository.cs` |
| [Task 7](2026-07-16-epic15a-task-7-topology-migration.md) | M031: TenantId on 12 topology + nullable TenantId on 5 hybrid + backfill | `M031_CoreTopologyTenantId.cs`, 17 entity files |
| [Task 8](2026-07-16-epic15a-task-8-integration-tests.md) | 12 integration tests covering full tenant isolation end-to-end | `MSOSync.IntegrationTests/MultiTenancy/` |

---

## File Map

### New files — MSOSync.Common

```
src/MSOSync.Common/Tenancy/
  ITenantContext.cs
  ICurrentTenantAccessor.cs
  ITenantScoped.cs
  IHybridEntity.cs
  GlobalEntityAttribute.cs
  HybridEntityAttribute.cs
  TenantScopedAttribute.cs
  WellKnownTenantIds.cs
  EditionType.cs
```

### New files — MSOSync.Persistence

```
src/MSOSync.Persistence/Entities/Tenant.cs
src/MSOSync.Persistence/Entities/TenantMembership.cs
src/MSOSync.Persistence/Configurations/TenantConfiguration.cs
src/MSOSync.Persistence/Configurations/TenantMembershipConfiguration.cs
src/MSOSync.Persistence/Migrations/M030_MultiTenancyFoundation.cs
src/MSOSync.Persistence/Migrations/M031_CoreTopologyTenantId.cs
src/MSOSync.Persistence/Tenancy/ModelBuilderTenantExtensions.cs
src/MSOSync.Persistence/Tenancy/TenantRepository.cs
src/MSOSync.Persistence/Tenancy/PlatformRepository.cs
src/MSOSync.Persistence/Tenancy/IHybridLookupService.cs
src/MSOSync.Persistence/Tenancy/HybridLookupService.cs
src/MSOSync.Persistence/Tenancy/HttpContextCurrentTenantAccessor.cs
```

### New files — MSOSync.Security

```
src/MSOSync.Security/Tenancy/TenantContext.cs
src/MSOSync.Security/Tenancy/PlatformTenantContext.cs
src/MSOSync.Security/Tenancy/TenantAccessException.cs
src/MSOSync.Security/Tenancy/ITenantResolver.cs
src/MSOSync.Security/Tenancy/TenantResolver.cs
src/MSOSync.Security/Tenancy/ITenantAccessValidator.cs
src/MSOSync.Security/Tenancy/TenantAccessValidator.cs
src/MSOSync.Security/Tenancy/TenantResolverMiddleware.cs
```

### Modified files

```
src/MSOSync.Persistence/AppDbContext.cs            — inject ICurrentTenantAccessor, call ApplyTenantFilters
src/MSOSync.Security/JwtService.cs                 — add tenantId param to CreateAccessToken
src/MSOSync.App/Program.cs                         — register DI, add TenantResolverMiddleware
src/MSOSync.Api/Controllers/AuthController.cs      — pass tenantId to CreateAccessToken, add switch-tenant
44 entity files                                     — add ownership attributes / ITenantScoped / IHybridEntity
12 topology EF configs                              — add TenantId column configuration
```

### New test files

```
tests/MSOSync.Tests/Tenancy/EntityOwnershipGateTests.cs
tests/MSOSync.SecurityTests/Tenancy/TenantContextTests.cs
tests/MSOSync.SecurityTests/Tenancy/TenantResolverTests.cs
tests/MSOSync.SecurityTests/Tenancy/TenantAccessValidatorTests.cs
tests/MSOSync.Tests/Tenancy/HybridLookupServiceTests.cs
tests/MSOSync.IntegrationTests/MultiTenancy/CrossTenantIsolationTests.cs
tests/MSOSync.IntegrationTests/MultiTenancy/TenantAuthFlowTests.cs
tests/MSOSync.IntegrationTests/MultiTenancy/HybridEntityTests.cs
tests/MSOSync.IntegrationTests/MultiTenancy/SystemTenantSeederTests.cs
```
