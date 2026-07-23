# Task 2: SqlDistributedLockService + M035 Migration + DI Extension

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the SQL-backed distributed lock (replaces `DatabaseLockProvider`), add M035 migration that adds `lock_expiry` to `sync_lock`, and wire everything through `AddDistributedLocks`.

**Prerequisite:** Task 1 complete (`IDistributedLockService`, `IDistributedLock`, `DistributedLockOptions` exist in `MSOSync.Common.Locks`).

**Files:**
- Create: `src/MSOSync.Persistence/Lock/SqlDistributedLock.cs`
- Create: `src/MSOSync.Persistence/Lock/SqlDistributedLockService.cs`
- Create: `src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs`
- Create: `src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.cs`
- Create: `src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.Designer.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncLock.cs` — add `LockExpiry` property
- Modify: `src/MSOSync.Persistence/Configurations/SyncLockConfiguration.cs` — map `lock_expiry`
- Modify: `src/MSOSync.Persistence/PersistenceServiceExtensions.cs` — call `AddDistributedLocks`
- Modify: `src/MSOSync.Persistence/MSOSync.Persistence.csproj` — add StackExchange.Redis
- Modify: `src/MSOSync.App/appsettings.json` — add `"DistributedLocks"` section
- Test: `tests/MSOSync.Tests/Lock/SqlDistributedLockServiceTests.cs`
- Test: `tests/MSOSync.IntegrationTests/Lock/SqlDistributedLockIntegrationTests.cs`
- Modify: `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj` — reference already present

**Interfaces:**
- Consumes: `IDistributedLockService`, `IDistributedLock`, `DistributedLockOptions` from Task 1
- Produces:
  - `SqlDistributedLockService` implementing `IDistributedLockService` — registered as scoped
  - `DistributedLockServiceExtensions.AddDistributedLocks(IServiceCollection, IConfiguration)` — called in `PersistenceServiceExtensions`
  - `SyncLock.LockExpiry: DateTime?` — available to `LockAdminService` (Task 4)

---

- [ ] **Step 1: Add StackExchange.Redis to MSOSync.Persistence.csproj**

Edit `src/MSOSync.Persistence/MSOSync.Persistence.csproj` — add inside the first `<ItemGroup>` that has `PackageReference` entries:

```xml
<PackageReference Include="StackExchange.Redis" Version="2.8.16" />
```

The full `<ItemGroup>` after the change:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" />
  <PackageReference Include="Microsoft.EntityFrameworkCore.Design">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.EntityFrameworkCore.Tools">
    <PrivateAssets>all</PrivateAssets>
    <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
  </PackageReference>
  <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks" />
  <PackageReference Include="StackExchange.Redis" Version="2.8.16" />
</ItemGroup>
```

Run `dotnet restore src/MSOSync.Persistence/MSOSync.Persistence.csproj` to confirm the package resolves.

- [ ] **Step 2: Add `LockExpiry` to `SyncLock` entity**

Edit `src/MSOSync.Persistence/Entities/SyncLock.cs`. Current file:

```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public enum LockScope { Platform, Tenant }

[GlobalEntity]
public sealed class SyncLock
{
    public string LockName { get; set; } = null!;
    public string? LockOwner { get; set; }
    public DateTime? LockTime { get; set; }
    public LockScope Scope { get; set; } = LockScope.Platform;
}
```

Replace with:

```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

public enum LockScope { Platform, Tenant }

[GlobalEntity]
public sealed class SyncLock
{
    public string    LockName   { get; set; } = null!;
    public string?   LockOwner  { get; set; }
    public DateTime? LockTime   { get; set; }
    public DateTime? LockExpiry { get; set; }
    public LockScope Scope      { get; set; } = LockScope.Platform;
}
```

- [ ] **Step 3: Map `lock_expiry` in `SyncLockConfiguration`**

Edit `src/MSOSync.Persistence/Configurations/SyncLockConfiguration.cs`. Current `Configure` body ends with the `Scope` mapping. Add after it:

```csharp
builder.Property(e => e.LockExpiry)
    .HasColumnName("lock_expiry")
    .HasColumnType("datetime2(7)");
