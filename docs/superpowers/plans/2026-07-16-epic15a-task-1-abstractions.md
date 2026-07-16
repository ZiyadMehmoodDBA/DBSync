# Task 1: Entity Ownership Abstractions + Markers

**Part of:** [Epic 15A Multi-Tenancy](2026-07-16-epic15a-multi-tenancy.md)

**Goal:** Create all tenancy abstractions in `MSOSync.Common`, apply ownership markers to every entity in the persistence layer, and prove the gate test passes. No database migrations in this task.

> **IMPORTANT:** Do NOT run `dotnet ef migrations add` after this task. The `Guid TenantId` property is added to topology entity classes here so EF can pick it up in Task 7's migration. Running migrations early will produce an incorrect migration.

**Files:**
- Create: `src/MSOSync.Common/Tenancy/EditionType.cs`
- Create: `src/MSOSync.Common/Tenancy/ITenantContext.cs`
- Create: `src/MSOSync.Common/Tenancy/ICurrentTenantAccessor.cs`
- Create: `src/MSOSync.Common/Tenancy/ITenantScoped.cs`
- Create: `src/MSOSync.Common/Tenancy/IHybridEntity.cs`
- Create: `src/MSOSync.Common/Tenancy/GlobalEntityAttribute.cs`
- Create: `src/MSOSync.Common/Tenancy/HybridEntityAttribute.cs`
- Create: `src/MSOSync.Common/Tenancy/TenantScopedAttribute.cs`
- Create: `src/MSOSync.Common/Tenancy/WellKnownTenantIds.cs`
- Modify: 5 Global entity files — add `[GlobalEntity]`
- Modify: 6 Hybrid entity files — add `[HybridEntity]`
- Modify: 12 Core topology entity files — implement `ITenantScoped`, add `Guid TenantId { get; set; }`
- Modify: 22 Other tenant-scoped entity files — add `[TenantScoped]`
- Create: `src/MSOSync.Persistence/Entities/SyncMonitorRule.cs` (new concept, no DbSet yet)
- Create: `tests/MSOSync.Tests/Tenancy/EntityOwnershipGateTests.cs`

**Interfaces:**
- Produces: `ITenantContext`, `ICurrentTenantAccessor`, `ITenantScoped`, `IHybridEntity`, `EditionType`, `WellKnownTenantIds`, all three attributes — consumed by Tasks 2–8

---

- [ ] **Step 1: Create the Tenancy folder and core abstractions**

Create `src/MSOSync.Common/Tenancy/EditionType.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

public enum EditionType { Community, Enterprise }
```

Create `src/MSOSync.Common/Tenancy/ITenantContext.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

public interface ITenantContext
{
    Guid        TenantId          { get; }
    string      TenantSlug        { get; }
    EditionType Edition           { get; }
    long?       UserId            { get; }   // null for node tokens and platform tokens
    long?       RoleId            { get; }   // from TenantMembership.RoleId; null for platform
    bool        IsPlatformContext { get; }   // true → TenantId == Guid.Empty, RoleId == null
}
```

Create `src/MSOSync.Common/Tenancy/ICurrentTenantAccessor.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

// Singleton — reads current request's ITenantContext via IHttpContextAccessor.
// Used by AppDbContext query filters to avoid EF model-cache issues.
public interface ICurrentTenantAccessor
{
    Guid? TenantId { get; }   // null = platform context or no active request
}
```

Create `src/MSOSync.Common/Tenancy/ITenantScoped.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

// Implemented by every Tenant Scoped entity that HAS a TenantId column.
// EF Core's ApplyTenantFilters() auto-registers a global query filter for all implementors.
public interface ITenantScoped
{
    Guid TenantId { get; set; }
}
```

Create `src/MSOSync.Common/Tenancy/IHybridEntity.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

// Marker for Hybrid entities: nullable TenantId, no EF global filter.
// Use IHybridLookupService for tenant-aware queries.
public interface IHybridEntity { }
```

Create `src/MSOSync.Common/Tenancy/GlobalEntityAttribute.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class GlobalEntityAttribute : Attribute { }
```

Create `src/MSOSync.Common/Tenancy/HybridEntityAttribute.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HybridEntityAttribute : Attribute { }
```

Create `src/MSOSync.Common/Tenancy/TenantScopedAttribute.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

// Applied to Tenant Scoped entities whose TenantId column migration is deferred
// to a future epic. Once migrated, the entity implements ITenantScoped instead.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class TenantScopedAttribute : Attribute { }
```

