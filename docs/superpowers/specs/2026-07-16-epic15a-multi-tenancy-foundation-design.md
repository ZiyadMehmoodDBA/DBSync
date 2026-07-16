# Epic 15A: Multi-Tenancy Foundation — Design Spec

**Date:** 2026-07-16
**Status:** Approved
**Scope:** Foundation + Core Domain (Approach A)

---

## Goal

Deliver a complete vertical slice of multi-tenancy that proves the full pattern end-to-end. Every remaining domain migration (15B–15E and beyond) becomes a mechanical repetition of what 15A establishes.

> **15A establishes the multi-tenant reference architecture. It does not complete the multi-tenant migration.**

---

## Architecture

### Isolation Model

Row-level isolation. Every Tenant Scoped entity carries a `TenantId` (Guid) column. EF Core global query filters enforce tenant visibility at the ORM layer — no application code manually adds `WHERE TenantId = ?` predicates.

### CE Compatibility

Community Edition = exactly one active `SystemTenant`. No code branching between single-tenant and multi-tenant. CE is multi-tenant with one tenant. The `SystemTenantSeeder` runs at migration time to seed `SystemTenant` and backfill all existing rows.

**Invariant:** CE must never allow deleting `SystemTenant` or creating a second tenant. Enterprise removes those restrictions without requiring code changes.

### TenantId Immutability

`TenantId` is set once at INSERT and never updated. Moving data between tenants requires:

```
Copy → Validate → Delete original
```

`UPDATE ... SET TenantId = ...` is prohibited. No repository accepts `TenantId` as a method parameter — tenant always comes from `ITenantContext`.

---

## Entity Ownership Classification

Every persisted business entity declares exactly one ownership category. This is an architectural invariant enforced by a reflection test in CI.

### Tenant Scoped (33 existing + 1 new)

Receive `TenantId` column. Covered by EF Core global query filter.

| Entity | Table |
|--------|-------|
| SyncNode | Nodes |
| SyncNodeLifecycleHistory | NodeLifecycleHistories |
| SyncNodeConnectivityHistory | NodeConnectivityHistories |
| SyncNodeBootstrapToken | NodeBootstrapTokens |
| SyncNodeGroup | NodeGroups |
| SyncNodeSecurity | NodeSecurities |
| SyncRegistrationRequest | RegistrationRequests |
| SyncNodeScope | NodeScopes |
| SyncNodeChannelAssignment | NodeChannelAssignments |
| SyncNodeTriggerAssignment | NodeTriggerAssignments |
| SyncNodeRouterAssignment | NodeRouterAssignments |
| SyncChannel | Channels |
| SyncTrigger | Triggers |
| SyncTriggerHist | TriggerHists |
| SyncRouter | Routers |
| SyncTriggerRouter | TriggerRouters |
| SyncDataEvent | DataEvents |
| SyncDataEventBatch | DataEventBatches |
| SyncOutgoingBatch | OutgoingBatches |
| SyncIncomingBatch | IncomingBatches |
| SyncBatchError | BatchErrors |
| SyncConfigurationTemplate | ConfigurationTemplates |
| SyncConfigurationTemplateVersion | ConfigurationTemplateVersions |
| SyncNodeConfigurationOverride | NodeConfigurationOverrides |
| SyncNodeConfigurationHistory | NodeConfigurationHistories |
| SyncConfigurationRollout | ConfigurationRollouts |
| SyncRuntimeStats | RuntimeStats |
| SyncAudit | Audits |
| SyncOperation | Operations |
| SyncExportJob | ExportJobs |
| SyncNotification | Notifications |
| SyncUserNotification | UserNotifications |
| SyncUserRefreshToken | UserRefreshTokens |
| **SyncMonitorRule** *(new)* | MonitorRules |

**15A migrates only the core sync topology domain as the reference implementation:**
Nodes, NodeGroups, NodeSecurities, NodeScopes, NodeChannelAssignments, NodeTriggerAssignments, NodeRouterAssignments, Channels, Triggers, TriggerRouters, Routers.
All other Tenant Scoped entities migrate in subsequent epics following the same pattern.

### Global (5)

No `TenantId`. No query filter. Platform-owned.

