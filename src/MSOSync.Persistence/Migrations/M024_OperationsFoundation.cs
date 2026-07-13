using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M024_OperationsFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_operation",
                schema: "msosync",
                columns: table => new
                {
                    operation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: false,
                        defaultValueSql: "NEWID()"),
                    operation_type = table.Column<string>(type: "varchar(50)", unicode: false,
                        maxLength: 50, nullable: false),
                    reference_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    status = table.Column<string>(type: "varchar(30)", unicode: false,
                        maxLength: 30, nullable: false),
                    result = table.Column<string>(type: "varchar(30)", unicode: false,
                        maxLength: 30, nullable: true),
                    source = table.Column<string>(type: "varchar(30)", unicode: false,
                        maxLength: 30, nullable: false),
                    progress_percent = table.Column<int>(type: "int", nullable: true),
                    progress_message = table.Column<string>(type: "varchar(500)", unicode: false,
                        maxLength: 500, nullable: true),
                    correlation_id = table.Column<string>(type: "varchar(100)", unicode: false,
                        maxLength: 100, nullable: true),
                    initiated_by = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000,
                        nullable: true),
                    summary = table.Column<string>(type: "varchar(500)", unicode: false,
                        maxLength: 500, nullable: true),
                    can_cancel = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    can_retry = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    started_at = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    completed_at = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_operation", x => x.operation_id);
                });

            // Indexes on sync_operation
            migrationBuilder.CreateIndex(
                name: "IX_sync_operation_status",
                schema: "msosync",
                table: "sync_operation",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_sync_operation_type",
                schema: "msosync",
                table: "sync_operation",
                column: "operation_type");

            migrationBuilder.CreateIndex(
                name: "IX_sync_operation_started_at_desc",
                schema: "msosync",
                table: "sync_operation",
                column: "started_at",
                descending: new[] { true });

            migrationBuilder.CreateIndex(
                name: "IX_sync_operation_correlation_id",
                schema: "msosync",
                table: "sync_operation",
                column: "correlation_id");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_operation",
                schema: "msosync");
        }
    }
}
