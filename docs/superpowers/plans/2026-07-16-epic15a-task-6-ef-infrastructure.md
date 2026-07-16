# Task 6: EF Core Filter Infrastructure + TenantRepository + IPlatformRepository + IHybridLookupService

**Part of:** [Epic 15A Multi-Tenancy](2026-07-16-epic15a-multi-tenancy.md)

**Goal:** Modify AppDbContext to accept `ICurrentTenantAccessor` and auto-apply global query filters for all `ITenantScoped` entities. Provide `TenantRepository<T>` (filtered), `PlatformRepository<T>` (unfiltered, internal), and `IHybridLookupService` with tenant-fallback lookup. Unit-test the hybrid lookup service.

**Files:**
- Modify: `src/MSOSync.Persistence/AppDbContext.cs` — inject `ICurrentTenantAccessor?`, call `ApplyTenantFilters`
- Create: `src/MSOSync.Persistence/Tenancy/ModelBuilderTenantExtensions.cs`
- Create: `src/MSOSync.Persistence/Tenancy/TenantRepository.cs`
- Create: `src/MSOSync.Persistence/Tenancy/PlatformRepository.cs`
- Create: `src/MSOSync.Persistence/Tenancy/IHybridLookupService.cs`
- Create: `src/MSOSync.Persistence/Tenancy/HybridLookupService.cs`
- Create: `tests/MSOSync.Tests/Tenancy/HybridLookupServiceTests.cs`

**Interfaces:**
- Consumes: `ICurrentTenantAccessor`, `ITenantScoped`, `IHybridEntity` (Task 1); `AppDbContext` (existing); `TenantContextHolder` (Task 5)
- Produces: `TenantRepository<T>`, `PlatformRepository<T>`, `IHybridLookupService` — consumed by Tasks 7, 8

---

- [ ] **Step 1: Write failing test for HybridLookupService**

Create `tests/MSOSync.Tests/Tenancy/HybridLookupServiceTests.cs`:
```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.Tests.Tenancy;

public sealed class HybridLookupServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly HybridLookupService _sut;

    public HybridLookupServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db  = new AppDbContext(opts);
        _sut = new HybridLookupService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetAsync_TenantRecordExists_ReturnsTenantValue()
    {
        var tenantId = Guid.NewGuid();
        _db.Parameters.AddRange(
            new SyncParameter { ParameterName = "timeout", TenantId = null,     Value = "30" },
            new SyncParameter { ParameterName = "timeout", TenantId = tenantId, Value = "99" });
        await _db.SaveChangesAsync();

        var result = await _sut.GetParameterAsync(tenantId, "timeout", default);

        result.Should().NotBeNull();
        result!.Value.Should().Be("99");
    }

    [Fact]
    public async Task GetAsync_NoTenantRecord_ReturnsPlatformDefault()
    {
        var tenantId = Guid.NewGuid();
        _db.Parameters.Add(new SyncParameter { ParameterName = "timeout", TenantId = null, Value = "30" });
        await _db.SaveChangesAsync();

        var result = await _sut.GetParameterAsync(tenantId, "timeout", default);

        result.Should().NotBeNull();
        result!.Value.Should().Be("30");
    }

    [Fact]
    public async Task GetAsync_NeitherExists_ReturnsNull()
    {
        var result = await _sut.GetParameterAsync(Guid.NewGuid(), "nonexistent", default);
        result.Should().BeNull();
    }
}
```

- [ ] **Step 2: Run test — confirm compile error**

```
dotnet test tests/MSOSync.Tests/ --filter "HybridLookupServiceTests"
```
Expected: compile error — `HybridLookupService` not yet defined.

- [ ] **Step 3: Create IHybridLookupService**

Create `src/MSOSync.Persistence/Tenancy/IHybridLookupService.cs`:
```csharp
using MSOSync.Common.Tenancy;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Tenancy;

public interface IHybridLookupService
{
    // Returns tenant-specific SyncParameter if exists, else platform (NULL TenantId) record.
    Task<SyncParameter?>               GetParameterAsync   (Guid tenantId, string paramName, CancellationToken ct);
    Task<IReadOnlyList<SyncParameter>> GetAllParametersAsync(Guid tenantId, CancellationToken ct);
    Task<bool>                         ParameterExistsAsync (Guid tenantId, string paramName, CancellationToken ct);
}
```

