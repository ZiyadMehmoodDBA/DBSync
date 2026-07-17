# Task 5: Tenant Service Verification

**Part of:** [Epic 15B Domain Tenant Migration](2026-07-17-epic15b-domain-tenant-migration.md)

**Goal:** Confirm the EF global query filter is correct and harmless for three entities that have subtle behavior: `SyncUserRefreshToken` (auth flow), `SyncRuntimeStats` (metrics dashboard), `SyncNotification` (notification bell). No entity changes expected — this task adds targeted unit tests to prove the filter behavior is correct.

**Files:**
- Create: `tests/MSOSync.Tests/Tenancy/DomainTenantFilterVerificationTests.cs`
- Possibly modify: `src/MSOSync.Security/AuthenticationService.cs` (only if a bug is found during verification)

**Interfaces:**
- Consumes: `MutableTenantAccessor` + `WithTenantAsync<T>` from `tests/MSOSync.IntegrationTests/MultiTenancy/MultiTenantFixture.cs` (15A); `AppDbContext` with `ICurrentTenantAccessor`
- Produces: 3 passing unit tests confirming filter semantics; documentation of any issues found

---

## Background

After M032, the EF global query filter on each entity is:
```
accessor.TenantId == null  →  all rows visible  (platform context)
accessor.TenantId == X     →  only rows where TenantId == X
```

**SyncUserRefreshToken behavior:**
- Login call: `User.Identity.IsAuthenticated == false` → `TenantResolverMiddleware` skips → `accessor.TenantId == null` → filter passes all rows → `AuthenticationService` can find all tokens → issues JWT with tenantId claim. **No change needed.**
- Refresh call: user has JWT → middleware runs → sets tenant context → filter scopes to that tenant's refresh tokens. **Correct: a tenant A token cannot be used to refresh as tenant B.**
- Logout call: middleware sets tenant context → `AuthenticationService` revokes tokens where `TenantId == currentTenant`. **Correct: logout from tenant A does not revoke tenant B tokens.**

**SyncRuntimeStats behavior:**
- Stats are written by nodes (which are tenant-scoped). With the EF filter, `db.RuntimeStats` in HTTP context shows only the current tenant's stats. For a metrics dashboard, this is correct — tenant admins see only their own nodes' stats. **No change needed.**

**SyncNotification behavior:**
- `NotificationService` writes and reads notifications. With the EF filter, notifications are scoped to the current tenant. A tenant A user does not see tenant B notifications. **No change needed.**

---

- [ ] **Step 1: Write verification tests**

Create `tests/MSOSync.Tests/Tenancy/DomainTenantFilterVerificationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.Tests.Tenancy;

/// <summary>
/// Verifies EF global query filter semantics for three entities added in 15B.
/// Uses in-memory DB — proves filter logic, not SQL behavior.
/// </summary>
public sealed class DomainTenantFilterVerificationTests : IDisposable
{
    private readonly MutableTenantAccessor _accessor = new();
    private readonly AppDbContext          _db;

    public DomainTenantFilterVerificationTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts, _accessor);
    }

    public void Dispose() => _db.Dispose();

    // ── SyncUserRefreshToken ───────────────────────────────────────────────────

    [Fact]
    public async Task RefreshToken_PlatformContext_ReturnsAllTenants()
    {
        // Arrange – two tokens from different tenants
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _db.UserRefreshTokens.AddRange(
            new SyncUserRefreshToken { TokenFamilyHash = "hashA", LookupHash = "lookA", UserId = 1, TenantId = tenantA },
            new SyncUserRefreshToken { TokenFamilyHash = "hashB", LookupHash = "lookB", UserId = 2, TenantId = tenantB });
        await _db.SaveChangesAsync();

        // Act – platform context (accessor.TenantId == null)
        _accessor.SetTenantId(null);
        var count = await _db.UserRefreshTokens.CountAsync();

        // Assert – login endpoint runs in platform context; must see all tokens
        count.Should().Be(2);
    }

    [Fact]
    public async Task RefreshToken_TenantContext_ScopesToTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _db.UserRefreshTokens.AddRange(
            new SyncUserRefreshToken { TokenFamilyHash = "hashA", LookupHash = "lookA", UserId = 1, TenantId = tenantA },
            new SyncUserRefreshToken { TokenFamilyHash = "hashB", LookupHash = "lookB", UserId = 2, TenantId = tenantB });
        await _db.SaveChangesAsync();

        // Act – tenant A refresh request; cannot see tenant B's token
        _accessor.SetTenantId(tenantA);
        var tokens = await _db.UserRefreshTokens.ToListAsync();

        // Assert – refresh with tenant A JWT only sees tenant A tokens
        tokens.Should().HaveCount(1);
        tokens[0].TenantId.Should().Be(tenantA);
    }

    // ── SyncRuntimeStats ──────────────────────────────────────────────────────

    [Fact]
    public async Task RuntimeStats_TenantContext_ScopesToTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _db.RuntimeStats.AddRange(
            new SyncRuntimeStats { CpuPercent = 20m, TenantId = tenantA },
            new SyncRuntimeStats { CpuPercent = 80m, TenantId = tenantB });
        await _db.SaveChangesAsync();

        // Act – tenant A dashboard request
        _accessor.SetTenantId(tenantA);
        var stats = await _db.RuntimeStats.ToListAsync();

        // Assert – tenant A admin sees only their own nodes' stats
        stats.Should().HaveCount(1);
        stats[0].CpuPercent.Should().Be(20m);
    }

    // ── SyncNotification ──────────────────────────────────────────────────────

    [Fact]
    public async Task Notification_TenantContext_ScopesToTenant()
    {
        // Arrange
        var tenantA = Guid.NewGuid();
        var tenantB = Guid.NewGuid();
        _db.Notifications.AddRange(
            new SyncNotification { Title = "Alert A", TenantId = tenantA },
            new SyncNotification { Title = "Alert B", TenantId = tenantB });
        await _db.SaveChangesAsync();

        // Act – tenant B request
        _accessor.SetTenantId(tenantB);
        var notifications = await _db.Notifications.ToListAsync();

        // Assert – tenant B sees only their notifications
        notifications.Should().HaveCount(1);
        notifications[0].Title.Should().Be("Alert B");
    }
}

/// <summary>
/// Thread-unsafe accessor for unit tests — only one tenant at a time.
/// Same pattern as MutableTenantAccessor in MultiTenantFixture.cs (15A).
/// </summary>
internal sealed class MutableTenantAccessor : ICurrentTenantAccessor
{
    private Guid? _tenantId;
    public Guid? TenantId => _tenantId;
    public void SetTenantId(Guid? tenantId) => _tenantId = tenantId;
}
```