```

Full file after edit:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncLockConfiguration : IEntityTypeConfiguration<SyncLock>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncLock> builder)
    {
        builder.ToTable("sync_lock", Schema);
        builder.HasKey(e => e.LockName);

        builder.Property(e => e.LockName).HasColumnName("lock_name").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.LockOwner).HasColumnName("lock_owner").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false);
        builder.Property(e => e.LockTime).HasColumnName("lock_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.LockExpiry).HasColumnName("lock_expiry").HasColumnType("datetime2(7)");

        // M031 — lock scope (0 = Platform, 1 = Tenant)
        builder.Property(e => e.Scope)
            .HasColumnName("lock_scope")
            .HasColumnType("int")
            .IsRequired()
            .HasDefaultValue(LockScope.Platform);
    }
}
```

- [ ] **Step 4: Write the M035 migration**

Create `src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("M035_DistributedLockExpiry")]
public partial class M035_DistributedLockExpiry : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name:     "lock_expiry",
            schema:   Schema,
            table:    "sync_lock",
            type:     "datetime2(7)",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name:   "lock_expiry",
            schema: Schema,
            table:  "sync_lock");
    }
}
```

- [ ] **Step 5: Write the M035 Designer stub**

Create `src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.Designer.cs`:

```csharp
// <auto-generated />
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("M035_DistributedLockExpiry")]
    partial class M035_DistributedLockExpiry
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // EF uses AppDbContextModelSnapshot.cs at runtime; this stub satisfies the migration runner.
        }
    }
}
```

- [ ] **Step 6: Write `SqlDistributedLock.cs`**

Create `src/MSOSync.Persistence/Lock/SqlDistributedLock.cs`:

```csharp
using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

internal sealed class SqlDistributedLock : IDistributedLock
{
    private readonly SqlDistributedLockService _service;
    private bool _disposed;

    public string         Resource  { get; }
    public string         Owner     { get; }
    public DateTimeOffset ExpiresAt { get; }

    internal SqlDistributedLock(
        SqlDistributedLockService service,
        string resource,
        string owner,
        DateTimeOffset expiresAt)
    {
        _service  = service;
        Resource  = resource;
        Owner     = owner;
        ExpiresAt = expiresAt;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _service.ReleaseAsync(Resource, Owner, CancellationToken.None);
    }
}
```

- [ ] **Step 7: Write the failing tests for SqlDistributedLockService**

Create `tests/MSOSync.Tests/Lock/SqlDistributedLockServiceTests.cs`.

These tests require a real SQL Server because `ExecuteSqlRawAsync` does not work with EF InMemory. We use Testcontainers to spin up a SQL Server LocalDB-compatible container. Check that `MSOSync.Tests.csproj` already references `MSOSync.Persistence`. If it does not include `Testcontainers.MsSql`, add it — but first check the existing csproj at `tests/MSOSync.Tests/MSOSync.Tests.csproj`:

The current `MSOSync.Tests.csproj` only has `Microsoft.EntityFrameworkCore.InMemory`. For the SQL-level tests we need `Testcontainers.MsSql`. Add it:

```xml
<PackageReference Include="Testcontainers.MsSql" Version="3.10.0" />
<PackageReference Include="Moq" />
```

Full updated `tests/MSOSync.Tests/MSOSync.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.InMemory" />
    <PackageReference Include="Testcontainers.MsSql" Version="3.10.0" />
    <PackageReference Include="Moq" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
  </ItemGroup>
</Project>
```

Now write the test file `tests/MSOSync.Tests/Lock/SqlDistributedLockServiceTests.cs`:

