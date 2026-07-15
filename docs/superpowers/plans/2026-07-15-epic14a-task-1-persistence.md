# Epic 14A — Task 1: Persistence Layer

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans.

**Goal:** Create the `MSOSync.Plugin` project scaffold (interfaces + models needed by persistence), add the `sync_plugin` table via M029 migration, implement `PluginStore`, and seed the `MANAGE_PLUGINS` permission.

**Architecture:** `MSOSync.Plugin` references only `MSOSync.Common`. `MSOSync.Persistence` references `MSOSync.Plugin` for `IPluginStore`/`PluginRecord`. Both added to solution.

**Tech Stack:** C# 13 / .NET 9 / EF Core 9 / SQL Server / xUnit

## Global Constraints

- `MSOSync.Plugin` references ONLY `MSOSync.Common`
- Package versions come from `Directory.Packages.props` — no explicit versions in `.csproj`
- Schema = `Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync"`
- M029 adds 1 table → total 42 → 43; M029 also seeds `MANAGE_PLUGINS` permission (Admin-only)
- All projects target `net9.0`, `LangVersion 13.0`, `Nullable enable`, `ImplicitUsings enable` (inherited from `Directory.Build.props`)

## Files

**Create:**
- `src/MSOSync.Plugin/MSOSync.Plugin.csproj`
- `src/MSOSync.Plugin/Abstractions/IPluginStore.cs`
- `src/MSOSync.Plugin/Models/PluginRecord.cs`
- `src/MSOSync.Plugin/Models/PluginStatus.cs`
- `src/MSOSync.Persistence/Entities/SyncPlugin.cs`
- `src/MSOSync.Persistence/Configurations/SyncPluginConfiguration.cs`
- `src/MSOSync.Persistence/Migrations/M029_Plugins.cs`
- `src/MSOSync.Persistence/Migrations/M029_Plugins.Designer.cs`
- `src/MSOSync.Persistence/Stores/PluginStore.cs`

**Modify:**
- `src/MSOSync.Persistence/MSOSync.Persistence.csproj` — add ProjectReference to MSOSync.Plugin
- `src/MSOSync.Persistence/AppDbContext.cs` — add `DbSet<SyncPlugin>`
- `tests/MSOSync.IntegrationTests/PersistenceTests.cs` — update table count 42 → 43

## Interfaces

**Consumes:** `MSOSync.Common.Exceptions.NotFoundException` (for `SetEnabledAsync`)

**Produces:**
- `IPluginStore` (consumed by Tasks 4, 5, 7)
- `PluginRecord` (consumed by Tasks 4, 5)
- `PluginStatus` (consumed by Tasks 2, 3, 4, 5, 6, 7, 8)

---

- [ ] **Step 1: Create `src/MSOSync.Plugin/MSOSync.Plugin.csproj`**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add MSOSync.Plugin to solution**

```bash
dotnet sln D:\MSOSync\MSOSync.sln add src/MSOSync.Plugin/MSOSync.Plugin.csproj
```

Expected: `Project 'src\MSOSync.Plugin\MSOSync.Plugin.csproj' added to the solution.`

- [ ] **Step 3: Create `src/MSOSync.Plugin/Models/PluginStatus.cs`**

```csharp
namespace MSOSync.Plugin.Models;

public enum PluginStatus { Discovered, Validated, Loaded, Disabled, Failed }

public enum PluginLoadOutcome { Success, Skipped, Disabled, Failed }
```

- [ ] **Step 4: Create `src/MSOSync.Plugin/Models/PluginRecord.cs`**

```csharp
namespace MSOSync.Plugin.Models;

public sealed class PluginRecord
{
    public string   PluginId      { get; set; } = null!;
    public string   PluginName    { get; set; } = null!;
    public string   PluginVersion { get; set; } = null!;
    public string   Status        { get; set; } = null!;   // PluginStatus enum name
    public bool     Enabled       { get; set; } = true;
    public DateTime InstalledAt   { get; set; }
    public DateTime LastSeenAt    { get; set; }
    public string?  LastError     { get; set; }
    public string?  ManifestHash  { get; set; }
    public string?  HostVersion   { get; set; }
}
```

- [ ] **Step 5: Create `src/MSOSync.Plugin/Abstractions/IPluginStore.cs`**

```csharp
using MSOSync.Plugin.Models;

namespace MSOSync.Plugin.Abstractions;

public interface IPluginStore
{
    Task<IReadOnlyList<PluginRecord>> GetAllAsync(CancellationToken ct);
    Task UpsertAsync(PluginRecord record, CancellationToken ct);
    Task TouchAsync(string pluginId, CancellationToken ct);
    Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct);
}
```

- [ ] **Step 6: Create `src/MSOSync.Persistence/Entities/SyncPlugin.cs`**

