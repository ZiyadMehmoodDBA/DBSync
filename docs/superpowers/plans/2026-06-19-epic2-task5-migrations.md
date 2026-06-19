# Epic 2 / Task 5: Model Snapshot + Migrations M001–M007

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Generate the EF model snapshot (so future `dotnet ef migrations add` works correctly), then hand-write migrations M001–M007 that create the msosync schema and all structural tables.

**Architecture:** Run `dotnet ef migrations add GenerateSnapshot` once to get `AppDbContextModelSnapshot.cs`, then delete the generated migration file and replace it with 7 hand-crafted files. The snapshot reflects the final model state so future migrations only diff against it.

**Tech Stack:** EF Core 9.0.0 migrations, SQL Server 2022

## Global Constraints

- Migrations hardcode `"msosync"` as schema (not env var — migrations are static)
- Migration timestamps use `20260619000001` through `20260619000007`
- `[Migration("YYYYMMDDNNNNNN_ClassName")]` attribute controls apply order
- IDENTITY columns use `.Annotation("SqlServer:Identity", "1, 1")`
- dotnet PATH:
  ```powershell
  $env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
  ```

---

**Files:**
- Generate then keep: `src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs`
- Generate then delete: `src/MSOSync.Persistence/Migrations/{timestamp}_GenerateSnapshot.cs`
- Create: `src/MSOSync.Persistence/Migrations/M001_CreateSchema.cs`
- Create: `src/MSOSync.Persistence/Migrations/M002_CoreTables.cs`
- Create: `src/MSOSync.Persistence/Migrations/M003_TriggerAndRoutingTables.cs`
- Create: `src/MSOSync.Persistence/Migrations/M004_EventTables.cs`
- Create: `src/MSOSync.Persistence/Migrations/M005_BatchTables.cs`
- Create: `src/MSOSync.Persistence/Migrations/M006_MonitoringTables.cs`
- Create: `src/MSOSync.Persistence/Migrations/M007_SecurityTables.cs`

**Interfaces:**
- Consumes: `AppDbContext` + all 23 configurations (Tasks 3–4)
- Produces: `AppDbContextModelSnapshot.cs` + M001–M007 — consumed by Task 6 (`database update`), Task 8 (`MigrateAsync`)

---

- [ ] **Step 1: Generate model snapshot**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
$env:MSOSYNC_SCHEMA = "msosync"
dotnet ef migrations add GenerateSnapshot --project src\MSOSync.Persistence --output-dir Migrations
```

Expected: Two files created in `src/MSOSync.Persistence/Migrations/`:
- `{timestamp}_GenerateSnapshot.cs`
- `AppDbContextModelSnapshot.cs`

- [ ] **Step 2: Delete the generated migration file (keep only the snapshot)**

```powershell
Get-ChildItem src\MSOSync.Persistence\Migrations\*GenerateSnapshot.cs | Remove-Item
```

Verify only `AppDbContextModelSnapshot.cs` remains in `Migrations/` (plus the 7 hand-crafted files you'll add next).

- [ ] **Step 3: Write M001_CreateSchema.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M001_CreateSchema.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000001_CreateSchema")]
public sealed class M001_CreateSchema : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: "msosync");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Schema drop only safe after all tables removed by later Down() calls
    }
}
```