| Entity | Table | Notes |
|--------|-------|-------|
| SyncPermission | Permissions | System-wide permission catalog |
| SyncRolePermission | RolePermissions | System role → permission mappings |
| SyncPlugin | Plugins | Platform-level install; per-tenant enablement is future (`TenantPlugin`) |
| SyncLock | Locks | Operational infrastructure; add `LockScope` enum (Platform \| Tenant) instead of TenantId |
| SyncMonitor → **SyncMonitorSnapshot** | MonitorSnapshots | Host monitoring telemetry (CPU, memory, throughput, queue depth) |

### Hybrid (6)

Nullable `TenantId`. No EF Core global query filter. Explicit fallback resolution via `IHybridLookupService`: tenant-specific record → platform (NULL TenantId) record → null.

| Entity | Table | Nullable TenantId meaning |
|--------|-------|--------------------------|
| SyncUser | Users | Platform identity; tenant scoping via `TenantMembership` junction |
| SyncRole | Roles | NULL = system role; non-null = tenant custom role |
| SyncUserRole | UserRoles | NULL = platform role assignment; non-null = tenant role assignment |
| SyncParameter | Parameters | NULL = platform setting; non-null = tenant override |
| SyncParameterHist | ParameterHists | Mirrors Parameter |
| SyncUserPreference | UserPreferences | NULL = user global preference; non-null = tenant-specific override |

**Lookup algorithm (uniform across all Hybrid entities):**
```
GetAsync(tenantId, key)
  → tenant record exists? → return tenant value
  → platform (NULL) record exists? → return platform value
  → return null
```

### New Entities

| Entity | Table | Category |
|--------|-------|----------|
| Tenant | Tenants | — (root) |
| TenantMembership | TenantMemberships | — (junction) |
| SyncMonitorRule | MonitorRules | Tenant Scoped (new concept, split from SyncMonitor) |

---

## New Entity Schemas

### Tenant

```sql
CREATE TABLE Tenants (
    TenantId        uniqueidentifier    NOT NULL PRIMARY KEY,
    Name            nvarchar(200)       NOT NULL,
    Slug            nvarchar(100)       NOT NULL UNIQUE,   -- lowercase, immutable after creation
    Status          int                 NOT NULL,          -- TenantStatus enum
    Edition         int                 NOT NULL,          -- EditionType enum
    LicenseId       uniqueidentifier    NULL,              -- FK to future License entity (15B)
    CreatedAtUtc    datetimeoffset      NOT NULL,
    UpdatedAtUtc    datetimeoffset      NOT NULL,
    SuspendedAtUtc  datetimeoffset      NULL,
    DeletedAtUtc    datetimeoffset      NULL,
    RowVersion      rowversion          NOT NULL
)
```

**C# entity:**
```csharp
public class Tenant
{
    public Guid            TenantId       { get; set; }
    public string          Name           { get; set; } = "";
    public string          Slug           { get; set; } = "";   // immutable after create
    public TenantStatus    Status         { get; set; }
    public EditionType     Edition        { get; set; }
    public Guid?           LicenseId      { get; set; }
    public DateTimeOffset  CreatedAtUtc   { get; set; }
    public DateTimeOffset  UpdatedAtUtc   { get; set; }
    public DateTimeOffset? SuspendedAtUtc { get; set; }
    public DateTimeOffset? DeletedAtUtc   { get; set; }
    public byte[]          RowVersion     { get; set; } = [];
}

public enum TenantStatus  { Provisioning, Active, Suspended, Deleted }
public enum EditionType   { Community, Enterprise }
```

**Slug rules:** lowercase, URL-safe characters only (`[a-z0-9-]`), 3–100 chars, unique, immutable after creation. Normalize (trim + lowercase) before persistence.

### TenantMembership

```sql
CREATE TABLE TenantMemberships (
    TenantId        uniqueidentifier    NOT NULL REFERENCES Tenants(TenantId),
    UserId          bigint              NOT NULL REFERENCES Users(UserId),
    RoleId          bigint              NOT NULL REFERENCES Roles(RoleId),
    Status          int                 NOT NULL,          -- MemberStatus enum
    JoinedAt        datetimeoffset      NOT NULL,
    LastAccessedAt  datetimeoffset      NOT NULL,
    RowVersion      rowversion          NOT NULL,
    PRIMARY KEY (TenantId, UserId)
)
```

