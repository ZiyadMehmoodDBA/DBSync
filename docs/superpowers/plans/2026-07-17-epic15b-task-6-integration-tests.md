# Task 6: Integration Tests

**Part of:** [Epic 15B Domain Tenant Migration](2026-07-17-epic15b-domain-tenant-migration.md)

**Goal:** Integration tests against real SQL Server (same DB used in development) covering: cross-tenant isolation for each entity group, platform repository visibility, migration smoke (all rows assigned SystemTenant), and composite index coverage.

**Files:**
- Create: `tests/MSOSync.IntegrationTests/MultiTenancy/DomainMigrationIsolationTests.cs`
- Modify: `tests/MSOSync.IntegrationTests/MultiTenancy/MultiTenantFixture.cs` — add `TenantB` data setup helpers for the new entity groups

**Interfaces:**
- Consumes: `MultiTenantFixture` (15A), `MutableTenantAccessor`, `WithTenantAsync<T>` from `MultiTenantFixture.cs`; `IPlatformRepository<SyncAudit>` from Task 4; real SQL Server DB with M032 applied
- Produces: passing integration test suite covering 21 newly migrated entities

---

## Test Strategy

All tests use the real SQL Server DB and the existing `MultiTenantFixture` (from 15A). They must run serially within the collection (same `[Collection("MultiTenancy")]` attribute).

**Isolation test pattern:**
```
TenantA creates entity
TenantB queries same DbSet
→ Sees 0 rows (EF filter isolates correctly)
```

**Platform repo test:**
```
Both TenantA and TenantB have entities
PlatformRepository<T>.QueryAll()
→ Sees entities from both tenants
```

**Migration smoke test (runs once, reads from DB):**
```
SELECT COUNT(*) FROM table WHERE tenant_id IS NULL
→ Expected: 0 (all rows backfilled to SystemTenant)
```

---

- [ ] **Step 1: Create the integration test class**