> **Design note:** `IHybridLookupService` in 15A exposes typed methods for `SyncParameter` — the only Hybrid entity actively used in the 15A integration tests. Future epics extend this interface as additional Hybrid entities need tenant-aware lookup.

- [ ] **Step 4: Create HybridLookupService**

Create `src/MSOSync.Persistence/Tenancy/HybridLookupService.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Tenancy;

public sealed class HybridLookupService(AppDbContext db) : IHybridLookupService
{
    public async Task<SyncParameter?> GetParameterAsync(Guid tenantId, string paramName, CancellationToken ct)
    {
        // Try tenant-specific first
        var tenantRecord = await db.Parameters
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ParameterName == paramName, ct);

        if (tenantRecord is not null)
            return tenantRecord;

        // Fall back to platform default (NULL TenantId)
        return await db.Parameters
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == null && p.ParameterName == paramName, ct);
    }

    public async Task<IReadOnlyList<SyncParameter>> GetAllParametersAsync(Guid tenantId, CancellationToken ct)
    {
        // Merge: start with all platform defaults, override with tenant-specific values
        var platform = await db.Parameters
            .AsNoTracking()
            .Where(p => p.TenantId == null)
            .ToListAsync(ct);

        var tenantSpecific = await db.Parameters
            .AsNoTracking()
            .Where(p => p.TenantId == tenantId)
            .ToListAsync(ct);

        var tenantNames = tenantSpecific.Select(p => p.ParameterName).ToHashSet();
        var merged      = tenantSpecific.Concat(platform.Where(p => !tenantNames.Contains(p.ParameterName)));
        return merged.ToList();
    }

    public async Task<bool> ParameterExistsAsync(Guid tenantId, string paramName, CancellationToken ct)
        => await GetParameterAsync(tenantId, paramName, ct) is not null;
}
```

- [ ] **Step 5: Run HybridLookupService tests**

```
dotnet test tests/MSOSync.Tests/ --filter "HybridLookupServiceTests" -v normal
```
Expected: `3 passed, 0 failed`

> **Note:** The in-memory provider doesn't enforce `Guid? TenantId` nullable column constraints (those come from the real SQL migration in Task 7). This is acceptable for unit tests; integration tests in Task 8 use the real DB.

- [ ] **Step 6: Create ModelBuilderTenantExtensions**

Create `src/MSOSync.Persistence/Tenancy/ModelBuilderTenantExtensions.cs`:
```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Tenancy;

public static class ModelBuilderTenantExtensions
{
    public static void ApplyTenantFilters(this ModelBuilder modelBuilder, ICurrentTenantAccessor accessor)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
            .Where(t => typeof(ITenantScoped).IsAssignableFrom(t.ClrType)))
        {
            modelBuilder.Entity(entityType.ClrType)
                .HasQueryFilter(BuildFilter(entityType.ClrType, accessor));
        }
    }

    // Builds: e => accessor.TenantId == null || e.TenantId == accessor.TenantId.Value
    // EF Core evaluates accessor.TenantId at query time (singleton reads IHttpContextAccessor).
    private static LambdaExpression BuildFilter(Type clrType, ICurrentTenantAccessor accessor)
    {
        var param       = Expression.Parameter(clrType, "e");
        var tenantIdProp = Expression.Property(param, nameof(ITenantScoped.TenantId));

        var accessorExpr      = Expression.Constant(accessor, typeof(ICurrentTenantAccessor));
        var accessorTenantId  = Expression.Property(accessorExpr, nameof(ICurrentTenantAccessor.TenantId));

        // accessor.TenantId == null  (platform context or no request)
        var isNull = Expression.Equal(accessorTenantId, Expression.Constant(null, typeof(Guid?)));

        // accessor.TenantId.Value  (unwrap Guid? → Guid)
        var accessorValue = Expression.Property(accessorTenantId, "Value");

        // e.TenantId == accessor.TenantId.Value
        var equals = Expression.Equal(tenantIdProp, accessorValue);

        // accessor.TenantId == null || e.TenantId == accessor.TenantId.Value
        var filter = Expression.OrElse(isNull, equals);

        return Expression.Lambda(filter, param);
    }
}
```