**C# entity:**
```csharp
public class TenantMembership
{
    public Guid           TenantId       { get; set; }
    public long           UserId         { get; set; }
    public long           RoleId         { get; set; }
    public MemberStatus   Status         { get; set; }
    public DateTimeOffset JoinedAt       { get; set; }
    public DateTimeOffset LastAccessedAt { get; set; }
    public byte[]         RowVersion     { get; set; } = [];
}

public enum MemberStatus { Active, Suspended }
```

---

## Components

### ITenantContext

Scoped per-request. Populated by `TenantResolverMiddleware`. Nothing reads JWT claims directly.

```csharp
public interface ITenantContext
{
    Guid        TenantId          { get; }
    string      TenantSlug        { get; }
    EditionType Edition           { get; }
    long?       UserId            { get; }   // null for node tokens and platform tokens
    long?       RoleId            { get; }   // from TenantMembership.RoleId
    bool        IsPlatformContext { get; }
}
```

`IsPlatformContext = true` for platform admin tokens — replaces any `Guid.Empty` sentinel check. When `IsPlatformContext = true`, `TenantId = Guid.Empty` and `RoleId = null`.

### ITenantResolver

Sole component that constructs `ITenantContext` from `HttpContext`. Resolution priority (first match wins):

1. **Platform token** — `tenantId` claim absent or null → `PlatformTenantContext` (`IsPlatformContext = true`)
2. **Node token** — `nodeId` claim present + `tenantId` claim present:
   - Load `SyncNode` via `IPlatformRepository` (bypasses filter)
   - If `node.TenantId ≠ JWT tenantId claim` → **403** (forged/stale token)
   - Match → build `TenantContext` from node's `TenantId`, `UserId = null`
3. **User JWT** — `tenantId` + `userId` claims present → call `ITenantAccessValidator`
4. **No token** → **401**

```csharp
public interface ITenantResolver
{
    Task<ITenantContext> ResolveAsync(HttpContext ctx, CancellationToken ct);
}
```

### ITenantAccessValidator

Single place to evaluate membership validity. 15B plugs license checks into this same interface.

```csharp
public interface ITenantAccessValidator
{
    // Throws TenantAccessException (mapped to 403/409) on any violation
    Task ValidateAsync(Guid tenantId, long userId, CancellationToken ct);
}
```

Checks (in order):
1. `TenantMembership` exists → 403 if missing
2. `TenantMembership.Status == Active` → 403 if suspended
3. `Tenant.Status == Active` → 409 if `Provisioning` or `Suspended`
4. *(15B)* License valid → 402 if expired

### TenantResolverMiddleware

Runs after `UseAuthentication()`, before `UseAuthorization()`. Calls `ITenantResolver`, registers resolved `ITenantContext` as scoped DI, writes `IsPlatformContext` to `HttpContext.Items` for downstream policy checks.

**Middleware order (invariant):**
```csharp
app.UseAuthentication();
app.UseMiddleware<TenantResolverMiddleware>();
app.UseAuthorization();
app.MapControllers();
```

### ITenantScoped (marker interface)

```csharp
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
```

All 33 existing + 1 new Tenant Scoped entities implement this. `ApplyTenantFilters()` extension on `ModelBuilder` auto-registers `HasQueryFilter` for every entity implementing `ITenantScoped`:

```csharp
// In AppDbContext.OnModelCreating:
builder.ApplyTenantFilters(_tenantContext);

// Extension — discovers via reflection, one filter per ITenantScoped entity:
static void ApplyTenantFilters(this ModelBuilder b, ITenantContext ctx)
{
    foreach (var type in b.Model.GetEntityTypes()
        .Where(t => t.ClrType.IsAssignableTo(typeof(ITenantScoped))))
    {
        // Builds: e => ((ITenantScoped)e).TenantId == ctx.TenantId
        b.Entity(type.ClrType).HasQueryFilter(...);
    }
}
```

**Hybrid entities MUST NOT use `HasQueryFilter`.** They use `IHybridLookupService` exclusively.

### TenantRepository\<T\> + IPlatformRepository\<T\>

```csharp
// Base for all tenant-scoped repositories — filter active, no TenantId parameter accepted
public abstract class TenantRepository<T> where T : class, ITenantScoped { }

// Sole component permitted to call IgnoreQueryFilters() — internal, not injectable by app services
internal interface IPlatformRepository<T> where T : class { }
```

