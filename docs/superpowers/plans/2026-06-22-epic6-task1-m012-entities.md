# Task 1: M012 Migration + Enums + NodeProperties + Entity/Dto Updates

**Part of:** Epic 6 Transport Layer
**Spec:** `docs/superpowers/specs/2026-06-22-epic6-transport-design.md` § 2

**Files:**
- Create: `src/MSOSync.Persistence/TransportMode.cs`
- Create: `src/MSOSync.Persistence/IncomingBatchStatus.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncNode.cs`
- Modify: `src/MSOSync.Persistence/Entities/SyncIncomingBatch.cs`
- Modify: `src/MSOSync.Persistence/Configurations/SyncNodeConfiguration.cs`
- Modify: `src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs`
- Create: `src/MSOSync.Common/NodeProperties.cs`
- Modify: `src/MSOSync.Metadata/Dtos/NodeDto.cs`
- Modify: `src/MSOSync.Metadata/Services/NodeMetadataService.cs`
- Create (scaffold then edit): `src/MSOSync.Persistence/Migrations/M012_Transport.cs`

**Interfaces:**
- Produces: `TransportMode` enum, `IncomingBatchStatus` enum, `SyncNode.TransportMode`, `SyncIncomingBatch.{IncomingBatchStatus Status, BatchSequence, SourceNodeId, ReceivedTime}`, `NodeProperties`, `NodeDto.TransportMode`
- Consumed by: Tasks 2, 5, 6, 7, 8, 9, 10, 11, 12, 13

---

- [ ] **Step 1: Create the new enums**

Create `src/MSOSync.Persistence/TransportMode.cs`:
```csharp
namespace MSOSync.Persistence;

public enum TransportMode : byte
{
    Pull = 1,
    Push = 2
}
```

Create `src/MSOSync.Persistence/IncomingBatchStatus.cs`:
```csharp
namespace MSOSync.Persistence;

public enum IncomingBatchStatus : byte
{
    New           = 0,
    Applying      = 1,
    Applied       = 2,
    Error         = 3,
    PartialSuccess = 4
}
```

- [ ] **Step 2: Update SyncNode entity**

Current `src/MSOSync.Persistence/Entities/SyncNode.cs`:
```csharp
namespace MSOSync.Persistence.Entities;

public sealed class SyncNode
{
    public string NodeId { get; set; } = null!;
    public string GroupId { get; set; } = null!;
    public string SyncUrl { get; set; } = null!;
    public string Status { get; set; } = null!;
    public DateTime? RegistrationTime { get; set; }
    public DateTime? LastHeartbeat { get; set; }
    public int HeartbeatInterval { get; set; } = 60;
    public bool SyncEnabled { get; set; } = true;
}
```

Add one property at the end:
```csharp
    public TransportMode TransportMode { get; set; } = TransportMode.Pull;
```

- [ ] **Step 3: Update SyncIncomingBatch entity**

Replace `src/MSOSync.Persistence/Entities/SyncIncomingBatch.cs` entirely:
```csharp
namespace MSOSync.Persistence.Entities;

public sealed class SyncIncomingBatch
{
    public long BatchId { get; set; }
    public string NodeId { get; set; } = null!;
    public string ChannelId { get; set; } = null!;
    public IncomingBatchStatus Status { get; set; } = IncomingBatchStatus.New;
    public int? RowCount { get; set; }
    public long BatchSequence { get; set; }
    public string SourceNodeId { get; set; } = null!;
    public DateTime ReceivedTime { get; set; }
    public DateTime? LoadTime { get; set; }
    public DateTime? ExtractTime { get; set; }
    public DateTime? AppliedTime { get; set; }
    public long? ApplyTimeMs { get; set; }
}
```

- [ ] **Step 4: Update entity configurations**

In `src/MSOSync.Persistence/Configurations/SyncNodeConfiguration.cs`, add inside `Configure()` after the existing `SyncEnabled` property line:
```csharp
        builder.Property(e => e.TransportMode)
            .HasColumnName("transport_mode")
            .HasColumnType("tinyint")
            .HasConversion<byte>()
            .HasDefaultValue(TransportMode.Pull)
            .IsRequired();
```

