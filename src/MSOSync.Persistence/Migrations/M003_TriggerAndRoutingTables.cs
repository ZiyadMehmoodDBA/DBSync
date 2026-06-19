// src/MSOSync.Persistence/Migrations/M003_TriggerAndRoutingTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000003_TriggerAndRoutingTables")]
public partial class M003_TriggerAndRoutingTables : Migration
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