```csharp
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.Tests.Lock;

[Collection("SqlLock")]
public sealed class SqlDistributedLockServiceTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_container.GetConnectionString())
            .Options;

        _db = new AppDbContext(options);
        await _db.Database.MigrateAsync();

        // Seed a single lock row for each lock name under test
        await _db.Database.ExecuteSqlRawAsync(
            "IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'TEST_LOCK') " +
            "INSERT INTO [msosync].[sync_lock] (lock_name, lock_scope) VALUES ('TEST_LOCK', 0)");
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _container.DisposeAsync();
    }

    private SqlDistributedLockService Svc() => new(_db);
    private static TimeSpan Expiry30s => TimeSpan.FromSeconds(30);

    // ── Reset helper ─────────────────────────────────────────────────────────
    private async Task ResetLockAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL, lock_expiry = NULL " +
            "WHERE lock_name = 'TEST_LOCK'");
    }

    // ── TryAcquireAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_ReturnsHandle_WhenLockFree()
    {
        await ResetLockAsync();

        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);

        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("TEST_LOCK");
        handle.Owner.Should().Be("OWNER1");
        handle.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(5));

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_ReturnsNull_WhenLockHeld()
    {
        await ResetLockAsync();
        var first = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        first.Should().NotBeNull();

        var second = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER2", Expiry30s);

        second.Should().BeNull();

        await first!.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_Acquires_WhenLockExpired()
    {
        await ResetLockAsync();
        // Plant an expired lock directly
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = 'STALE', lock_time = DATEADD(SECOND, -60, GETUTCDATE()), " +
            "    lock_expiry = DATEADD(SECOND, -30, GETUTCDATE()) " +
            "WHERE lock_name = 'TEST_LOCK'");

        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER2", Expiry30s);

        handle.Should().NotBeNull();
        handle!.Owner.Should().Be("OWNER2");

        await handle.DisposeAsync();
    }

    [Fact]
    public async Task TryAcquireAsync_Acquires_WhenLegacyStaleLock()
    {
        await ResetLockAsync();
        // Lock with no expiry column value but lock_time 11 minutes ago (legacy stale)
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = 'LEGACY', lock_time = DATEADD(MINUTE, -11, GETUTCDATE()), " +
            "    lock_expiry = NULL " +
            "WHERE lock_name = 'TEST_LOCK'");

        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER3", Expiry30s);

        handle.Should().NotBeNull();

        await handle!.DisposeAsync();
    }

    // ── RenewAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenewAsync_ReturnsTrue_WhenOwnerMatches()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        var renewed = await Svc().RenewAsync("TEST_LOCK", "OWNER1", TimeSpan.FromMinutes(5));

        renewed.Should().BeTrue();

        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task RenewAsync_ReturnsFalse_WhenOwnerMismatch()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        var renewed = await Svc().RenewAsync("TEST_LOCK", "WRONG_OWNER", TimeSpan.FromMinutes(5));

        renewed.Should().BeFalse();

        await handle!.DisposeAsync();
    }

    // ── ReleaseAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseAsync_ClearsRow_WhenOwnerMatches()
    {
        await ResetLockAsync();
        await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);

        await Svc().ReleaseAsync("TEST_LOCK", "OWNER1");

        var isHeld = await Svc().IsHeldAsync("TEST_LOCK");
        isHeld.Should().BeFalse();
    }

    [Fact]
    public async Task ReleaseAsync_NoOp_WhenOwnerMismatch()
    {
        await ResetLockAsync();
        await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);

        await Svc().ReleaseAsync("TEST_LOCK", "WRONG_OWNER");

        var isHeld = await Svc().IsHeldAsync("TEST_LOCK");
        isHeld.Should().BeTrue();   // still held by OWNER1

        await Svc().ReleaseAsync("TEST_LOCK", "OWNER1");
    }

    // ── IsHeldAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IsHeldAsync_ReturnsTrue_WhenActiveOwner()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        var held = await Svc().IsHeldAsync("TEST_LOCK");

        held.Should().BeTrue();
        await handle!.DisposeAsync();
    }

    [Fact]
    public async Task IsHeldAsync_ReturnsFalse_WhenExpired()
    {
        await ResetLockAsync();
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] " +
            "SET lock_owner = 'EXPIRED', lock_expiry = DATEADD(SECOND, -1, GETUTCDATE()) " +
            "WHERE lock_name = 'TEST_LOCK'");

        var held = await Svc().IsHeldAsync("TEST_LOCK");

        held.Should().BeFalse();
    }

    // ── DisposeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_ReleasesLock()
    {
        await ResetLockAsync();
        var handle = await Svc().TryAcquireAsync("TEST_LOCK", "OWNER1", Expiry30s);
        handle.Should().NotBeNull();

        await handle!.DisposeAsync();

        var held = await Svc().IsHeldAsync("TEST_LOCK");
        held.Should().BeFalse();
    }
}

[CollectionDefinition("SqlLock")]
public sealed class SqlLockCollectionDefinition { }
```

- [ ] **Step 8: Run the failing tests**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "FullyQualifiedName~SqlDistributedLockServiceTests" -v minimal
```

Expected: compilation errors because `SqlDistributedLockService` does not exist yet. That's correct — we're writing failing tests first.

- [ ] **Step 9: Write `SqlDistributedLockService.cs`**

Create `src/MSOSync.Persistence/Lock/SqlDistributedLockService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

