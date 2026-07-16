# Task 4: ITenantResolver + TenantResolver + ITenantAccessValidator + TenantAccessValidator

**Part of:** [Epic 15A Multi-Tenancy](2026-07-16-epic15a-multi-tenancy.md)

**Goal:** Implement tenant resolution (platform token → node token → user JWT → 401) and membership validation (membership exists, active, tenant active). Unit tests cover all resolution paths and all validator failure cases.

**Files:**
- Create: `src/MSOSync.Security/Tenancy/ITenantResolver.cs`
- Create: `src/MSOSync.Security/Tenancy/TenantResolver.cs`
- Create: `src/MSOSync.Security/Tenancy/ITenantAccessValidator.cs`
- Create: `src/MSOSync.Security/Tenancy/TenantAccessValidator.cs`
- Create: `tests/MSOSync.SecurityTests/Tenancy/TenantResolverTests.cs`
- Create: `tests/MSOSync.SecurityTests/Tenancy/TenantAccessValidatorTests.cs`

**Interfaces:**
- Consumes: `ITenantContext`, `TenantContext`, `PlatformTenantContext`, `TenantAccessException` (Tasks 1, 3); `Tenant`, `TenantMembership`, `DbSet<Tenant>`, `DbSet<TenantMembership>` (Task 2); `SyncNode` (existing); `AppDbContext` (existing)
- Produces: `ITenantResolver`, `ITenantAccessValidator` — consumed by Tasks 5, 8

---

- [ ] **Step 1: Write failing tests for TenantResolver**

Create `tests/MSOSync.SecurityTests/Tenancy/TenantResolverTests.cs`:
```csharp
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using MSOSync.Common.Tenancy;
using MSOSync.Security.Tenancy;
using NSubstitute;
using Xunit;

namespace MSOSync.SecurityTests.Tenancy;

public sealed class TenantResolverTests
{
    private readonly ITenantAccessValidator _validator = Substitute.For<ITenantAccessValidator>();
    private readonly INodeTenantLookup      _nodeLookup = Substitute.For<INodeTenantLookup>();

    private TenantResolver BuildSut() => new(_validator, _nodeLookup);

    private static HttpContext BuildContext(params (string type, string value)[] claims)
    {
        var identity = new ClaimsIdentity(claims.Select(c => new Claim(c.type, c.value)), "Bearer");
        var ctx = new DefaultHttpContext();
        ctx.User = new ClaimsPrincipal(identity);
        return ctx;
    }

    [Fact]
    public async Task NoToken_Returns401()
    {
        var ctx = new DefaultHttpContext(); // unauthenticated
        var sut = BuildSut();

        var act = () => sut.ResolveAsync(ctx, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 401);
    }

    [Fact]
    public async Task PlatformToken_NoTenantIdClaim_ReturnsPlatformContext()
    {
        var ctx = BuildContext(("userId", "1"), ("sub", "admin"));
        var sut = BuildSut();

        var result = await sut.ResolveAsync(ctx, default);

        result.IsPlatformContext.Should().BeTrue();
        result.TenantId.Should().Be(Guid.Empty);
    }

    [Fact]
    public async Task UserJwt_ValidMembership_ReturnsTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var ctx = BuildContext(("userId", "5"), ("tenantId", tenantId.ToString()));
        _validator.ValidateAsync(tenantId, 5L, default)
            .Returns(new TenantValidationResult(tenantId, "acme", EditionType.Community, roleId: 3L));
        var sut = BuildSut();

        var result = await sut.ResolveAsync(ctx, default);

        result.IsPlatformContext.Should().BeFalse();
        result.TenantId.Should().Be(tenantId);
        result.TenantSlug.Should().Be("acme");
        result.UserId.Should().Be(5L);
        result.RoleId.Should().Be(3L);
    }

    [Fact]
    public async Task NodeToken_TenantIdMatch_ReturnsTenantContext()
    {
        var tenantId = Guid.NewGuid();
        var ctx = BuildContext(("nodeId", "node-01"), ("tenantId", tenantId.ToString()));
        _nodeLookup.GetNodeTenantIdAsync("node-01", default).Returns(tenantId);
        var sut = BuildSut();

        var result = await sut.ResolveAsync(ctx, default);

        result.IsPlatformContext.Should().BeFalse();
        result.TenantId.Should().Be(tenantId);
        result.UserId.Should().BeNull();
    }

    [Fact]
    public async Task NodeToken_TenantIdMismatch_Returns403()
    {
        var claimedTenantId = Guid.NewGuid();
        var storedTenantId  = Guid.NewGuid(); // different
        var ctx = BuildContext(("nodeId", "node-01"), ("tenantId", claimedTenantId.ToString()));
        _nodeLookup.GetNodeTenantIdAsync("node-01", default).Returns(storedTenantId);
        var sut = BuildSut();

        var act = () => sut.ResolveAsync(ctx, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 403);
    }
}
```

