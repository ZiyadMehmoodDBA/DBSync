// src/MSOSync.Persistence/Migrations/M002_CoreTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000002_CoreTables")]
public partial class M002_CoreTables : Migration
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