```csharp
namespace MSOSync.Persistence.Entities;

public sealed class SyncPlugin
{
    public string   PluginId      { get; set; } = null!;
    public string   PluginName    { get; set; } = null!;
    public string   PluginVersion { get; set; } = null!;
    public string   Status        { get; set; } = null!;
    public bool     Enabled       { get; set; } = true;
    public DateTime InstalledAt   { get; set; }
    public DateTime LastSeenAt    { get; set; }
    public string?  LastError     { get; set; }
    public string?  ManifestHash  { get; set; }
    public string?  HostVersion   { get; set; }
}
```

- [ ] **Step 7: Create `src/MSOSync.Persistence/Configurations/SyncPluginConfiguration.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncPluginConfiguration : IEntityTypeConfiguration<SyncPlugin>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncPlugin> builder)
    {
        builder.ToTable("sync_plugin", Schema);
        builder.HasKey(e => e.PluginId);

        builder.Property(e => e.PluginId)
            .HasColumnName("plugin_id")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200);

        builder.Property(e => e.PluginName)
            .HasColumnName("plugin_name")
            .HasColumnType("nvarchar(200)")
            .HasMaxLength(200);

        builder.Property(e => e.PluginVersion)
            .HasColumnName("plugin_version")
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50);

        builder.Property(e => e.Status)
            .HasColumnName("status")
            .HasColumnType("nvarchar(20)")
            .HasMaxLength(20);

        builder.Property(e => e.Enabled)
            .HasColumnName("enabled")
            .HasColumnType("bit")
            .HasDefaultValue(true);

        builder.Property(e => e.InstalledAt)
            .HasColumnName("installed_at")
            .HasColumnType("datetime2");

        builder.Property(e => e.LastSeenAt)
            .HasColumnName("last_seen_at")
            .HasColumnType("datetime2");

        builder.Property(e => e.LastError)
            .HasColumnName("last_error")
            .HasColumnType("nvarchar(2000)")
            .HasMaxLength(2000);

        builder.Property(e => e.ManifestHash)
            .HasColumnName("manifest_hash")
            .HasColumnType("nvarchar(64)")
            .HasMaxLength(64);

        builder.Property(e => e.HostVersion)
            .HasColumnName("host_version")
            .HasColumnType("nvarchar(50)")
            .HasMaxLength(50);
    }
}
```

- [ ] **Step 8: Add `DbSet<SyncPlugin>` to `AppDbContext.cs`**

After the existing `DbSet<SyncUserNotification>` line, add:

```csharp
public DbSet<SyncPlugin> Plugins => Set<SyncPlugin>();
```

- [ ] **Step 9: Add ProjectReference to `MSOSync.Persistence.csproj`**

Open `src/MSOSync.Persistence/MSOSync.Persistence.csproj`. Add inside `<ItemGroup>` (or create one):

```xml
<ProjectReference Include="..\MSOSync.Plugin\MSOSync.Plugin.csproj" />
```

- [ ] **Step 10: Create `src/MSOSync.Persistence/Migrations/M029_Plugins.cs`**

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    public partial class M029_Plugins : Migration
    {
        private const string Schema = "msosync";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_plugin",
                schema: Schema,
                columns: table => new
                {
                    plugin_id      = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    plugin_name    = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    plugin_version = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: false),
                    status         = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,  nullable: false),
                    enabled        = table.Column<bool>  (type: "bit",                           nullable: false, defaultValue: true),
                    installed_at   = table.Column<DateTime>(type: "datetime2",                   nullable: false),
                    last_seen_at   = table.Column<DateTime>(type: "datetime2",                   nullable: false),
                    last_error     = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    manifest_hash  = table.Column<string>(type: "nvarchar(64)",  maxLength: 64,  nullable: true),
                    host_version   = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_plugin", x => x.plugin_id);
                });

            // Seed MANAGE_PLUGINS permission (Admin-only)
            migrationBuilder.InsertData(
                schema: Schema,
                table: "sync_permission",
                columns: ["PermissionKey", "DisplayName", "Description", "Category", "SortOrder", "IsSystem"],
                values: new object[,]
                {
                    { "MANAGE_PLUGINS", "Manage Plugins", "View and manage loaded plugins", "ADMINISTRATION", 50, true },
                });

            migrationBuilder.InsertData(
                schema: Schema,
                table: "sync_role_permission",
                columns: ["RoleName", "PermissionKey"],
                values: new object[,]
                {
                    { "ADMIN", "MANAGE_PLUGINS" },
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DeleteData(schema: Schema, table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "MANAGE_PLUGINS" });

            migrationBuilder.DeleteData(schema: Schema, table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "MANAGE_PLUGINS");

            migrationBuilder.DropTable(name: "sync_plugin", schema: Schema);
        }
    }
}
```

- [ ] **Step 11: Create `src/MSOSync.Persistence/Migrations/M029_Plugins.Designer.cs`**

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
    [Migration("M029_Plugins")]
    partial class M029_Plugins
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder) { }
    }
}
```

