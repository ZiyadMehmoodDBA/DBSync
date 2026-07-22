# Task 1 — Persistence + M034

**Files:**
- Create: `src/MSOSync.Persistence/Entities/SyncReplayRequest.cs`
- Create: `src/MSOSync.Persistence/Entities/SyncReplayItem.cs`
- Create: `src/MSOSync.Persistence/Configurations/SyncReplayRequestConfiguration.cs`
- Create: `src/MSOSync.Persistence/Configurations/SyncReplayItemConfiguration.cs`
- Create: `src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs`
- Create: `src/MSOSync.Persistence/Migrations/M034_BatchReplay.Designer.cs`
- Modify: `src/MSOSync.Persistence/AppDbContext.cs` (add two DbSets)
- Modify: `src/MSOSync.Metadata/Operations/OperationEnums.cs` (add `BatchReplay` to `OperationType`, add `NoData` to `OperationResult`)
- Create: `src/MSOSync.Metadata/Operations/Replay/ReplayMode.cs`
- Create: `src/MSOSync.Metadata/Operations/Replay/ReplayItemStatus.cs`
- Test: `tests/MSOSync.IntegrationTests/Lifecycle/M034MigrationTests.cs`

**Interfaces:**
- Produces:
  - `SyncReplayRequest` entity (properties below)
  - `SyncReplayItem` entity (properties below)
  - `AppDbContext.ReplayRequests` DbSet
  - `AppDbContext.ReplayItems` DbSet
  - `OperationType.BatchReplay`
  - `OperationResult.NoData`
  - `ReplayMode` enum: `FailedDelivery`, `MissedData`, `Both`
  - `ReplayItemStatus` enum: `Pending`, `Processing`, `Completed`, `Failed`, `Skipped`

---

- [ ] **Step 1: Create `SyncReplayRequest` entity**

```csharp
// src/MSOSync.Persistence/Entities/SyncReplayRequest.cs
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncReplayRequest : ITenantScoped
{
    public Guid    ReplayId        { get; set; }
    public Guid    OperationId     { get; set; }
    public string  NodeId          { get; set; } = null!;
    public string? ChannelIdsJson  { get; set; }    // JSON string[]; null = all channels
    public string? BatchIdsJson    { get; set; }    // JSON long[]; null = no cherry-pick
    public DateTime FromTime       { get; set; }
    public DateTime ToTime         { get; set; }
    public string  ReplayMode      { get; set; } = null!; // FailedDelivery|MissedData|Both
    public Guid    TenantId        { get; set; }
}
```

- [ ] **Step 2: Create `SyncReplayItem` entity**

```csharp
// src/MSOSync.Persistence/Entities/SyncReplayItem.cs
using MSOSync.Common.Tenancy;

namespace MSOSync.Persistence.Entities;

[TenantScoped]
public sealed class SyncReplayItem : ITenantScoped
{
    public Guid    ItemId          { get; set; }
    public Guid    OperationId     { get; set; }
    public long?   SourceBatchId   { get; set; }   // null for MissedData
    public long?   ReplayBatchId   { get; set; }   // null until worker processes item
    public string  NodeId          { get; set; } = null!;
    public string  ChannelId       { get; set; } = null!;
    public int     EventCount      { get; set; }
    public string  Status          { get; set; } = null!; // Pending|Processing|Completed|Failed|Skipped
    public string? ErrorMessage    { get; set; }
    public Guid    TenantId        { get; set; }
}
```

- [ ] **Step 3: Create `SyncReplayRequestConfiguration`**

```csharp
// src/MSOSync.Persistence/Configurations/SyncReplayRequestConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncReplayRequestConfiguration : IEntityTypeConfiguration<SyncReplayRequest>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncReplayRequest> b)
    {
        b.ToTable("sync_replay_request", Schema);
        b.HasKey(x => x.ReplayId);
        b.Property(x => x.ReplayId).HasColumnName("replay_id").ValueGeneratedNever();
        b.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
        b.Property(x => x.NodeId).HasColumnName("node_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.ChannelIdsJson).HasColumnName("channel_ids_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.BatchIdsJson).HasColumnName("batch_ids_json").HasColumnType("nvarchar(max)");
        b.Property(x => x.FromTime).HasColumnName("from_time").HasColumnType("datetime2(7)").IsRequired();
        b.Property(x => x.ToTime).HasColumnName("to_time").HasColumnType("datetime2(7)").IsRequired();
        b.Property(x => x.ReplayMode).HasColumnName("replay_mode").HasMaxLength(20).IsRequired();
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

        b.HasOne<SyncOperation>().WithMany().HasForeignKey(x => x.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
```

- [ ] **Step 4: Create `SyncReplayItemConfiguration`**