> **Property names:** The test uses properties like `TokenFamilyHash`, `LookupHash`, `UserId` on `SyncUserRefreshToken` and `Title` on `SyncNotification`. Verify these match the actual entity definitions before running. If property names differ, adjust them to match the actual entity.

> **If `MutableTenantAccessor` already exists** in `tests/MSOSync.IntegrationTests/MultiTenancy/MultiTenantFixture.cs` (from 15A), do NOT duplicate it. Instead, either:
> (a) Move it to `tests/MSOSync.Tests/Tenancy/MutableTenantAccessor.cs` (shared location), or
> (b) Define a local private version in this test file (simpler — the class is tiny).
> Use option (b) as shown above.

- [ ] **Step 2: Run tests — confirm they compile and pass**

```
dotnet test tests/MSOSync.Tests/ --filter "DomainTenantFilterVerificationTests" -v normal
```

Expected: `4 passed, 0 failed`

If a test fails:
- `MutableTenantAccessor.TenantId` returning null when set — check that `SetTenantId` actually sets the field.
- Filter not applying — confirm `AppDbContext` was constructed with the `ICurrentTenantAccessor` parameter: `new AppDbContext(opts, _accessor)`. If the second parameter is optional and defaults to null, the filter is not applied.
- Property not found on entity — adjust property names to match the actual entity class.

- [ ] **Step 3: Scan AuthenticationService for any tenantId-related issues**

Open `src/MSOSync.Security/AuthenticationService.cs` and search for queries against `db.UserRefreshTokens` (or `_db.UserRefreshTokens`). Confirm that:

1. Token lookup by `LookupHash` does not include a hardcoded `TenantId` filter — the EF global filter handles tenant scoping.
2. Token family revocation (all tokens for a family) will correctly be scoped to the current tenant after M032 — this is the correct behavior.
3. There are no direct `UPDATE` statements that bypass EF (raw SQL on `UserRefreshTokens`) — if found, they must be updated to include `WHERE tenant_id = @tenantId`.

If no issues are found, no code changes are needed. Document findings in the commit message.

- [ ] **Step 4: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: `Build succeeded. 0 Error(s) 0 Warning(s)`

- [ ] **Step 5: Run all unit tests**

```
dotnet test MSOSync.sln --filter "Category!=Integration" -v minimal
```

Expected: all tests pass (4 new passing tests for this task).

- [ ] **Step 6: Commit**

```
git add tests/MSOSync.Tests/Tenancy/DomainTenantFilterVerificationTests.cs
git commit -m "test(15B-5): EF filter verification tests for SyncUserRefreshToken, SyncRuntimeStats, SyncNotification"
```
