# Task 1 — Entity + Migration

**Plan:** `2026-07-23-phase-2C-2-master.md`
**Scope:** `SyncMarketplaceCache` entity, EF configuration, M037 migration, `AppDbContext` DbSet addition, persistence test table-count update.

---

## Step 1.1 — Create the entity

- [ ] Create `src/MSOSync.Persistence/Entities/SyncMarketplaceCache.cs`:

```csharp
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

/// <summary>
/// Local cache of remote marketplace registry entries.
/// One row per plugin ID per registry source.
/// </summary>
[GlobalEntity]
public sealed class SyncMarketplaceCache
{
    /// <summary>Surrogate PK (int identity for fast seek).</summary>
    public int Id { get; set; }

    /// <summary>Registry base URL (normalized, trailing slash stripped).</summary>
    public string RegistryUrl { get; set; } = null!;

    /// <summary>Plugin ID as returned by the registry.</summary>
    public string PluginId { get; set; } = null!;

    /// <summary>Latest version string from the registry at cache time.</summary>
    public string LatestVersion { get; set; } = null!;

    /// <summary>JSON-serialized RegistryPluginEntry — full metadata blob.</summary>
    public string MetadataJson { get; set; } = null!;

    /// <summary>UTC timestamp when this entry was written or refreshed.</summary>
    public DateTime CachedAt { get; set; }

    /// <summary>UTC timestamp after which this entry is stale.</summary>
    public DateTime ExpiresAt { get; set; }
}
```

> Constraint: `[GlobalEntity]` attribute is required — same pattern as `SyncPlugin`. Without it the tenant query filter would apply and `MarketplaceCacheStore` reads would silently return no rows.

---

## Step 1.2 — Create the EF configuration

- [ ] Create `src/MSOSync.Persistence/Configurations/SyncMarketplaceCacheConfiguration.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncMarketplaceCacheConfiguration : IEntityTypeConfiguration<SyncMarketplaceCache>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncMarketplaceCache> builder)
    {
        builder.ToTable("sync_marketplace_cache", Schema);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
               .HasColumnName("id")
               .UseIdentityColumn();

        builder.Property(e => e.RegistryUrl)
               .HasColumnName("registry_url")
               .HasColumnType("nvarchar(500)")
               .HasMaxLength(500)
               .IsRequired();

        builder.Property(e => e.PluginId)
               .HasColumnName("plugin_id")
               .HasColumnType("nvarchar(200)")
               .HasMaxLength(200)
               .IsRequired();

        builder.Property(e => e.LatestVersion)
               .HasColumnName("latest_version")
               .HasColumnType("nvarchar(50)")
               .HasMaxLength(50)
               .IsRequired();

        builder.Property(e => e.MetadataJson)
               .HasColumnName("metadata_json")
               .HasColumnType("nvarchar(max)")
               .IsRequired();

        builder.Property(e => e.CachedAt)
               .HasColumnName("cached_at")
               .HasColumnType("datetime2")
               .IsRequired();

        builder.Property(e => e.ExpiresAt)
               .HasColumnName("expires_at")
               .HasColumnType("datetime2")
               .IsRequired();

        // One cache entry per (registry, pluginId)
        builder.HasIndex(e => new { e.RegistryUrl, e.PluginId })
               .IsUnique()
               .HasDatabaseName("IX_sync_marketplace_cache_registry_plugin");

        // Expiry-based sweep index
        builder.HasIndex(e => e.ExpiresAt)
               .HasDatabaseName("IX_sync_marketplace_cache_expires_at");
    }
}
```

---

## Step 1.3 — Add DbSet to AppDbContext

- [ ] Open `src/MSOSync.Persistence/AppDbContext.cs`
- [ ] Add the following line after the existing `Plugins` DbSet (line 67):

```csharp
public DbSet<SyncMarketplaceCache> MarketplaceCache => Set<SyncMarketplaceCache>();
```

---

## Step 1.4 — Write migration M037_MarketplaceCache

- [ ] Create `src/MSOSync.Persistence/Migrations/M037_MarketplaceCache.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M037_MarketplaceCache : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_marketplace_cache",
            schema: Schema,
            columns: t => new
            {
                id             = t.Column<int>(type: "int", nullable: false)
                                  .Annotation("SqlServer:Identity", "1, 1"),
                registry_url   = t.Column<string>(type: "nvarchar(500)",  maxLength: 500,  nullable: false),
                plugin_id      = t.Column<string>(type: "nvarchar(200)",  maxLength: 200,  nullable: false),
                latest_version = t.Column<string>(type: "nvarchar(50)",   maxLength: 50,   nullable: false),
                metadata_json  = t.Column<string>(type: "nvarchar(max)",               nullable: false),
                cached_at      = t.Column<DateTime>(type: "datetime2",                nullable: false),
                expires_at     = t.Column<DateTime>(type: "datetime2",                nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_sync_marketplace_cache", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sync_marketplace_cache_registry_plugin",
            schema: Schema,
            table: "sync_marketplace_cache",
            columns: new[] { "registry_url", "plugin_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sync_marketplace_cache_expires_at",
            schema: Schema,
            table: "sync_marketplace_cache",
            column: "expires_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sync_marketplace_cache",
            schema: Schema);
    }
}
```

- [ ] Create the corresponding designer file `src/MSOSync.Persistence/Migrations/M037_MarketplaceCache.Designer.cs`:

```csharp
// <auto-generated />
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("M037_MarketplaceCache")]
partial class M037_MarketplaceCache
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // Minimal designer stub — EF only needs this for snapshot diffs.
        // Full model is driven by AppDbContext.OnModelCreating via ApplyConfigurationsFromAssembly.
    }
}
```

> Note: The project uses a custom migration pattern (hand-written migrations without a `ModelSnapshot`). Follow the exact same minimal designer stub pattern used in `M034_BatchReplay.Designer.cs`.

---

## Step 1.5 — Register migration in the migration list

- [ ] Verify that `AppDbContext` uses `ApplyConfigurationsFromAssembly` (confirmed at line 100 of `AppDbContext.cs`). The EF migration runner discovers migrations by assembly scan — no explicit migration list to update.

- [ ] Check if there is a `MigrationContext` or `DesignTimeMigrationFactory` that needs updating:

```powershell
Select-String -Path "src\MSOSync.Persistence\**\*.cs" -Pattern "MigrationsAssembly|GetMigrations" -Recurse
```

If found, add `"M037_MarketplaceCache"` at the end of the sequence. If not found, no change required.

---

## Step 1.6 — Update the persistence table-count test

- [ ] Open `tests/MSOSync.IntegrationTests/PersistenceTests.cs`
- [ ] Find the test `SchemaCreated_All48TablesExist` (currently at line 20)
- [ ] Change `48` → `49` in both the method name and the assertion:

```csharp
[Fact]
public async Task SchemaCreated_All49TablesExist()
{
    var count = await fixture.Db.Database
        .SqlQuery<int>($"SELECT COUNT(1) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'msosync'")
        .SingleAsync();
    count.Should().Be(49);
}
```

---

## Step 1.7 — Build check

- [ ] Run:

```powershell
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj --no-restore
```

Expected: 0 errors. The new entity, configuration, and DbSet must compile cleanly.

- [ ] Verify EF sees the migration (design-time only — no DB required):

```powershell
dotnet ef migrations list --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```

`M037_MarketplaceCache` must appear in the output.
