// src/MSOSync.Persistence/Migrations/M005_BatchTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000005_BatchTables")]
public partial class M005_BatchTables : Migration
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