- [ ] **Step 4: Write M002_CoreTables.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M002_CoreTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000002_CoreTables")]
public sealed class M002_CoreTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_node_group",
            schema: "msosync",
            columns: table => new
            {
                group_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                group_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_node_group", x => x.group_id));

        migrationBuilder.CreateTable(
            name: "sync_node",
            schema: "msosync",
            columns: table => new
            {
                node_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                group_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                sync_url = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                status = table.Column<string>(type: "varchar(20)", maxLength: 20, nullable: false),
                registration_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                last_heartbeat = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                heartbeat_interval = table.Column<int>(nullable: false, defaultValue: 60),
                sync_enabled = table.Column<bool>(nullable: false, defaultValue: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_node", x => x.node_id));

        migrationBuilder.CreateIndex(
            name: "IX_sync_node_last_heartbeat",
            schema: "msosync",
            table: "sync_node",
            column: "last_heartbeat");

        migrationBuilder.CreateTable(
            name: "sync_node_security",
            schema: "msosync",
            columns: table => new
            {
                node_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                node_token = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                created_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_node_security", x => x.node_id));

        migrationBuilder.CreateTable(
            name: "sync_registration_request",
            schema: "msosync",
            columns: table => new
            {
                request_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                node_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                node_group = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                sync_url = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: true),
                node_version = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                db_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                request_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                approved = table.Column<bool>(nullable: false, defaultValue: false)
            },
            constraints: table => table.PrimaryKey("PK_sync_registration_request", x => x.request_id));

        migrationBuilder.CreateTable(
            name: "sync_channel",
            schema: "msosync",
            columns: table => new
            {
                channel_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                priority = table.Column<int>(nullable: false),
                batch_size = table.Column<int>(nullable: false, defaultValue: 1000),
                max_batch_to_send = table.Column<int>(nullable: false, defaultValue: 10),
                max_data_size = table.Column<long>(nullable: false, defaultValue: 1048576L),
                enabled = table.Column<bool>(nullable: false, defaultValue: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_channel", x => x.channel_id));

        migrationBuilder.CreateTable(
            name: "sync_parameter",
            schema: "msosync",
            columns: table => new
            {
                parameter_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                parameter_value = table.Column<string>(type: "nvarchar(max)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_parameter", x => x.parameter_name));

        migrationBuilder.CreateTable(
            name: "sync_parameter_hist",
            schema: "msosync",
            columns: table => new
            {
                hist_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                parameter_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                old_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                new_value = table.Column<string>(type: "nvarchar(max)", nullable: true),
                changed_by = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                change_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_parameter_hist", x => x.hist_id));

        migrationBuilder.CreateTable(
            name: "sync_lock",
            schema: "msosync",
            columns: table => new
            {
                lock_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                lock_owner = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                lock_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_lock", x => x.lock_name));
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_lock", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_parameter_hist", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_parameter", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_channel", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_registration_request", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_node_security", schema: "msosync");
        migrationBuilder.DropIndex(name: "IX_sync_node_last_heartbeat", schema: "msosync", table: "sync_node");
        migrationBuilder.DropTable(name: "sync_node", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_node_group", schema: "msosync");
    }
}
```

- [ ] **Step 5: Write M003_TriggerAndRoutingTables.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M003_TriggerAndRoutingTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000003_TriggerAndRoutingTables")]
public sealed class M003_TriggerAndRoutingTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_trigger",
            schema: "msosync",
            columns: table => new
            {
                trigger_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                source_table = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                channel_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                sync_on_insert = table.Column<bool>(nullable: false, defaultValue: true),
                sync_on_update = table.Column<bool>(nullable: false, defaultValue: true),
                sync_on_delete = table.Column<bool>(nullable: false, defaultValue: true),
                enabled = table.Column<bool>(nullable: false, defaultValue: true),
                trigger_version = table.Column<int>(nullable: false, defaultValue: 0),
                last_verified_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_trigger", x => x.trigger_id));

        migrationBuilder.CreateTable(
            name: "sync_trigger_hist",
            schema: "msosync",
            columns: table => new
            {
                hist_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                trigger_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                ddl_text = table.Column<string>(type: "nvarchar(max)", nullable: true),
                trigger_version = table.Column<int>(nullable: true),
                create_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_trigger_hist", x => x.hist_id));

        migrationBuilder.CreateTable(
            name: "sync_router",
            schema: "msosync",
            columns: table => new
            {
                router_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                source_node_group = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                target_node_group = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                router_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false, defaultValue: "default"),
                enabled = table.Column<bool>(nullable: false, defaultValue: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_router", x => x.router_id));

        migrationBuilder.CreateTable(
            name: "sync_trigger_router",
            schema: "msosync",
            columns: table => new
            {
                trigger_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                router_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                enabled = table.Column<bool>(nullable: false, defaultValue: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_trigger_router", x => new { x.trigger_id, x.router_id });
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_trigger_router", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_router", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_trigger_hist", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_trigger", schema: "msosync");
    }
}
```

- [ ] **Step 6: Write M004_EventTables.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M004_EventTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000004_EventTables")]
public sealed class M004_EventTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_data_event",
            schema: "msosync",
            columns: table => new
            {
                event_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                trigger_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                source_node_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                channel_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                event_type = table.Column<string>(type: "char(1)", nullable: false),
                table_name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                pk_data = table.Column<string>(type: "nvarchar(max)", nullable: true),
                row_data = table.Column<string>(type: "nvarchar(max)", nullable: true),
                transaction_id = table.Column<long>(nullable: true),
                create_time = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                is_processed = table.Column<bool>(nullable: false, defaultValue: false)
            },
            constraints: table => table.PrimaryKey("PK_sync_data_event", x => x.event_id));

        migrationBuilder.CreateIndex(
            name: "IX_sync_data_event_channel_processed",
            schema: "msosync",
            table: "sync_data_event",
            columns: new[] { "channel_id", "is_processed" });

        migrationBuilder.CreateIndex(
            name: "IX_sync_data_event_transaction_id",
            schema: "msosync",
            table: "sync_data_event",
            column: "transaction_id");

        migrationBuilder.CreateIndex(
            name: "IX_sync_data_event_create_time",
            schema: "msosync",
            table: "sync_data_event",
            column: "create_time");

        migrationBuilder.CreateTable(
            name: "sync_data_event_batch",
            schema: "msosync",
            columns: table => new
            {
                event_id = table.Column<long>(nullable: false),
                batch_id = table.Column<long>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_data_event_batch", x => new { x.event_id, x.batch_id });
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_data_event_batch", schema: "msosync");
        migrationBuilder.DropIndex(name: "IX_sync_data_event_create_time", schema: "msosync", table: "sync_data_event");
        migrationBuilder.DropIndex(name: "IX_sync_data_event_transaction_id", schema: "msosync", table: "sync_data_event");
        migrationBuilder.DropIndex(name: "IX_sync_data_event_channel_processed", schema: "msosync", table: "sync_data_event");
        migrationBuilder.DropTable(name: "sync_data_event", schema: "msosync");
    }
}
```

- [ ] **Step 7: Write M005_BatchTables.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M005_BatchTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000005_BatchTables")]
public sealed class M005_BatchTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_outgoing_batch",
            schema: "msosync",
            columns: table => new
            {
                batch_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                batch_sequence = table.Column<long>(nullable: false),
                node_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                channel_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                status = table.Column<byte>(type: "tinyint", nullable: false),
                row_count = table.Column<int>(nullable: false, defaultValue: 0),
                byte_count = table.Column<long>(nullable: false, defaultValue: 0L),
                retry_count = table.Column<int>(nullable: false, defaultValue: 0),
                next_retry_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                network_millis = table.Column<long>(nullable: true),
                create_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                sent_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                ack_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_outgoing_batch", x => x.batch_id));

        migrationBuilder.CreateIndex(
            name: "IX_sync_outgoing_batch_node_status",
            schema: "msosync",
            table: "sync_outgoing_batch",
            columns: new[] { "node_id", "status" });

        migrationBuilder.CreateIndex(
            name: "IX_sync_outgoing_batch_next_retry",
            schema: "msosync",
            table: "sync_outgoing_batch",
            column: "next_retry_time");

        migrationBuilder.CreateIndex(
            name: "IX_sync_outgoing_batch_channel",
            schema: "msosync",
            table: "sync_outgoing_batch",
            column: "channel_id");

        migrationBuilder.CreateTable(
            name: "sync_incoming_batch",
            schema: "msosync",
            columns: table => new
            {
                batch_id = table.Column<long>(nullable: false),
                node_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                channel_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                status = table.Column<byte>(type: "tinyint", nullable: false),
                row_count = table.Column<int>(nullable: true),
                load_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                extract_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                applied_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                apply_time_ms = table.Column<long>(nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_incoming_batch", x => x.batch_id));

        migrationBuilder.CreateIndex(
            name: "IX_sync_incoming_batch_node_status",
            schema: "msosync",
            table: "sync_incoming_batch",
            columns: new[] { "node_id", "status" });

        migrationBuilder.CreateTable(
            name: "sync_batch_error",
            schema: "msosync",
            columns: table => new
            {
                error_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                batch_id = table.Column<long>(nullable: false),
                event_id = table.Column<long>(nullable: true),
                conflict_type = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                error_message = table.Column<string>(type: "nvarchar(max)", nullable: true),
                retry_count = table.Column<int>(nullable: false, defaultValue: 0),
                last_retry_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_batch_error", x => x.error_id);
                table.ForeignKey(
                    name: "FK_sync_batch_error_batch_id",
                    column: x => x.batch_id,
                    principalSchema: "msosync",
                    principalTable: "sync_outgoing_batch",
                    principalColumn: "batch_id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sync_batch_error_batch_id",
            schema: "msosync",
            table: "sync_batch_error",
            column: "batch_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_batch_error", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_incoming_batch", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_outgoing_batch", schema: "msosync");
    }
}
```

- [ ] **Step 8: Write M006_MonitoringTables.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M006_MonitoringTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000006_MonitoringTables")]
public sealed class M006_MonitoringTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_monitor",
            schema: "msosync",
            columns: table => new
            {
                snapshot_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                node_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: true),
                metric_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                metric_value = table.Column<string>(maxLength: 500, nullable: true),
                create_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_monitor", x => x.snapshot_id));

        migrationBuilder.CreateIndex(
            name: "IX_sync_monitor_node_create_time",
            schema: "msosync",
            table: "sync_monitor",
            columns: new[] { "node_id", "create_time" });

        migrationBuilder.CreateTable(
            name: "sync_runtime_stats",
            schema: "msosync",
            columns: table => new
            {
                stat_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                heap_used = table.Column<long>(nullable: true),
                heap_max = table.Column<long>(nullable: true),
                thread_count = table.Column<int>(nullable: true),
                cpu_percent = table.Column<decimal>(type: "decimal(5,2)", nullable: true),
                gc_count = table.Column<long>(nullable: true),
                gc_time_ms = table.Column<long>(nullable: true),
                uptime_ms = table.Column<long>(nullable: true),
                create_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_runtime_stats", x => x.stat_id));

        migrationBuilder.CreateTable(
            name: "sync_audit",
            schema: "msosync",
            columns: table => new
            {
                audit_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                action_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                object_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                correlation_id = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                create_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_audit", x => x.audit_id));

        migrationBuilder.CreateIndex(
            name: "IX_sync_audit_create_time",
            schema: "msosync",
            table: "sync_audit",
            column: "create_time");

        migrationBuilder.CreateIndex(
            name: "IX_sync_audit_username",
            schema: "msosync",
            table: "sync_audit",
            column: "username");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_audit", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_runtime_stats", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_monitor", schema: "msosync");
    }
}
```

- [ ] **Step 9: Write M007_SecurityTables.cs**

```csharp
// src/MSOSync.Persistence/Migrations/M007_SecurityTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000007_SecurityTables")]
public sealed class M007_SecurityTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_user",
            schema: "msosync",
            columns: table => new
            {
                user_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                password_hash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                enabled = table.Column<bool>(nullable: false, defaultValue: true),
                last_login = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                failed_attempts = table.Column<int>(nullable: false, defaultValue: 0),
                created_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_user", x => x.user_id));

        migrationBuilder.CreateIndex(
            name: "UQ_sync_user_username",
            schema: "msosync",
            table: "sync_user",
            column: "username",
            unique: true);

        migrationBuilder.CreateTable(
            name: "sync_role",
            schema: "msosync",
            columns: table => new
            {
                role_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                role_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_sync_role", x => x.role_id));

        migrationBuilder.CreateIndex(
            name: "UQ_sync_role_role_name",
            schema: "msosync",
            table: "sync_role",
            column: "role_name",
            unique: true);

        migrationBuilder.CreateTable(
            name: "sync_user_role",
            schema: "msosync",
            columns: table => new
            {
                user_id = table.Column<long>(nullable: false),
                role_id = table.Column<long>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_user_role", x => new { x.user_id, x.role_id });
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_user_role", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_role", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_user", schema: "msosync");
    }
}
```

- [ ] **Step 10: Verify migrations list**

```powershell
$env:PATH = "C:\Users\zmehmood\.dotnet;C:\Users\zmehmood\.dotnet\tools;" + $env:PATH
dotnet ef migrations list --project src\MSOSync.Persistence
```

Expected (7 entries, all showing `(Pending)` since no DB yet):
```
20260619000001_CreateSchema (Pending)
20260619000002_CoreTables (Pending)
20260619000003_TriggerAndRoutingTables (Pending)
20260619000004_EventTables (Pending)
20260619000005_BatchTables (Pending)
20260619000006_MonitoringTables (Pending)
20260619000007_SecurityTables (Pending)
```

- [ ] **Step 11: Commit**

```powershell
git add src/MSOSync.Persistence/Migrations/
git commit -m "feat(persistence): add model snapshot and migrations M001-M007"
```