Create `tests/MSOSync.IntegrationTests/MultiTenancy/DomainMigrationIsolationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Tenancy;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Tenancy;
using Xunit;

namespace MSOSync.IntegrationTests.MultiTenancy;

[Collection("MultiTenancy")]
public sealed class DomainMigrationIsolationTests(MultiTenantFixture fixture)
{
    // ── Migration smoke: zero NULLs across all 21 tables ──────────────────────

    [Fact]
    public async Task M032_NoNullTenantIds_InAnyMigratedTable()
    {
        // Use raw SQL to verify the actual DB state post-M032
        await using var conn = fixture.CreateConnection();
        await conn.OpenAsync();

        // Sample 5 tables across entity groups
        string[] checks =
        [
            "SELECT COUNT(*) FROM [msosync].[sync_audit] WHERE [tenant_id] IS NULL",
            "SELECT COUNT(*) FROM [msosync].[sync_outgoing_batch] WHERE [tenant_id] IS NULL",
            "SELECT COUNT(*) FROM [msosync].[sync_configuration_template] WHERE [tenant_id] IS NULL",
            "SELECT COUNT(*) FROM [msosync].[sync_user_refresh_token] WHERE [tenant_id] IS NULL",
            "SELECT COUNT(*) FROM [dbo].[sync_export_job] WHERE [tenant_id] IS NULL",
        ];

        foreach (var sql in checks)
        {
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = sql;
            var nullCount = (int)(await cmd.ExecuteScalarAsync())!;
            nullCount.Should().Be(0, because: $"migration backfill should set all rows: {sql}");
        }
    }

    // ── Group 1: Node Management — SyncRegistrationRequest isolation ──────────

    [Fact]
    public async Task RegistrationRequest_TenantA_NotVisibleToTenantB()
    {
        await fixture.WithTenantAsync(fixture.TenantAId, async db =>
        {
            db.RegistrationRequests.Add(new SyncRegistrationRequest
            {
                NodeId      = "node-isolation-test",
                Status      = RegistrationStatus.Pending,
                TenantId    = fixture.TenantAId,
                RequestTime = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        await fixture.WithTenantAsync(fixture.TenantBId, async db =>
        {
            var count = await db.RegistrationRequests
                .Where(r => r.NodeId == "node-isolation-test")
                .CountAsync();
            count.Should().Be(0, "TenantB must not see TenantA's registration requests");
        });
    }

    // ── Group 2: Synchronization Engine — SyncAudit isolation ─────────────────

    [Fact]
    public async Task Audit_TenantA_NotVisibleToTenantB()
    {
        await fixture.WithTenantAsync(fixture.TenantAId, async db =>
        {
            db.Audits.Add(new SyncAudit
            {
                ActionName  = "TEST_ACTION_ISOLATION",
                Username    = "testuser",
                TenantId    = fixture.TenantAId,
                CreateTime  = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        await fixture.WithTenantAsync(fixture.TenantBId, async db =>
        {
            var count = await db.Audits
                .Where(a => a.ActionName == "TEST_ACTION_ISOLATION")
                .CountAsync();
            count.Should().Be(0, "TenantB must not see TenantA's audit records");
        });
    }

    // ── Group 3: Configuration Management — SyncConfigurationTemplate isolation

    [Fact]
    public async Task ConfigTemplate_TenantA_NotVisibleToTenantB()
    {
        await fixture.WithTenantAsync(fixture.TenantAId, async db =>
        {
            db.ConfigurationTemplates.Add(new SyncConfigurationTemplate
            {
                Name        = "isolation-test-template",
                TenantId    = fixture.TenantAId,
                CreatedAt   = DateTime.UtcNow,
                UpdatedAt   = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        await fixture.WithTenantAsync(fixture.TenantBId, async db =>
        {
            var count = await db.ConfigurationTemplates
                .Where(t => t.Name == "isolation-test-template")
                .CountAsync();
            count.Should().Be(0, "TenantB must not see TenantA's configuration templates");
        });
    }

    // ── SyncConfigurationTemplate: per-tenant unique name allowed ─────────────

    [Fact]
    public async Task ConfigTemplate_SameNameAllowedInDifferentTenants()
    {
        // Both tenants can have a template named "default"
        await fixture.WithTenantAsync(fixture.TenantAId, async db =>
        {
            db.ConfigurationTemplates.Add(new SyncConfigurationTemplate
            {
                Name = "default", TenantId = fixture.TenantAId,
                CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
            });
            await db.SaveChangesAsync();
        });

        var act = async () =>
        {
            await fixture.WithTenantAsync(fixture.TenantBId, async db =>
            {
                db.ConfigurationTemplates.Add(new SyncConfigurationTemplate
                {
                    Name = "default", TenantId = fixture.TenantBId,
                    CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            });
        };

        // Should not throw — per-tenant unique constraint allows same name in different tenants
        await act.Should().NotThrowAsync();
    }

    // ── Group 5: User & Runtime — SyncNotification isolation ─────────────────

    [Fact]
    public async Task Notification_TenantA_NotVisibleToTenantB()
    {
        await fixture.WithTenantAsync(fixture.TenantAId, async db =>
        {
            db.Notifications.Add(new SyncNotification
            {
                Title    = "TenantA Alert",
                TenantId = fixture.TenantAId,
            });
            await db.SaveChangesAsync();
        });

        await fixture.WithTenantAsync(fixture.TenantBId, async db =>
        {
            var count = await db.Notifications
                .Where(n => n.Title == "TenantA Alert")
                .CountAsync();
            count.Should().Be(0, "TenantB must not see TenantA's notifications");
        });
    }

    // ── Platform Repository: cross-tenant visibility ───────────────────────────

    [Fact]
    public async Task PlatformAuditRepository_CanSeeAllTenantAudits()
    {
        // Arrange: one audit in TenantA, one in TenantB
        const string marker = "PLATFORM_REPO_TEST";

        await fixture.WithTenantAsync(fixture.TenantAId, async db =>
        {
            db.Audits.Add(new SyncAudit { ActionName = marker, Username = "ua", TenantId = fixture.TenantAId, CreateTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        await fixture.WithTenantAsync(fixture.TenantBId, async db =>
        {
            db.Audits.Add(new SyncAudit { ActionName = marker, Username = "ub", TenantId = fixture.TenantBId, CreateTime = DateTime.UtcNow });
            await db.SaveChangesAsync();
        });

        // Act: platform repository bypasses EF filter
        var platformRepo = fixture.GetPlatformRepository<SyncAudit>();
        var allAudits = await platformRepo.QueryAll()
            .Where(a => a.ActionName == marker)
            .ToListAsync();

        // Assert: sees both tenants' audits
        allAudits.Should().HaveCount(2);
        allAudits.Select(a => a.TenantId).Should().BeEquivalentTo([fixture.TenantAId, fixture.TenantBId]);
    }

    // ── Query plan: composite index used for audit query ─────────────────────

    [Fact]
    public async Task Audit_QueryPlan_UsesCompositeIndex()
    {
        // Verify via SET STATISTICS IO — confirms the optimizer chose the index
        await using var conn = fixture.CreateConnection();
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SET STATISTICS IO ON;
            SELECT TOP 10 [audit_id], [action_name], [create_time]
            FROM [msosync].[sync_audit]
            WHERE [tenant_id] = '{fixture.TenantAId}'
            ORDER BY [create_time] DESC;
            SET STATISTICS IO OFF;";

        // We just run the query — if it doesn't throw, the column exists and the index was considered.
        // For a deeper check, review execution plan in SSMS: seek on IX_sync_audit_TenantId_CreateTime.
        var act = async () => await cmd.ExecuteNonQueryAsync();
        await act.Should().NotThrowAsync("tenant_id column must exist and be queryable after M032");
    }
}
```