```csharp
// src/MSOSync.Persistence/Configurations/SyncReplayItemConfiguration.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using MSOSync.Persistence.Entities;

namespace MSOSync.Persistence.Configurations;

public sealed class SyncReplayItemConfiguration : IEntityTypeConfiguration<SyncReplayItem>
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public void Configure(EntityTypeBuilder<SyncReplayItem> b)
    {
        b.ToTable("sync_replay_item", Schema);
        b.HasKey(x => x.ItemId);
        b.Property(x => x.ItemId).HasColumnName("item_id").ValueGeneratedNever();
        b.Property(x => x.OperationId).HasColumnName("operation_id").IsRequired();
        b.Property(x => x.SourceBatchId).HasColumnName("source_batch_id");
        b.Property(x => x.ReplayBatchId).HasColumnName("replay_batch_id");
        b.Property(x => x.NodeId).HasColumnName("node_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.ChannelId).HasColumnName("channel_id").HasMaxLength(50).IsRequired();
        b.Property(x => x.EventCount).HasColumnName("event_count").IsRequired();
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
        b.Property(x => x.ErrorMessage).HasColumnName("error_message").HasMaxLength(1000);
        b.Property(x => x.TenantId).HasColumnName("tenant_id").IsRequired();

        b.HasOne<SyncOperation>().WithMany().HasForeignKey(x => x.OperationId)
            .OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.OperationId, x.Status })
            .HasDatabaseName("ix_sync_replay_item_op_status");
        b.HasIndex(x => new { x.TenantId, x.NodeId })
            .HasDatabaseName("ix_sync_replay_item_tenant_node");
    }
}
```

- [ ] **Step 5: Add DbSets to `AppDbContext`**

In `src/MSOSync.Persistence/AppDbContext.cs`, after the `OperationSteps` DbSet line (line ~58), add:

```csharp
    public DbSet<SyncReplayRequest>  ReplayRequests   => Set<SyncReplayRequest>();
    public DbSet<SyncReplayItem>     ReplayItems      => Set<SyncReplayItem>();
```

- [ ] **Step 6: Add enum values**

In `src/MSOSync.Metadata/Operations/OperationEnums.cs`:

Add `BatchReplay` to `OperationType`:
```csharp
public enum OperationType
{
    Export,
    Rollout,
    Decommission,
    Recovery,
    RollingMaintenance,
    RollingUpgrade,
    BatchReplay,
}
```

Add `NoData` to `OperationResult`:
```csharp
public enum OperationResult
{
    Success,
    PartialSuccess,
    Failure,
    Cancelled,
    NoData,
}
```

- [ ] **Step 7: Create `ReplayMode` enum**

```csharp
// src/MSOSync.Metadata/Operations/Replay/ReplayMode.cs
namespace MSOSync.Metadata.Operations.Replay;

public enum ReplayMode
{
    FailedDelivery,
    MissedData,
    Both,
}
```

- [ ] **Step 8: Create `ReplayItemStatus` enum**

```csharp
// src/MSOSync.Metadata/Operations/Replay/ReplayItemStatus.cs
namespace MSOSync.Metadata.Operations.Replay;

public enum ReplayItemStatus
{
    Pending,
    Processing,
    Completed,
    Failed,
    Skipped,
}
```

- [ ] **Step 9: Create M034 migration**

```csharp
// src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M034_BatchReplay : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_replay_request",
            schema: Schema,
            columns: t => new
            {
                replay_id       = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                operation_id    = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                node_id         = t.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                channel_ids_json = t.Column<string>(type: "nvarchar(max)", nullable: true),
                batch_ids_json  = t.Column<string>(type: "nvarchar(max)", nullable: true),
                from_time       = t.Column<DateTime>(type: "datetime2(7)", nullable: false),
                to_time         = t.Column<DateTime>(type: "datetime2(7)", nullable: false),
                replay_mode     = t.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                tenant_id       = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("pk_sync_replay_request", x => x.replay_id);
                t.ForeignKey(
                    name: "fk_sync_replay_request_operation",
                    column: x => x.operation_id,
                    principalSchema: Schema,
                    principalTable: "sync_operation",
                    principalColumn: "operation_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "sync_replay_item",
            schema: Schema,
            columns: t => new
            {
                item_id         = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                operation_id    = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                source_batch_id = t.Column<long>(type: "bigint", nullable: true),
                replay_batch_id = t.Column<long>(type: "bigint", nullable: true),
                node_id         = t.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                channel_id      = t.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                event_count     = t.Column<int>(type: "int", nullable: false),
                status          = t.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                error_message   = t.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                tenant_id       = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("pk_sync_replay_item", x => x.item_id);
                t.ForeignKey(
                    name: "fk_sync_replay_item_operation",
                    column: x => x.operation_id,
                    principalSchema: Schema,
                    principalTable: "sync_operation",
                    principalColumn: "operation_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_sync_replay_item_op_status",
            schema: Schema,
            table: "sync_replay_item",
            columns: new[] { "operation_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_sync_replay_item_tenant_node",
            schema: Schema,
            table: "sync_replay_item",
            columns: new[] { "tenant_id", "node_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_replay_item", schema: Schema);
        migrationBuilder.DropTable(name: "sync_replay_request", schema: Schema);
    }
}
```

