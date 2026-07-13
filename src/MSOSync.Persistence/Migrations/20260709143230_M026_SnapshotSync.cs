using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M026_SnapshotSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_node_lifecycle_history_correlation_id",
                schema: "msosync",
                table: "sync_node_lifecycle_history",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_node_config_history_correlation_id",
                schema: "msosync",
                table: "sync_node_configuration_history",
                column: "correlation_id");

            migrationBuilder.CreateIndex(
                name: "IX_sync_audit_correlation_create_time",
                schema: "msosync",
                table: "sync_audit",
                columns: new[] { "correlation_id", "create_time" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_node_lifecycle_history_correlation_id",
                schema: "msosync",
                table: "sync_node_lifecycle_history");

            migrationBuilder.DropIndex(
                name: "IX_node_config_history_correlation_id",
                schema: "msosync",
                table: "sync_node_configuration_history");

            migrationBuilder.DropIndex(
                name: "IX_sync_audit_correlation_create_time",
                schema: "msosync",
                table: "sync_audit");
        }
    }
}
