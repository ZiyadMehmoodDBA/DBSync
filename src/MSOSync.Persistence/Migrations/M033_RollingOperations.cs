using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M033_RollingOperations : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // New columns on sync_node
        migrationBuilder.AddColumn<string>(
            name: "agent_version",
            schema: Schema,
            table: "sync_node",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "drain_completed_at",
            schema: Schema,
            table: "sync_node",
            type: "datetimeoffset",
            nullable: true);

        // New table: sync_operation_step
        migrationBuilder.CreateTable(
            name: "sync_operation_step",
            schema: Schema,
            columns: t => new
            {
                step_id       = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                operation_id  = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
                node_id       = t.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                wave_number   = t.Column<int>(type: "int", nullable: false),
                status        = t.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                started_at    = t.Column<DateTime>(type: "datetime2(7)", nullable: true),
                completed_at  = t.Column<DateTime>(type: "datetime2(7)", nullable: true),
                error_message = t.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                tenant_id     = t.Column<Guid>(type: "uniqueidentifier", nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("pk_sync_operation_step", x => x.step_id);
                t.ForeignKey(
                    name: "fk_sync_operation_step_operation",
                    column: x => x.operation_id,
                    principalSchema: Schema,
                    principalTable: "sync_operation",
                    principalColumn: "operation_id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_sync_operation_step_op_wave",
            schema: Schema,
            table: "sync_operation_step",
            columns: new[] { "operation_id", "wave_number" });

        migrationBuilder.CreateIndex(
            name: "ix_sync_operation_step_tenant_node",
            schema: Schema,
            table: "sync_operation_step",
            columns: new[] { "tenant_id", "node_id" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_operation_step", schema: Schema);

        migrationBuilder.DropColumn(name: "agent_version", schema: Schema, table: "sync_node");
        migrationBuilder.DropColumn(name: "drain_completed_at", schema: Schema, table: "sync_node");
    }
}