public sealed class SqlDistributedLockService(AppDbContext db) : IDistributedLockService
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var expiryMs = (long)expiry.TotalMilliseconds;

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE(), " +
            "    lock_expiry = DATEADD(ms, {1}, GETUTCDATE()) " +
            "WHERE lock_name = {2} " +
            "  AND (lock_owner IS NULL " +
            "    OR (lock_expiry IS NULL AND lock_time < DATEADD(MINUTE, -10, GETUTCDATE())) " +
            "    OR (lock_expiry IS NOT NULL AND lock_expiry < GETUTCDATE()))",
            new object[] { owner, expiryMs, resource }, ct);

        if (rows != 1) return null;

        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        return new SqlDistributedLock(this, resource, owner, expiresAt);
    }

    public async Task<bool> RenewAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var expiryMs = (long)expiry.TotalMilliseconds;

        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_expiry = DATEADD(ms, {0}, GETUTCDATE()) " +
            "WHERE lock_name = {1} AND lock_owner = {2}",
            new object[] { expiryMs, resource, owner }, ct);

        return rows == 1;
    }

    public async Task ReleaseAsync(
        string resource, string owner, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL, lock_expiry = NULL " +
            "WHERE lock_name = {0} AND lock_owner = {1}",
            new object[] { resource, owner }, ct);
    }

    public async Task<bool> IsHeldAsync(
        string resource, CancellationToken ct = default)
    {
        var count = await db.Locks
            .AsNoTracking()
            .Where(l => l.LockName == resource
                     && l.LockOwner != null
                     && (l.LockExpiry == null || l.LockExpiry > DateTime.UtcNow))
            .CountAsync(ct);
        return count > 0;
    }
}
```

- [ ] **Step 10: Run the unit tests again**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "FullyQualifiedName~SqlDistributedLockServiceTests" -v minimal
```

Expected: `Passed: 11, Failed: 0`

> Note: Testcontainers will pull a SQL Server Docker image on first run. Docker must be running. If Docker is unavailable in this environment, skip to Step 11 and confirm the integration tests pass on a machine with Docker.

- [ ] **Step 11: Write the integration test for concurrency**

Create `tests/MSOSync.IntegrationTests/Lock/SqlDistributedLockIntegrationTests.cs`.

The `MSOSync.IntegrationTests.csproj` already references `MSOSync.Persistence` and has `Testcontainers.MsSql`. No csproj changes needed.

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using Testcontainers.MsSql;
using Xunit;

namespace MSOSync.IntegrationTests.Lock;

[Collection("LockIntegration")]
public sealed class SqlDistributedLockIntegrationTests : IAsyncLifetime
{
    private readonly MsSqlContainer _container = new MsSqlBuilder().Build();
    private string _connStr = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        _connStr = _container.GetConnectionString();