In `src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs`, replace the `Status` mapping and add three new properties. Full new Configure body:
```csharp
        builder.ToTable("sync_incoming_batch", Schema);
        builder.HasKey(e => e.BatchId);

        builder.Property(e => e.BatchId).HasColumnName("batch_id").ValueGeneratedNever();
        builder.Property(e => e.NodeId).HasColumnName("node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ChannelId).HasColumnName("channel_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.Status).HasColumnName("status").HasColumnType("tinyint").HasConversion<byte>();
        builder.Property(e => e.RowCount).HasColumnName("row_count");
        builder.Property(e => e.BatchSequence).HasColumnName("batch_sequence").IsRequired();
        builder.Property(e => e.SourceNodeId).HasColumnName("source_node_id").HasColumnType("varchar(50)").HasMaxLength(50).IsUnicode(false).IsRequired();
        builder.Property(e => e.ReceivedTime).HasColumnName("received_time").HasColumnType("datetime2(7)").IsRequired();
        builder.Property(e => e.LoadTime).HasColumnName("load_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ExtractTime).HasColumnName("extract_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.AppliedTime).HasColumnName("applied_time").HasColumnType("datetime2(7)");
        builder.Property(e => e.ApplyTimeMs).HasColumnName("apply_time_ms");

        builder.HasIndex(e => new { e.NodeId, e.Status }).HasDatabaseName("IX_sync_incoming_batch_node_status");
        builder.HasIndex(e => new { e.SourceNodeId, e.ChannelId, e.BatchSequence })
            .HasDatabaseName("IX_sync_incoming_batch_source_channel_sequence");

        builder.HasOne<SyncNode>()
            .WithMany()
            .HasForeignKey(e => e.SourceNodeId)
            .HasConstraintName("FK_sync_incoming_batch_source_node")
            .OnDelete(DeleteBehavior.Restrict);
```

Note: `SyncNode` import needed — add `using MSOSync.Persistence.Entities;` if not present.

- [ ] **Step 5: Create NodeProperties in Common**

Create `src/MSOSync.Common/NodeProperties.cs`:
```csharp
using System.Text.Json.Serialization;

namespace MSOSync.Common;

public sealed class NodeProperties
{
    public string NodeId    { get; init; } = null!;
    public string GroupId   { get; init; } = null!;
    public string SyncUrl   { get; init; } = null!;

    [JsonIgnore]
    public string NodeToken { get; init; } = null!;
}
```

- [ ] **Step 6: Update NodeDto**

Current `src/MSOSync.Metadata/Dtos/NodeDto.cs`:
```csharp
namespace MSOSync.Metadata.Dtos;

public sealed record NodeDto(
    string NodeId,
    string GroupId,
    string SyncUrl,
    string Status,
    DateTime? RegistrationTime,
    DateTime? LastHeartbeat,
    int HeartbeatInterval,
    bool SyncEnabled);
```

Replace with (add `TransportMode` at end; requires Persistence reference which Metadata.csproj already has):
```csharp
using MSOSync.Persistence;

namespace MSOSync.Metadata.Dtos;

public sealed record NodeDto(
    string NodeId,
    string GroupId,
    string SyncUrl,
    string Status,
    DateTime? RegistrationTime,
    DateTime? LastHeartbeat,
    int HeartbeatInterval,
    bool SyncEnabled,
    TransportMode TransportMode);
```

- [ ] **Step 7: Update NodeMetadataService.MapNode**

In `src/MSOSync.Metadata/Services/NodeMetadataService.cs`, find the MapNode method at the bottom:
```csharp
    private static NodeDto MapNode(SyncNode n) =>
        new(n.NodeId, n.GroupId, n.SyncUrl, n.Status,
            n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval, n.SyncEnabled);
```

Replace with:
```csharp
    private static NodeDto MapNode(SyncNode n) =>
        new(n.NodeId, n.GroupId, n.SyncUrl, n.Status,
            n.RegistrationTime, n.LastHeartbeat, n.HeartbeatInterval, n.SyncEnabled,
            n.TransportMode);
```

- [ ] **Step 8: Scaffold and edit the EF migration**

Run from repo root:
```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet ef migrations add M012_Transport `
    --project src/MSOSync.Persistence `
    --startup-project src/MSOSync.App `
    --output-dir Migrations
