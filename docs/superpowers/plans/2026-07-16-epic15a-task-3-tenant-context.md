# Task 3: TenantContext + PlatformTenantContext + TenantAccessException

**Part of:** [Epic 15A Multi-Tenancy](2026-07-16-epic15a-multi-tenancy.md)

**Goal:** Create the concrete `ITenantContext` implementations and `TenantAccessException`. These are pure value types / exceptions — no I/O, no DB, fully unit-testable.

**Files:**
- Create: `src/MSOSync.Security/Tenancy/TenantContext.cs`
- Create: `src/MSOSync.Security/Tenancy/PlatformTenantContext.cs`
- Create: `src/MSOSync.Security/Tenancy/TenantAccessException.cs`
- Create: `tests/MSOSync.SecurityTests/Tenancy/TenantContextTests.cs`

**Interfaces:**
- Consumes: `ITenantContext`, `EditionType` from Task 1
- Produces: `TenantContext`, `PlatformTenantContext`, `TenantAccessException` — consumed by Tasks 4, 5, 8

---

- [ ] **Step 1: Write failing tests**

Create `tests/MSOSync.SecurityTests/Tenancy/TenantContextTests.cs`:
```csharp
using FluentAssertions;
using MSOSync.Common.Tenancy;
using MSOSync.Security.Tenancy;
using Xunit;

namespace MSOSync.SecurityTests.Tenancy;

public sealed class TenantContextTests
{
    [Fact]
    public void TenantContext_Properties_RoundTrip()
    {
        var tenantId = Guid.NewGuid();
        var ctx = new TenantContext(
            tenantId:   tenantId,
            tenantSlug: "acme",
            edition:    EditionType.Enterprise,
            userId:     42L,
            roleId:     7L);

        ctx.TenantId.Should().Be(tenantId);
        ctx.TenantSlug.Should().Be("acme");
        ctx.Edition.Should().Be(EditionType.Enterprise);
        ctx.UserId.Should().Be(42L);
        ctx.RoleId.Should().Be(7L);
        ctx.IsPlatformContext.Should().BeFalse();
    }

    [Fact]
    public void TenantContext_NullableUsers_Allowed()
    {
        var ctx = new TenantContext(
            tenantId:   Guid.NewGuid(),
            tenantSlug: "node-ctx",
            edition:    EditionType.Community,
            userId:     null,
            roleId:     null);

        ctx.UserId.Should().BeNull();
        ctx.RoleId.Should().BeNull();
        ctx.IsPlatformContext.Should().BeFalse();
    }

    [Fact]
    public void PlatformTenantContext_HasCorrectDefaults()
    {
        var ctx = PlatformTenantContext.Instance;

        ctx.TenantId.Should().Be(Guid.Empty);
        ctx.TenantSlug.Should().Be("");
        ctx.UserId.Should().BeNull();
        ctx.RoleId.Should().BeNull();
        ctx.IsPlatformContext.Should().BeTrue();
    }

    [Fact]
    public void TenantAccessException_StoresCode()
    {
        var ex = new TenantAccessException(403, "Membership not found");
        ex.StatusCode.Should().Be(403);
        ex.Message.Should().Be("Membership not found");
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test tests/MSOSync.SecurityTests/ --filter "TenantContextTests" -v normal
```
Expected: compile error — `TenantContext`, `PlatformTenantContext`, `TenantAccessException` not yet defined.

- [ ] **Step 3: Create TenantContext**

Create `src/MSOSync.Security/Tenancy/TenantContext.cs`:
```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class TenantContext : ITenantContext
{
    public Guid        TenantId          { get; }
    public string      TenantSlug        { get; }
    public EditionType Edition           { get; }
    public long?       UserId            { get; }
    public long?       RoleId            { get; }
    public bool        IsPlatformContext => false;

    public TenantContext(
        Guid        tenantId,
        string      tenantSlug,
        EditionType edition,
        long?       userId,
        long?       roleId)
    {
        TenantId   = tenantId;
        TenantSlug = tenantSlug;
        Edition    = edition;
        UserId     = userId;
        RoleId     = roleId;
    }
}
```

- [ ] **Step 4: Create PlatformTenantContext**

Create `src/MSOSync.Security/Tenancy/PlatformTenantContext.cs`:
```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class PlatformTenantContext : ITenantContext
{
    public static readonly PlatformTenantContext Instance = new();

    public Guid        TenantId          => Guid.Empty;
    public string      TenantSlug        => "";
    public EditionType Edition           => EditionType.Enterprise;   // platform has no edition restriction
    public long?       UserId            => null;
    public long?       RoleId            => null;
    public bool        IsPlatformContext => true;

    private PlatformTenantContext() { }
}
```

- [ ] **Step 5: Create TenantAccessException**

Create `src/MSOSync.Security/Tenancy/TenantAccessException.cs`:
```csharp
namespace MSOSync.Security.Tenancy;

public sealed class TenantAccessException : Exception
{
    public int StatusCode { get; }

    public TenantAccessException(int statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }
}
```

- [ ] **Step 6: Verify build**

```
dotnet build src/MSOSync.Security/MSOSync.Security.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 7: Run tests — confirm all pass**

```
dotnet test tests/MSOSync.SecurityTests/ --filter "TenantContextTests" -v normal
```
Expected: `4 passed, 0 failed`

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Security/Tenancy/ tests/MSOSync.SecurityTests/Tenancy/TenantContextTests.cs
git commit -m "feat(15A-3): TenantContext, PlatformTenantContext, TenantAccessException"
```