- [ ] **Step 7: Modify AppDbContext to inject ICurrentTenantAccessor and apply filters**

Open `src/MSOSync.Persistence/AppDbContext.cs`.

Update the constructor:
```csharp
private readonly ICurrentTenantAccessor? _tenantAccessor;

public AppDbContext(
    DbContextOptions<AppDbContext> options,
    ICurrentTenantAccessor?        tenantAccessor = null)
    : base(options)
{
    _tenantAccessor = tenantAccessor;
}
```

Update `OnModelCreating` to call `ApplyTenantFilters` after the existing configuration:
```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

    if (_tenantAccessor is not null)
        modelBuilder.ApplyTenantFilters(_tenantAccessor);
}
```

Add required using at the top:
```csharp
using MSOSync.Common.Tenancy;
using MSOSync.Persistence.Tenancy;
```

> **Backward compatibility:** All existing tests that call `new AppDbContext(opts)` still compile because `tenantAccessor` is optional. When null, no filters are applied — tests see all data as before.

- [ ] **Step 8: Create TenantRepository<T>**

Create `src/MSOSync.Persistence/Tenancy/TenantRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Tenancy;

// Base class for all tenant-scoped repositories.
// Global query filter is active — all queries automatically scoped to current tenant.
// NEVER accept TenantId as a method parameter — tenant always comes from ITenantContext.
public abstract class TenantRepository<T>(AppDbContext db) where T : class, ITenantScoped
{
    protected DbSet<T> Set => db.Set<T>();

    protected Task<T?> FindAsync(object key, CancellationToken ct)
        => db.FindAsync<T>(new[] { key }, ct).AsTask();

    protected Task SaveAsync(CancellationToken ct)
        => db.SaveChangesAsync(ct);
}
```

- [ ] **Step 9: Create PlatformRepository<T> (internal)**

Create `src/MSOSync.Persistence/Tenancy/PlatformRepository.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Tenancy;

// INTERNAL — only platform-admin code may use this.
// The ONLY class permitted to call IgnoreQueryFilters().
// Do not expose via public API; inject IPlatformRepository<T> in callers.
internal interface IPlatformRepository<T> where T : class
{
    IQueryable<T> QueryAll();
}

internal sealed class PlatformRepository<T>(AppDbContext db) : IPlatformRepository<T>
    where T : class
{
    public IQueryable<T> QueryAll()
        => db.Set<T>().IgnoreQueryFilters().AsNoTracking();
}
```

- [ ] **Step 10: Register IHybridLookupService and PlatformRepository in DI**

Open `src/MSOSync.App/Program.cs` and add to the tenancy registrations block from Task 5:
```csharp
builder.Services.AddScoped<IHybridLookupService, HybridLookupService>();
// PlatformRepository is registered per-type as needed — example:
// builder.Services.AddScoped(typeof(IPlatformRepository<>), typeof(PlatformRepository<>));
```

The `PlatformRepository<T>` open-generic registration covers all entity types for platform-admin use.

Add the open-generic registration:
```csharp
builder.Services.AddScoped(typeof(IPlatformRepository<>), typeof(PlatformRepository<>));
```

Required usings:
```csharp
using MSOSync.Persistence.Tenancy;
```

- [ ] **Step 11: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 12: Run all tests accumulated so far**

```
dotnet test D:\MSOSync\MSOSync.sln --filter "EntityOwnershipGateTests|TenantContextTests|TenantResolverTests|TenantAccessValidatorTests|HybridLookupServiceTests" -v normal
```
Expected: all pass (count from Tasks 1–6 combined ≥ 20 tests)

- [ ] **Step 13: Commit**

```
git add src/MSOSync.Persistence/AppDbContext.cs
git add src/MSOSync.Persistence/Tenancy/
git add tests/MSOSync.Tests/Tenancy/HybridLookupServiceTests.cs
git commit -m "feat(15A-6): EF tenant query filters, TenantRepository, PlatformRepository, IHybridLookupService"
```
