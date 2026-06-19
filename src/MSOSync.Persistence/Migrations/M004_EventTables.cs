// src/MSOSync.Persistence/Migrations/M004_EventTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000004_EventTables")]
public partial class M004_EventTables : Migration
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