Create `tests/MSOSync.SecurityTests/Tenancy/TenantAccessValidatorTests.cs`:
```csharp
using FluentAssertions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Security.Tenancy;
using NSubstitute;
using Xunit;

namespace MSOSync.SecurityTests.Tenancy;

public sealed class TenantAccessValidatorTests
{
    private static ITenantStore BuildStore(Tenant? tenant, TenantMembership? membership)
    {
        var store = Substitute.For<ITenantStore>();
        store.FindTenantAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(tenant);
        store.FindMembershipAsync(Arg.Any<Guid>(), Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(membership);
        return store;
    }

    private static Tenant ActiveTenant(Guid id) => new()
    {
        TenantId = id, Name = "T", Slug = "t", Status = TenantStatus.Active,
        Edition = MSOSync.Common.Tenancy.EditionType.Community,
        CreatedAtUtc = DateTimeOffset.UtcNow, UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    private static TenantMembership ActiveMembership(Guid tenantId, long userId) => new()
    {
        TenantId = tenantId, UserId = userId, RoleId = 1L,
        Status = MemberStatus.Active, JoinedAt = DateTimeOffset.UtcNow,
        LastAccessedAt = DateTimeOffset.UtcNow
    };

    [Fact]
    public async Task MembershipMissing_Throws403()
    {
        var tenantId = Guid.NewGuid();
        var store    = BuildStore(ActiveTenant(tenantId), membership: null);
        var sut      = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 99, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 403);
    }

    [Fact]
    public async Task MembershipSuspended_Throws403()
    {
        var tenantId   = Guid.NewGuid();
        var membership = ActiveMembership(tenantId, 5L);
        membership.Status = MemberStatus.Suspended;
        var store = BuildStore(ActiveTenant(tenantId), membership);
        var sut   = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 5, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 403);
    }

    [Fact]
    public async Task TenantSuspended_Throws409()
    {
        var tenantId = Guid.NewGuid();
        var tenant   = ActiveTenant(tenantId);
        tenant.Status = TenantStatus.Suspended;
        var store = BuildStore(tenant, ActiveMembership(tenantId, 5L));
        var sut   = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 5, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task TenantProvisioning_Throws409()
    {
        var tenantId = Guid.NewGuid();
        var tenant   = ActiveTenant(tenantId);
        tenant.Status = TenantStatus.Provisioning;
        var store = BuildStore(tenant, ActiveMembership(tenantId, 5L));
        var sut   = new TenantAccessValidator(store);

        var act = () => sut.ValidateAsync(tenantId, userId: 5, default);
        await act.Should().ThrowAsync<TenantAccessException>()
            .Where(e => e.StatusCode == 409);
    }

    [Fact]
    public async Task AllValid_ReturnsResult()
    {
        var tenantId = Guid.NewGuid();
        var store    = BuildStore(ActiveTenant(tenantId), ActiveMembership(tenantId, 5L));
        var sut      = new TenantAccessValidator(store);

        var result = await sut.ValidateAsync(tenantId, userId: 5, default);

        result.TenantId.Should().Be(tenantId);
        result.TenantSlug.Should().Be("t");
        result.RoleId.Should().Be(1L);
    }
}
```

- [ ] **Step 2: Run tests — confirm compile error**

```
dotnet test tests/MSOSync.SecurityTests/ --filter "TenantResolverTests|TenantAccessValidatorTests"
```
Expected: compile error — types not yet defined.

- [ ] **Step 3: Create ITenantAccessValidator + result type**

Create `src/MSOSync.Security/Tenancy/ITenantAccessValidator.cs`:
```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed record TenantValidationResult(
    Guid        TenantId,
    string      TenantSlug,
    EditionType Edition,
    long        RoleId);

public interface ITenantAccessValidator
{
    // Throws TenantAccessException (403/409) on any violation.
    Task<TenantValidationResult> ValidateAsync(Guid tenantId, long userId, CancellationToken ct);
}
```

Create `src/MSOSync.Security/Tenancy/ITenantStore.cs` (read-only DB interface used by validator):
```csharp
using MSOSync.Persistence.Entities;

namespace MSOSync.Security.Tenancy;

public interface ITenantStore
{
    Task<Tenant?>           FindTenantAsync    (Guid tenantId, CancellationToken ct);
    Task<TenantMembership?> FindMembershipAsync(Guid tenantId, long userId, CancellationToken ct);
}
```