Create `src/MSOSync.Common/Tenancy/WellKnownTenantIds.cs`:
```csharp
namespace MSOSync.Common.Tenancy;

public static class WellKnownTenantIds
{
    // Fixed GUID for the Community Edition SystemTenant — used in migrations for backfill DEFAULT.
    public static readonly Guid SystemTenant = new("00000000-0000-0000-0000-000000000001");
}
```

- [ ] **Step 2: Verify MSOSync.Common builds**

Run:
```
dotnet build src/MSOSync.Common/MSOSync.Common.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Apply [GlobalEntity] to the 5 Global entities**

These entities require only an attribute — no property changes.

For each file listed, add `using MSOSync.Common.Tenancy;` and `[GlobalEntity]` to the class declaration.

**`src/MSOSync.Persistence/Entities/SyncPermission.cs`** — add above class:
```csharp
using MSOSync.Common.Tenancy;

[GlobalEntity]
public class SyncPermission
{
    // existing properties unchanged
```

Apply the same pattern to:
- `src/MSOSync.Persistence/Entities/SyncRolePermission.cs`
- `src/MSOSync.Persistence/Entities/SyncPlugin.cs`
- `src/MSOSync.Persistence/Entities/SyncLock.cs`
- `src/MSOSync.Persistence/Entities/SyncMonitor.cs`

- [ ] **Step 4: Apply [HybridEntity] to the 6 Hybrid entities**

`SyncUser` gets `[HybridEntity]` only (NO TenantId property — platform identity, scoped via TenantMembership junction).

The other 5 Hybrid entities also get `[HybridEntity]` but DO get `Guid? TenantId { get; set; }` added as a property now. The migration column for these is in Task 7 — do NOT run `dotnet ef migrations add` after this step.

**`src/MSOSync.Persistence/Entities/SyncUser.cs`** — add attribute only:
```csharp
using MSOSync.Common.Tenancy;

[HybridEntity]
public class SyncUser
{
    // existing properties unchanged — NO TenantId property
```

**`src/MSOSync.Persistence/Entities/SyncRole.cs`** — add attribute + property:
```csharp
using MSOSync.Common.Tenancy;

[HybridEntity]
public class SyncRole : IHybridEntity
{
    // ... existing properties ...
    public Guid? TenantId { get; set; }  // null = system role; non-null = tenant custom role
```

Apply same pattern (attribute + IHybridEntity + `Guid? TenantId`) to:
- `src/MSOSync.Persistence/Entities/SyncUserRole.cs`
- `src/MSOSync.Persistence/Entities/SyncParameter.cs`
- `src/MSOSync.Persistence/Entities/SyncParameterHist.cs`
- `src/MSOSync.Persistence/Entities/SyncUserPreference.cs`

- [ ] **Step 5: Apply ITenantScoped to the 12 core topology entities**

These entities implement `ITenantScoped` and get `Guid TenantId { get; set; }`. EF config for the column is in Task 7. Do NOT run migrations.

**`src/MSOSync.Persistence/Entities/SyncNode.cs`** — full change shown:
```csharp
using MSOSync.Common.Tenancy;

public class SyncNode : ITenantScoped
{
    // ... existing properties unchanged ...