- [ ] **Step 12: Create `src/MSOSync.Persistence/Stores/PluginStore.cs`**

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Common.Exceptions;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Models;

namespace MSOSync.Persistence.Stores;

public sealed class PluginStore(AppDbContext db) : IPluginStore
{
    public async Task<IReadOnlyList<PluginRecord>> GetAllAsync(CancellationToken ct)
    {
        return await db.Plugins
            .AsNoTracking()
            .Select(p => new PluginRecord
            {
                PluginId      = p.PluginId,
                PluginName    = p.PluginName,
                PluginVersion = p.PluginVersion,
                Status        = p.Status,
                Enabled       = p.Enabled,
                InstalledAt   = p.InstalledAt,
                LastSeenAt    = p.LastSeenAt,
                LastError     = p.LastError,
                ManifestHash  = p.ManifestHash,
                HostVersion   = p.HostVersion,
            })
            .ToListAsync(ct);
    }

    public async Task UpsertAsync(PluginRecord record, CancellationToken ct)
    {
        var existing = await db.Plugins
            .FirstOrDefaultAsync(p => p.PluginId == record.PluginId, ct);

        if (existing == null)
        {
            db.Plugins.Add(new Entities.SyncPlugin
            {
                PluginId      = record.PluginId,
                PluginName    = record.PluginName,
                PluginVersion = record.PluginVersion,
                Status        = record.Status,
                Enabled       = record.Enabled,
                InstalledAt   = record.InstalledAt,
                LastSeenAt    = record.LastSeenAt,
                LastError     = record.LastError,
                ManifestHash  = record.ManifestHash,
                HostVersion   = record.HostVersion,
            });
        }
        else
        {
            existing.PluginName    = record.PluginName;
            existing.PluginVersion = record.PluginVersion;
            existing.Status        = record.Status;
            existing.LastSeenAt    = record.LastSeenAt;
            existing.LastError     = record.LastError;
            existing.ManifestHash  = record.ManifestHash;
            existing.HostVersion   = record.HostVersion;
            // Preserve InstalledAt and Enabled — not overwritten by loader
        }

        await db.SaveChangesAsync(ct);
    }

    public async Task TouchAsync(string pluginId, CancellationToken ct)
    {
        await db.Plugins
            .Where(p => p.PluginId == pluginId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.LastSeenAt, DateTime.UtcNow)
                .SetProperty(p => p.LastError,  (string?)null), ct);
    }

    public async Task SetEnabledAsync(string pluginId, bool enabled, CancellationToken ct)
    {
        var affected = await db.Plugins
            .Where(p => p.PluginId == pluginId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(p => p.Enabled, enabled), ct);

        if (affected == 0)
            throw new NotFoundException($"Plugin '{pluginId}' not found.");
    }
}
```

- [ ] **Step 13: Update `PersistenceTests.SchemaCreated_All42TablesExist` → 43**

In `tests/MSOSync.IntegrationTests/PersistenceTests.cs`, change:

```csharp
// Old:
[Fact]
public async Task SchemaCreated_All42TablesExist()
{
    var count = await fixture.Db.Database
        .SqlQuery<int>($"SELECT COUNT(1) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'msosync'")
        .SingleAsync();
    count.Should().Be(42);
}
```

To:

```csharp
[Fact]
public async Task SchemaCreated_All43TablesExist()
{
    var count = await fixture.Db.Database
        .SqlQuery<int>($"SELECT COUNT(1) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'msosync'")
        .SingleAsync();
    count.Should().Be(43);
}
```

- [ ] **Step 14: Verify build**

```bash
dotnet build src/MSOSync.Plugin/MSOSync.Plugin.csproj
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj
```

Expected: Build succeeded, 0 Warning(s), 0 Error(s)

- [ ] **Step 15: Run persistence integration test**

```bash
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~SchemaCreated_All43TablesExist"
```

Expected: 1 test passed.

- [ ] **Step 16: Commit**

```bash
git add src/MSOSync.Plugin/ src/MSOSync.Persistence/Entities/SyncPlugin.cs src/MSOSync.Persistence/Configurations/SyncPluginConfiguration.cs src/MSOSync.Persistence/Migrations/M029_Plugins.cs src/MSOSync.Persistence/Migrations/M029_Plugins.Designer.cs src/MSOSync.Persistence/Stores/PluginStore.cs src/MSOSync.Persistence/AppDbContext.cs src/MSOSync.Persistence/MSOSync.Persistence.csproj tests/MSOSync.IntegrationTests/PersistenceTests.cs MSOSync.sln
git commit -m "feat(14A-1): MSOSync.Plugin scaffold, M029 sync_plugin table, PluginStore, MANAGE_PLUGINS permission"
```
