// src/MSOSync.Persistence/Migrations/M006_MonitoringTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000006_MonitoringTables")]
public partial class M006_MonitoringTables : Migration
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