        // Apply migrations once
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_connStr)
            .Options;
        await using var db = new AppDbContext(opts);
        await db.Database.MigrateAsync();

        // Seed lock row
        await db.Database.ExecuteSqlRawAsync(
            "IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_lock] WHERE lock_name = 'CONCURRENT_LOCK') " +
            "INSERT INTO [msosync].[sync_lock] (lock_name, lock_scope) VALUES ('CONCURRENT_LOCK', 0)");
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();

    private AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(_connStr).Options);

    [Fact]
    public async Task TwoCallers_OnlyOneAcquires()
    {
        await using var db1 = NewDb();
        await using var db2 = NewDb();
        var svc1 = new SqlDistributedLockService(db1);
        var svc2 = new SqlDistributedLockService(db2);

        // Reset
        await db1.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] SET lock_owner=NULL,lock_time=NULL,lock_expiry=NULL " +
            "WHERE lock_name='CONCURRENT_LOCK'");

        // Both attempt concurrently
        var t1 = svc1.TryAcquireAsync("CONCURRENT_LOCK", "CALLER1", TimeSpan.FromSeconds(30));
        var t2 = svc2.TryAcquireAsync("CONCURRENT_LOCK", "CALLER2", TimeSpan.FromSeconds(30));
        var results = await Task.WhenAll(t1, t2);

        var acquired = results.Where(r => r is not null).ToList();
        acquired.Should().HaveCount(1);

        // Cleanup
        foreach (var h in results.Where(r => r is not null))
            await h!.DisposeAsync();
    }

    [Fact]
    public async Task ExpiredLock_StolenBySecondCaller()
    {
        await using var db1 = NewDb();
        await using var db2 = NewDb();
        var svc1 = new SqlDistributedLockService(db1);
        var svc2 = new SqlDistributedLockService(db2);

        // Reset
        await db1.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] SET lock_owner=NULL,lock_time=NULL,lock_expiry=NULL " +
            "WHERE lock_name='CONCURRENT_LOCK'");

        // First caller acquires with 100ms TTL
        var handle1 = await svc1.TryAcquireAsync("CONCURRENT_LOCK", "CALLER1", TimeSpan.FromMilliseconds(100));
        handle1.Should().NotBeNull();

        // Wait for expiry
        await Task.Delay(250);

        // Second caller steals the expired lock
        var handle2 = await svc2.TryAcquireAsync("CONCURRENT_LOCK", "CALLER2", TimeSpan.FromSeconds(30));
        handle2.Should().NotBeNull();
        handle2!.Owner.Should().Be("CALLER2");

        await handle2.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_ReleasesLock_AllowingReacquisition()
    {
        await using var db = NewDb();
        var svc = new SqlDistributedLockService(db);

        // Reset
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_lock] SET lock_owner=NULL,lock_time=NULL,lock_expiry=NULL " +
            "WHERE lock_name='CONCURRENT_LOCK'");

        var handle = await svc.TryAcquireAsync("CONCURRENT_LOCK", "OWNER1", TimeSpan.FromSeconds(30));
        handle.Should().NotBeNull();

        await handle!.DisposeAsync();

        // Re-acquire immediately after dispose
        var handle2 = await svc.TryAcquireAsync("CONCURRENT_LOCK", "OWNER1", TimeSpan.FromSeconds(30));
        handle2.Should().NotBeNull();

        await handle2!.DisposeAsync();
    }
}

[CollectionDefinition("LockIntegration")]
public sealed class LockIntegrationCollectionDefinition { }
```

- [ ] **Step 12: Write `DistributedLockServiceExtensions.cs`**

Create `src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Locks;
using StackExchange.Redis;

namespace MSOSync.Persistence.Lock;

public static class DistributedLockServiceExtensions
{
    public static IServiceCollection AddDistributedLocks(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<DistributedLockOptions>(
            configuration.GetSection(DistributedLockOptions.SectionName));

        var provider = configuration
            .GetSection(DistributedLockOptions.SectionName)["Provider"] ?? "Sql";

        if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            // IConnectionMultiplexer must already be registered by the caller
            // (e.g., via AddSingleton<IConnectionMultiplexer>(...) in Phase 2D.1).
            services.AddScoped<IDistributedLockService, RedisDistributedLockService>();
        }
        else
        {
            services.AddScoped<IDistributedLockService, SqlDistributedLockService>();
        }

        return services;
    }
}
```

- [ ] **Step 13: Update `PersistenceServiceExtensions` to call `AddDistributedLocks`**

Edit `src/MSOSync.Persistence/PersistenceServiceExtensions.cs`.

Replace the line:
```csharp
services.AddScoped<IDatabaseLockProvider, DatabaseLockProvider>();
```
with:
```csharp
services.AddDistributedLocks(configuration);
```

The `AddPersistence` method signature must accept `IConfiguration configuration` — it already does. The full file after the change:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Persistence.Lock;
using MSOSync.Persistence.Queries;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Persistence;

public static class PersistenceServiceExtensions
{
    public static IServiceCollection AddPersistence(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required");

        services.AddDbContext<AppDbContext>(opts =>
            opts.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable("__EFMigrationsHistory", schema))
                .ConfigureWarnings(w => w.Ignore(
                    Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning)));

        services.AddScoped<GetPendingBatchesQuery>();
        services.AddScoped<GetOfflineNodesQuery>();
        services.AddScoped<GetRetryCandidatesQuery>();
        services.AddScoped<GetEventQueueDepthQuery>();
        services.AddScoped<GetNodeByIdQuery>();
        services.AddScoped<GetNodeSecurityQuery>();
        services.AddScoped<GetUserByUsernameQuery>();

        services.AddDistributedLocks(configuration);

        // Tenancy repositories (internal types — registered here to stay within the assembly)
        services.AddScoped<IHybridLookupService, HybridLookupService>();
        services.AddScoped(typeof(IPlatformRepository<>), typeof(PlatformRepository<>));

        services.AddHealthChecks()
            .AddCheck<PersistenceHealthCheck>("database");

        return services;
    }
}
```

