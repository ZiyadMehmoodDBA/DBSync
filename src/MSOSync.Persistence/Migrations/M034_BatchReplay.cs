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
                replay_id        = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                operation_id     = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                node_id          = t.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                channel_ids_json = t.Column<string>(type: "nvarchar(max)", nullable: true),
                batch_ids_json   = t.Column<string>(type: "nvarchar(max)", nullable: true),
                from_time        = t.Column<DateTime>(type: "datetime2(7)", nullable: false),
                to_time          = t.Column<DateTime>(type: "datetime2(7)", nullable: false),
                replay_mode      = t.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                tenant_id        = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
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