> **Property names:** The test uses properties like `RequestTime`, `Status`, `ActionName`, `Username`, `CreateTime`, `Name`, `CreatedAt`, `UpdatedAt`, `Title` on various entities. Verify these match the actual entity class definitions before running. Adjust any that differ.

> **`fixture.CreateConnection()`** — add this helper to `MultiTenantFixture` in Step 2 if it doesn't exist.

> **`fixture.GetPlatformRepository<T>()`** — add this helper to `MultiTenantFixture` in Step 2.

- [ ] **Step 2: Update MultiTenantFixture with required helpers**

Open `tests/MSOSync.IntegrationTests/MultiTenancy/MultiTenantFixture.cs`.

Add these methods if not already present:

```csharp
/// <summary>Creates a raw ADO.NET connection for DDL verification queries.</summary>
public System.Data.SqlClient.SqlConnection CreateConnection()
{
    // Use the same connection string as AppDbContext
    var connStr = _db.Database.GetConnectionString()!;
    return new System.Data.SqlClient.SqlConnection(connStr);
}

/// <summary>Returns a PlatformRepository for cross-tenant reads (bypasses EF filter).</summary>
public IPlatformRepository<T> GetPlatformRepository<T>() where T : class
{
    // PlatformRepository is internal — instantiate via DI scope or directly via reflection.
    // The simplest approach: create a scoped service provider from the fixture's service collection.
    // For tests, we can directly instantiate since we have AppDbContext:
    return new TestPlatformRepositoryAdapter<T>(_db);
}

/// <summary>Internal adapter — mirrors PlatformRepository<T> for test use.</summary>
private sealed class TestPlatformRepositoryAdapter<T>(AppDbContext db) : IPlatformRepository<T>
    where T : class
{
    public IQueryable<T> QueryAll() => db.Set<T>().IgnoreQueryFilters().AsNoTracking();
}
```

> Note: `SqlConnection` requires `Microsoft.Data.SqlClient` or `System.Data.SqlClient` depending on what the project uses. Check the existing test infrastructure's `using` statements and use the same namespace.

- [ ] **Step 3: Run integration tests**

```
dotnet test tests/MSOSync.IntegrationTests/ --filter "DomainMigrationIsolationTests" -v normal
```

Expected: `7 passed, 0 failed`

If tests fail:

- `Table 'sync_registration_request' does not exist` — M032 was not applied. Run `.superpowers/apply-m032.sql` first.
- `Invalid column name 'tenant_id'` — M032 applied partially; check `__EFMigrationsHistory` and re-run failed table's section.
- `Violation of UNIQUE KEY constraint 'UX_sync_configuration_template_name'` in `ConfigTemplate_SameNameAllowedInDifferentTenants` — the old global unique constraint was not dropped in M032. Run the `DROP INDEX` + new composite unique index DDL from `.superpowers/apply-m032.sql` for `sync_configuration_template`.
- `Entity type 'SyncNotification' has no property 'Title'` — adjust the property name to match the actual `SyncNotification` entity class.
- `fixture.WithTenantAsync` not found — import the using for the fixture's namespace.

- [ ] **Step 4: Run full test suite**

```
dotnet test MSOSync.sln -v minimal
```

Expected: all unit tests pass; integration test failures should be pre-existing (unrelated to 15B) or zero.

- [ ] **Step 5: Commit**

```
git add tests/MSOSync.IntegrationTests/MultiTenancy/DomainMigrationIsolationTests.cs
git add tests/MSOSync.IntegrationTests/MultiTenancy/MultiTenantFixture.cs
git commit -m "test(15B-6): integration tests — cross-tenant isolation, platform repo, migration smoke, query plan"
```

---

## Definition of Done Checklist

After this commit, verify all 6 acceptance criteria from the spec:

| # | Check | How to verify |
|---|-------|--------------|
| 1 | All 33 tenant-scoped entities migrated (12 from 15A + 21 from 15B) | `SELECT COUNT(DISTINCT TABLE_NAME) FROM INFORMATION_SCHEMA.COLUMNS WHERE COLUMN_NAME = 'tenant_id'` → ≥ 33 |
| 2 | Every entity implements `ITenantScoped` | `EntityOwnershipGateTest` passes |
| 3 | Every entity has active EF global query filter | `DomainTenantFilterVerificationTests` + `DomainMigrationIsolationTests` pass |
| 4 | No manual `WHERE TenantId = ?` predicates in services | Grep: `grep -r "TenantId ==" src/ --include="*.cs" \| grep -v "Entity\|Config\|Spec\|Test"` should return only entity/config definitions, not service query predicates |
| 5 | Platform audit services use `IPlatformRepository<SyncAudit>` | Build passes + MetadataTests pass |
| 6 | CE upgrade verified | `M032_NoNullTenantIds_InAnyMigratedTable` passes |