```

EF will generate a migration. Open the generated `M012_Transport.cs` and replace its `Up()` body with the following exact content (keep the generated class header and `Down()` — edit Down() to reverse):

```csharp
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // --- sync_node: add transport_mode ---
        migrationBuilder.AddColumn<byte>(
            name: "transport_mode",
            schema: "msosync",
            table: "sync_node",
            type: "tinyint",
            nullable: false,
            defaultValue: (byte)1);   // Pull = 1

        migrationBuilder.Sql(
            "ALTER TABLE [msosync].[sync_node] " +
            "ADD CONSTRAINT CK_sync_node_transport_mode " +
            "CHECK (transport_mode IN (1, 2))");

        // --- sync_incoming_batch: add batch_sequence, source_node_id, received_time ---
        migrationBuilder.AddColumn<long>(
            name: "batch_sequence",
            schema: "msosync",
            table: "sync_incoming_batch",
            nullable: false,
            defaultValue: 0L);

        migrationBuilder.AddColumn<string>(
            name: "source_node_id",
            schema: "msosync",
            table: "sync_incoming_batch",
            type: "varchar(50)",
            maxLength: 50,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<DateTime>(
            name: "received_time",
            schema: "msosync",
            table: "sync_incoming_batch",
            type: "datetime2(7)",
            nullable: false,
            defaultValueSql: "SYSUTCDATETIME()");

        // FK: source_node_id → sync_node.node_id
        migrationBuilder.AddForeignKey(
            name: "FK_sync_incoming_batch_source_node",
            schema: "msosync",
            table: "sync_incoming_batch",
            column: "source_node_id",
            principalSchema: "msosync",
            principalTable: "sync_node",
            principalColumn: "node_id",
            onDelete: ReferentialAction.Restrict);

        // Index for sequence lookups
        migrationBuilder.CreateIndex(
            name: "IX_sync_incoming_batch_source_channel_sequence",
            schema: "msosync",
            table: "sync_incoming_batch",
            columns: new[] { "source_node_id", "channel_id", "batch_sequence" });

        // Unique constraint: prevent duplicate replay at DB level
        migrationBuilder.Sql(
            "ALTER TABLE [msosync].[sync_incoming_batch] " +
            "ADD CONSTRAINT UQ_sync_incoming_batch_source_sequence " +
            "UNIQUE (source_node_id, batch_sequence)");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE [msosync].[sync_incoming_batch] " +
            "DROP CONSTRAINT UQ_sync_incoming_batch_source_sequence");

        migrationBuilder.DropIndex(
            name: "IX_sync_incoming_batch_source_channel_sequence",
            schema: "msosync",
            table: "sync_incoming_batch");

        migrationBuilder.DropForeignKey(
            name: "FK_sync_incoming_batch_source_node",
            schema: "msosync",
            table: "sync_incoming_batch");

        migrationBuilder.DropColumn(name: "received_time", schema: "msosync", table: "sync_incoming_batch");
        migrationBuilder.DropColumn(name: "source_node_id", schema: "msosync", table: "sync_incoming_batch");
        migrationBuilder.DropColumn(name: "batch_sequence", schema: "msosync", table: "sync_incoming_batch");

        migrationBuilder.Sql(
            "ALTER TABLE [msosync].[sync_node] " +
            "DROP CONSTRAINT CK_sync_node_transport_mode");

        migrationBuilder.DropColumn(name: "transport_mode", schema: "msosync", table: "sync_node");
    }
```

- [ ] **Step 9: Build to verify**

```pwsh
$env:DOTNET_ROOT = "C:\Users\zmehmood\.dotnet"
$env:PATH        = "C:\Users\zmehmood\.dotnet;$env:PATH"
dotnet build MSOSync.sln -c Debug --warnaserror
```

Expected: build succeeds with zero warnings. If `NodeDto` call sites break (tests that construct `NodeDto` directly), update them to pass a `TransportMode` argument.

- [ ] **Step 10: Commit**

```pwsh
git add src/MSOSync.Persistence/TransportMode.cs
git add src/MSOSync.Persistence/IncomingBatchStatus.cs
git add src/MSOSync.Persistence/Entities/SyncNode.cs
git add src/MSOSync.Persistence/Entities/SyncIncomingBatch.cs
git add src/MSOSync.Persistence/Configurations/SyncNodeConfiguration.cs
git add src/MSOSync.Persistence/Configurations/SyncIncomingBatchConfiguration.cs
git add src/MSOSync.Persistence/Migrations/M012_Transport.cs
git add src/MSOSync.Persistence/Migrations/M012_Transport.Designer.cs
git add src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs
git add src/MSOSync.Common/NodeProperties.cs
git add src/MSOSync.Metadata/Dtos/NodeDto.cs
git add src/MSOSync.Metadata/Services/NodeMetadataService.cs
git commit -m "feat(epic6): M012 migration, TransportMode/IncomingBatchStatus enums, NodeProperties, entity updates"
```