Create `src/MSOSync.Security/Tenancy/TenantAccessValidator.cs`:
```csharp
using MSOSync.Persistence.Entities;

namespace MSOSync.Security.Tenancy;

public sealed class TenantAccessValidator(ITenantStore store) : ITenantAccessValidator
{
    public async Task<TenantValidationResult> ValidateAsync(Guid tenantId, long userId, CancellationToken ct)
    {
        var membership = await store.FindMembershipAsync(tenantId, userId, ct);
        if (membership is null)
            throw new TenantAccessException(403, "Tenant membership not found");

        if (membership.Status != MemberStatus.Active)
            throw new TenantAccessException(403, "Tenant membership is suspended");

        var tenant = await store.FindTenantAsync(tenantId, ct);
        if (tenant is null)
            throw new TenantAccessException(403, "Tenant not found");

        if (tenant.Status is TenantStatus.Provisioning or TenantStatus.Suspended)
            throw new TenantAccessException(409, $"Tenant is {tenant.Status.ToString().ToLower()}");

        return new TenantValidationResult(tenant.TenantId, tenant.Slug, tenant.Edition, membership.RoleId);
    }
}
```

- [ ] **Step 4: Create ITenantResolver + INodeTenantLookup**

Create `src/MSOSync.Security/Tenancy/ITenantResolver.cs`:
```csharp
using MSOSync.Common.Tenancy;
using Microsoft.AspNetCore.Http;

namespace MSOSync.Security.Tenancy;

public interface INodeTenantLookup
{
    Task<Guid?> GetNodeTenantIdAsync(string nodeId, CancellationToken ct);
}

public interface ITenantResolver
{
    Task<ITenantContext> ResolveAsync(HttpContext ctx, CancellationToken ct);
}
```

Create `src/MSOSync.Security/Tenancy/TenantResolver.cs`:
```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using MSOSync.Common.Tenancy;

namespace MSOSync.Security.Tenancy;

public sealed class TenantResolver(
    ITenantAccessValidator validator,
    INodeTenantLookup      nodeLookup) : ITenantResolver
{
    public async Task<ITenantContext> ResolveAsync(HttpContext ctx, CancellationToken ct)
    {
        var user = ctx.User;

        // No authenticated user → 401
        if (user.Identity?.IsAuthenticated != true)
            throw new TenantAccessException(401, "Authentication required");

        var tenantIdClaim = user.FindFirstValue("tenantId");
        var userIdClaim   = user.FindFirstValue("userId");
        var nodeIdClaim   = user.FindFirstValue("nodeId");

        // 1. Platform token — no tenantId claim
        if (string.IsNullOrEmpty(tenantIdClaim))
            return PlatformTenantContext.Instance;

        if (!Guid.TryParse(tenantIdClaim, out var tenantId))
            throw new TenantAccessException(401, "Invalid tenantId claim format");

        // 2. Node token — nodeId claim present
        if (!string.IsNullOrEmpty(nodeIdClaim))
        {
            var storedTenantId = await nodeLookup.GetNodeTenantIdAsync(nodeIdClaim, ct);
            if (storedTenantId is null || storedTenantId.Value != tenantId)
                throw new TenantAccessException(403, "Node token tenant mismatch");

            return new TenantContext(tenantId, tenantSlug: "", EditionType.Community, userId: null, roleId: null);
        }

        // 3. User JWT — userId + tenantId claims
        if (!long.TryParse(userIdClaim, out var userId))
            throw new TenantAccessException(401, "Invalid userId claim");

        var validation = await validator.ValidateAsync(tenantId, userId, ct);

        return new TenantContext(
            tenantId:   validation.TenantId,
            tenantSlug: validation.TenantSlug,
            edition:    validation.Edition,
            userId:     userId,
            roleId:     validation.RoleId);
    }
}
```

> **Note:** Node token path returns `tenantSlug: ""` and `Edition: Community` as placeholders — the node's actual tenant details are loaded by the node store in Task 7's final wiring. For 15A, the isolation guarantee is what matters (TenantId correct), not the slug in node context.

- [ ] **Step 5: Build**

```
dotnet build src/MSOSync.Security/MSOSync.Security.csproj
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 6: Run all Task 4 tests**

```
dotnet test tests/MSOSync.SecurityTests/ --filter "TenantResolverTests|TenantAccessValidatorTests" -v normal
```
Expected: `9 passed, 0 failed`

- [ ] **Step 7: Commit**

```
git add src/MSOSync.Security/Tenancy/ tests/MSOSync.SecurityTests/Tenancy/
git commit -m "feat(15A-4): ITenantResolver, TenantResolver, ITenantAccessValidator, TenantAccessValidator"
```