`IgnoreQueryFilters()` is banned everywhere except `IPlatformRepository<T>` implementations. Code review enforces this via search: no occurrences of `IgnoreQueryFilters` outside `Platform*Repository` files permitted.

### IHybridLookupService

```csharp
public interface IHybridLookupService
{
    Task<T?>                  GetAsync<T>(Guid tenantId, string key, CancellationToken ct)    where T : class, IHybridEntity;
    Task<IReadOnlyList<T>>    GetAllAsync<T>(Guid tenantId, CancellationToken ct)              where T : class, IHybridEntity;
    Task<bool>                ExistsAsync<T>(Guid tenantId, string key, CancellationToken ct) where T : class, IHybridEntity;
}
```

Lookup always: tenant-specific record first, fall back to NULL-TenantId record. One implementation — no per-repository fallback logic.

### SystemTenantSeeder

Runs at migration time (not application startup). Idempotent.

1. If `Tenants` table empty → insert `SystemTenant` with fixed well-known `TenantId` (constant in codebase)
2. For each core topology table migrated in 15A: `UPDATE SET TenantId = @SystemTenantId WHERE TenantId IS NULL`
3. Application startup validates `SystemTenant` exists — fatal error if missing in CE mode

---

## Data Flow

### Happy path — authenticated user request

```
UseAuthentication()              validates JWT, populates ClaimsPrincipal
TenantResolverMiddleware         calls ITenantResolver:
  reads tenantId + userId claims
  calls ITenantAccessValidator → 403/409 on violation
  builds TenantContext, registers scoped ITenantContext
UseAuthorization()               reads ITenantContext.RoleId for policy checks
Controller → Service             calls TenantRepository<T> (no TenantId param)
TenantRepository<T>              _dbContext.Set<T>()
                                 query filter: WHERE TenantId = @tenantId (auto-applied)
SQL Server                       returns only this tenant's rows
```

### Cross-tenant isolation — invisible by design

```
TenantA JWT → request TenantB NodeId
→ query filter: WHERE TenantId = TenantA_Id
→ row not found → 404
No information leakage. TenantB resource existence not revealed.
```

### Platform admin path

```
Platform token (tenantId claim absent)
→ PlatformTenantContext (IsPlatformContext = true)
→ Authorization: platform-admin policy required
→ IPlatformRepository<T>.IgnoreQueryFilters() — all tenant rows visible
```

### Hybrid entity path

```
Service calls IHybridLookupService.GetAsync(tenantId, key)
→ query: SELECT WHERE TenantId = @tenantId AND Key = @key
→ found → return tenant value
→ not found → query: SELECT WHERE TenantId IS NULL AND Key = @key
→ found → return platform default
→ not found → return null
```

---

## Error Handling

| Code | Trigger |
|------|---------|
| 401 | No token / invalid signature / expired |
| 403 | Valid token but: membership missing, membership suspended, node TenantId mismatch, platform token on tenant endpoint |
| 404 | Resource exists in another tenant (query filter — invisible isolation) |
| 409 | Tenant in `Provisioning` or `Suspended` state |
| 423 | Tenant locked — maintenance mode (future) |

---

## Testing Strategy

### Unit tests (`MSOSync.Tests`)

| Subject | Cases |
|---------|-------|
| `TenantResolver` | Platform token → `IsPlatformContext = true`; valid user JWT + membership → context built; user JWT missing membership → 403; node token TenantId match → context built; node token TenantId mismatch → 403; no token → 401 |
| `TenantAccessValidator` | Membership suspended → 403; Tenant suspended → 409; Tenant provisioning → 409; all valid → passes |
| `HybridLookupService` | Tenant record exists → returns tenant value; no tenant record, platform record exists → returns platform value; neither → null |

### Integration tests (`MSOSync.IntegrationTests`)