- [ ] **Step 14: Add `"DistributedLocks"` section to appsettings.json**

Edit `src/MSOSync.App/appsettings.json`. Add before the closing `}`:

```json
  "DistributedLocks": {
    "Provider": "Sql",
    "DefaultExpiry": "00:00:30",
    "RetryCount": 3,
    "RetryDelay": "00:00:00.200"
  }
```

Full file after the change:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  },
  "AllowedHosts": "*",
  "Node": {
    "NodeId": "",
    "GroupId": "",
    "SyncUrl": ""
  },
  "Jwt": {
    "Issuer": "msosync",
    "Audience": "msosync-dashboard",
    "AccessExpiryMinutes": 60,
    "RefreshExpiryDays": 7
  },
  "Heartbeat": {
    "IntervalSeconds": 30,
    "ProbeIntervalSeconds": 60,
    "StatusCheckIntervalSeconds": 60,
    "MissedThreshold": 3
  },
  "Sync": {
    "IntervalSeconds": 30,
    "PullIntervalSeconds": 10
  },
  "Export": {
    "ImmediateThreshold": 50000,
    "BasePath": "exports",
    "RetentionHours": 24,
    "MaxConcurrentJobs": 1
  },
  "Pagination": {
    "CursorHmacKey": "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="
  },
  "Lifecycle": {
    "RollingWorkerIntervalSeconds": 15
  },
  "Replay": {
    "MaxRangeDays": 90,
    "WorkerIntervalSeconds": 10,
    "MaxConcurrentOperations": 5,
    "ItemPageSize": 50
  },
  "DistributedLocks": {
    "Provider": "Sql",
    "DefaultExpiry": "00:00:30",
    "RetryCount": 3,
    "RetryDelay": "00:00:00.200"
  }
}
```

- [ ] **Step 15: Build the Persistence project**

The `DistributedLockServiceExtensions` written in Step 12 references `RedisDistributedLockService`, which is created in Task 3. To make the Task 2 build green without Task 3, write the extension with the SQL-only body (no Redis branch). Task 3 will replace it.

Edit `src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs` so the body is:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

public static class DistributedLockServiceExtensions
{
    public static IServiceCollection AddDistributedLocks(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<DistributedLockOptions>(
            configuration.GetSection(DistributedLockOptions.SectionName));

        // Redis branch added in Task 3 once RedisDistributedLockService exists.
        services.AddScoped<IDistributedLockService, SqlDistributedLockService>();

        return services;
    }
}
```

Then build:

```
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj
```

Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 16: Run all unit tests**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj -v minimal
```

Expected: all tests pass (including the new SQL tests and the DistributedLockHelper tests from Task 1).

- [ ] **Step 17: Run integration tests**

```
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "FullyQualifiedName~SqlDistributedLockIntegrationTests" -v minimal
```

Expected: `Passed: 3, Failed: 0`

- [ ] **Step 18: Commit**

```
git add src/MSOSync.Persistence/Entities/SyncLock.cs
git add src/MSOSync.Persistence/Configurations/SyncLockConfiguration.cs
git add src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.cs
git add src/MSOSync.Persistence/Migrations/M035_DistributedLockExpiry.Designer.cs
git add src/MSOSync.Persistence/Lock/SqlDistributedLock.cs
git add src/MSOSync.Persistence/Lock/SqlDistributedLockService.cs
git add src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs
git add src/MSOSync.Persistence/PersistenceServiceExtensions.cs
git add src/MSOSync.Persistence/MSOSync.Persistence.csproj
git add src/MSOSync.App/appsettings.json
git add tests/MSOSync.Tests/MSOSync.Tests.csproj
git add tests/MSOSync.Tests/Lock/SqlDistributedLockServiceTests.cs
git add tests/MSOSync.IntegrationTests/Lock/SqlDistributedLockIntegrationTests.cs
git commit -m "feat(2D.2-T2): SqlDistributedLockService + M035 migration + AddDistributedLocks DI extension"
```