- [ ] **Step 10: Create M034 Designer file**

Copy the Designer file pattern from `M033_RollingOperations.Designer.cs`, updating class name, migration id, and snapshot. The simplest approach: copy and update the class name to `M034_BatchReplay` and update `MigrationId` to `"M034_BatchReplay"`.

```csharp
// src/MSOSync.Persistence/Migrations/M034_BatchReplay.Designer.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[DbContext(typeof(MSOSync.Persistence.AppDbContext))]
[Migration("M034_BatchReplay")]
partial class M034_BatchReplay
{
    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        // Snapshot not needed for LocalDB migration tests — leave empty body
    }
}
```

- [ ] **Step 11: Write M034 migration test**

```csharp
// tests/MSOSync.IntegrationTests/Lifecycle/M034MigrationTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

[Collection("Migration")]
public sealed class M034MigrationTests
{
    private static AppDbContext CreateDb()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                "Server=(localdb)\\MSSQLLocalDB;Database=MSOSyncM034_Test;Trusted_Connection=True;",
                o => o.MigrationsAssembly("MSOSync.Persistence"))
            .Options;
        return new AppDbContext(opts);
    }

    [Fact]
    public async Task M034_creates_sync_replay_request_table()
    {
        await using var db = CreateDb();
        await db.Database.MigrateAsync();

        var tables = await db.Database
            .SqlQueryRaw<string>(
                "SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_replay_request'")
            .ToListAsync();

        tables.Should().ContainSingle(t => t == "sync_replay_request");
    }

    [Fact]
    public async Task M034_creates_sync_replay_item_table()
    {
        await using var db = CreateDb();
        await db.Database.MigrateAsync();

        var tables = await db.Database
            .SqlQueryRaw<string>(
                "SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_replay_item'")
            .ToListAsync();

        tables.Should().ContainSingle(t => t == "sync_replay_item");
    }

    [Fact]
    public async Task M034_creates_expected_indexes_on_replay_item()
    {
        await using var db = CreateDb();
        await db.Database.MigrateAsync();

        var indexes = await db.Database
            .SqlQueryRaw<string>(
                "SELECT i.name AS Value FROM sys.indexes i " +
                "JOIN sys.objects o ON i.object_id = o.object_id " +
                "WHERE o.name = 'sync_replay_item' AND i.name IS NOT NULL")
            .ToListAsync();

        indexes.Should().Contain("ix_sync_replay_item_op_status");
        indexes.Should().Contain("ix_sync_replay_item_tenant_node");
    }
}
```

- [ ] **Step 12: Run migration tests**

```
dotnet test tests/MSOSync.IntegrationTests --filter "FullyQualifiedName~M034MigrationTests" -v normal
```

Expected: environmental failure (LocalDB) is acceptable; build must pass. Unit compilation is the gate.

- [ ] **Step 13: Run unit build to verify compilation**

```
dotnet build src/MSOSync.Persistence/MSOSync.Persistence.csproj
dotnet build src/MSOSync.Metadata/MSOSync.Metadata.csproj
```

Expected: 0 errors.

- [ ] **Step 14: Commit**

```
git add src/MSOSync.Persistence/Entities/SyncReplayRequest.cs
git add src/MSOSync.Persistence/Entities/SyncReplayItem.cs
git add src/MSOSync.Persistence/Configurations/SyncReplayRequestConfiguration.cs
git add src/MSOSync.Persistence/Configurations/SyncReplayItemConfiguration.cs
git add src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs
git add src/MSOSync.Persistence/Migrations/M034_BatchReplay.Designer.cs
git add src/MSOSync.Persistence/AppDbContext.cs
git add src/MSOSync.Metadata/Operations/OperationEnums.cs
git add src/MSOSync.Metadata/Operations/Replay/ReplayMode.cs
git add src/MSOSync.Metadata/Operations/Replay/ReplayItemStatus.cs
git add tests/MSOSync.IntegrationTests/Lifecycle/M034MigrationTests.cs
git commit -m "feat(2B.2-T1): persistence entities, M034 migration, replay enums"
```