| Test | Verifies |
|------|---------|
| `CrossTenantIsolation_Node_Returns404` | TenantA JWT, request TenantB NodeId → 404 |
| `CrossTenantIsolation_Channel_Returns404` | TenantA JWT, request TenantB ChannelId → 404 |
| `SameTenant_Node_Returns200` | TenantA JWT, request TenantA NodeId → 200 |
| `PlatformAdmin_CanReadAllTenants` | Platform token, GET /admin/nodes → all tenants visible |
| `PlatformAdmin_OnTenantEndpoint_Returns403` | Platform token, GET /nodes (tenant endpoint) → 403 |
| `NodeToken_TenantIdMismatch_Returns403` | Node JWT with wrong tenantId claim → 403 |
| `CE_SystemTenant_Resolves` | Single tenant CE, valid JWT, no tenantId claim path → resolves SystemTenant |
| `SuspendedTenant_Returns409` | Valid JWT, tenant status = Suspended → 409 |
| `ProvisioningTenant_Returns409` | Valid JWT, tenant status = Provisioning → 409 |
| `HybridParameter_TenantOverride_WinsOverPlatform` | Tenant SyncParameter returned, not platform default |
| `HybridParameter_NoOverride_ReturnsPlatformDefault` | No tenant record → returns NULL-TenantId record |
| `SystemTenantSeeder_Idempotent` | Run twice → no duplicates, no error |

### Architectural gate (reflection test, CI)

Every entity class in `MSOSync.Persistence.Entities` must implement exactly one of:
- `ITenantScoped` (has `TenantId`)
- `[GlobalEntity]` attribute
- `[HybridEntity]` attribute

Zero uncategorized entities. New entities fail CI until ownership is declared.

---

## Wave Delivery Plan

**Wave 1 — Platform Foundation**
- `Tenant` entity, `TenantMembership` entity, migrations, `SystemTenantSeeder`
- `ITenantContext`, `TenantContext`, `PlatformTenantContext`
- `ITenantResolver`, `TenantResolverMiddleware`
- `ITenantAccessValidator`
- JWT: add `tenantId` claim to token generation and refresh token flow
- `ITenantScoped` marker interface, `ApplyTenantFilters()` extension
- `TenantRepository<T>`, `IPlatformRepository<T>` (internal), `IHybridLookupService`
- Auth: tenant login, tenant picker for multi-membership users, tenant switch endpoint (`POST /auth/switch-tenant` → new JWT)

**Wave 2 — Core Topology Migration (reference implementation)**
- Add `TenantId` column to: Nodes, NodeGroups, NodeSecurities, NodeScopes, NodeChannelAssignments, NodeTriggerAssignments, NodeRouterAssignments, Channels, Triggers, TriggerHists, Routers, TriggerRouters
- `HasQueryFilter` auto-applied via `ApplyTenantFilters()`
- Node and channel controllers use `TenantRepository<T>` (no changes to query logic)
- SystemTenantSeeder backfills these tables

**Wave 3 — Validation**
- All integration tests (cross-tenant isolation, CE verification, hybrid lookup, seeder idempotency)
- Architectural gate reflection test
- Manual CE upgrade validation

**Wave 4 — Documentation**
- Tenant architecture guide
- Entity ownership matrix (this document's classification table)
- Migration playbook for remaining domains (per-domain checklist for 15B+)
- Code review checklist: `ITenantScoped` declared, no `IgnoreQueryFilters` outside `IPlatformRepository`, no `TenantId` method parameters

---

## Definition of Done

- [ ] Tenant authentication works (JWT carries `tenantId` claim)
- [ ] Tenant resolution is automatic (middleware, no controller code)
- [ ] CE runs as single seeded `SystemTenant` with no code branching
- [ ] Core topology entities (Nodes, Channels, Triggers, Routers + assignments) are tenant-isolated
- [ ] Cross-tenant access returns 404 through standard APIs (invisible isolation)
- [ ] Platform admin token can read across tenants via `IPlatformRepository` only
- [ ] All integration tests pass
- [ ] Architectural gate reflection test enforces entity ownership in CI
- [ ] Migration playbook documents pattern for remaining 22 Tenant Scoped entities

---

## Architectural Invariants (enforced in code review)

1. `TenantId` is immutable — set once at INSERT, never updated
2. Repositories never accept `TenantId` as a method parameter
3. `IgnoreQueryFilters()` permitted only in `IPlatformRepository<T>` implementations
4. Hybrid entities never use EF global query filters
5. Every new entity must declare ownership (`ITenantScoped`, `[GlobalEntity]`, or `[HybridEntity]`) before merge
6. Middleware order: `UseAuthentication` → `TenantResolverMiddleware` → `UseAuthorization` → Controllers
7. `SystemTenant` cannot be deleted in Community Edition
8. Tenant slug is immutable after creation
9. Nothing reads JWT claims directly — all consumers use `ITenantContext`