    // Added for multi-tenancy (column migration in Task 7)
    public Guid TenantId { get; set; }
}
```

Apply exactly the same change (add `: ITenantScoped` + `public Guid TenantId { get; set; }`) to:
- `src/MSOSync.Persistence/Entities/SyncNodeGroup.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeSecurity.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeScope.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeChannelAssignment.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeTriggerAssignment.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeRouterAssignment.cs`
- `src/MSOSync.Persistence/Entities/SyncChannel.cs`
- `src/MSOSync.Persistence/Entities/SyncTrigger.cs`
- `src/MSOSync.Persistence/Entities/SyncTriggerHist.cs`
- `src/MSOSync.Persistence/Entities/SyncRouter.cs`
- `src/MSOSync.Persistence/Entities/SyncTriggerRouter.cs`

- [ ] **Step 6: Apply [TenantScoped] to the 22 deferred tenant-scoped entities**

These entities will receive `TenantId` in future epic migrations. For now, only apply the attribute.

Add `using MSOSync.Common.Tenancy;` and `[TenantScoped]` to each class:

- `src/MSOSync.Persistence/Entities/SyncNodeLifecycleHistory.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeConnectivityHistory.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeBootstrapToken.cs`
- `src/MSOSync.Persistence/Entities/SyncRegistrationRequest.cs`
- `src/MSOSync.Persistence/Entities/SyncDataEvent.cs`
- `src/MSOSync.Persistence/Entities/SyncDataEventBatch.cs`
- `src/MSOSync.Persistence/Entities/SyncOutgoingBatch.cs`
- `src/MSOSync.Persistence/Entities/SyncIncomingBatch.cs`
- `src/MSOSync.Persistence/Entities/SyncBatchError.cs`
- `src/MSOSync.Persistence/Entities/SyncConfigurationTemplate.cs`
- `src/MSOSync.Persistence/Entities/SyncConfigurationTemplateVersion.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeConfigurationOverride.cs`
- `src/MSOSync.Persistence/Entities/SyncNodeConfigurationHistory.cs`
- `src/MSOSync.Persistence/Entities/SyncConfigurationRollout.cs`
- `src/MSOSync.Persistence/Entities/SyncRuntimeStats.cs`
- `src/MSOSync.Persistence/Entities/SyncAudit.cs`
- `src/MSOSync.Persistence/Entities/SyncOperation.cs`
- `src/MSOSync.Persistence/Entities/SyncExportJob.cs`
- `src/MSOSync.Persistence/Entities/SyncNotification.cs`
- `src/MSOSync.Persistence/Entities/SyncUserNotification.cs`
- `src/MSOSync.Persistence/Entities/SyncUserRefreshToken.cs`

- [ ] **Step 7: Create the SyncMonitorRule entity (new concept, no DbSet yet)**

Create `src/MSOSync.Persistence/Entities/SyncMonitorRule.cs`:
```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

// Tenant-scoped monitoring rule entity.
// DB table (MonitorRules) created in a future epic when the monitoring rules feature ships.
// Do NOT add a DbSet<SyncMonitorRule> to AppDbContext until that migration exists.
public class SyncMonitorRule : ITenantScoped
{
    public Guid   RuleId      { get; set; }
    public Guid   TenantId    { get; set; }
    public string Name        { get; set; } = "";
    public string Expression  { get; set; } = "";
    public bool   IsEnabled   { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
```

- [ ] **Step 8: Verify MSOSync.Persistence builds**

Run:
```
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj
```
Expected: `Build succeeded. 0 Error(s)`

If MSOSync.Persistence does not already reference MSOSync.Common, add the project reference:
```xml
<!-- In src/MSOSync.Persistence/MSOSync.Persistence.csproj -->
<ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
```

- [ ] **Step 9: Write the architectural gate test**

Create `tests/MSOSync.Tests/Tenancy/EntityOwnershipGateTests.cs`:
```csharp
using System.Reflection;
using FluentAssertions;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.Tests.Tenancy;

public sealed class EntityOwnershipGateTests
{
    private static readonly Assembly PersistenceAssembly = typeof(SyncNode).Assembly;

    [Fact]
    public void AllEntityClasses_HaveExactlyOneOwnershipMarker()
    {
        var entityTypes = PersistenceAssembly
            .GetTypes()
            .Where(t => t.Namespace == "MSOSync.Persistence.Entities"
                     && t.IsClass
                     && !t.IsAbstract
                     && !t.IsEnum)
            .ToList();

        entityTypes.Should().NotBeEmpty("expected to find entity classes");

        var failures = new List<string>();

        foreach (var type in entityTypes)
        {
            var isTenantScoped  = typeof(ITenantScoped).IsAssignableFrom(type);
            var hasTenantScoped = type.GetCustomAttribute<TenantScopedAttribute>() is not null;
            var isGlobal        = type.GetCustomAttribute<GlobalEntityAttribute>()  is not null;
            var isHybrid        = type.GetCustomAttribute<HybridEntityAttribute>()  is not null;

            var markerCount = new[] { isTenantScoped || hasTenantScoped, isGlobal, isHybrid }
                .Count(x => x);

            if (markerCount == 0)
                failures.Add($"{type.Name}: missing ownership marker (add ITenantScoped, [TenantScoped], [GlobalEntity], or [HybridEntity])");
            else if (markerCount > 1 && !(isTenantScoped && hasTenantScoped))
                failures.Add($"{type.Name}: multiple conflicting ownership markers");
        }

        failures.Should().BeEmpty(
            because: "every entity must declare exactly one ownership category");
    }
}
```

- [ ] **Step 10: Run the gate test to confirm it passes**

Run:
```
dotnet test tests/MSOSync.Tests/ --filter "EntityOwnershipGateTests" -v normal
```
Expected: `1 passed, 0 failed`

If any entity is missing a marker the test output will name it exactly — fix it and re-run.

- [ ] **Step 11: Commit**

```
git add src/MSOSync.Common/Tenancy/ src/MSOSync.Persistence/Entities/ tests/MSOSync.Tests/Tenancy/
git commit -m "feat(15A-1): entity ownership abstractions, markers on all entities, gate test"
```
