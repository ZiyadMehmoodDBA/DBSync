using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M022_NodeLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Widen status column BEFORE converting values
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "msosync",
                table: "sync_node",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(20)",
                oldUnicode: false,
                oldMaxLength: 20);

            // 2. Convert legacy values (source of truth: LegacyStatusMap — keep in sync)
            migrationBuilder.Sql("""
                UPDATE [msosync].[sync_node] SET status = 'PendingApproval'     WHERE status = 'PENDING';
                UPDATE [msosync].[sync_node] SET status = 'PendingRegistration' WHERE status IN ('APPROVED','PROVISIONED');
                UPDATE [msosync].[sync_node] SET status = 'Active'              WHERE status IN ('REGISTERED','OFFLINE');
                UPDATE [msosync].[sync_node] SET status = 'Disabled'            WHERE status = 'DISABLED';
                """);

            // 3. Drop sync_enabled
            migrationBuilder.DropColumn(
                name: "sync_enabled",
                schema: "msosync",
                table: "sync_node");

            // 4. Add new columns to sync_node
            migrationBuilder.AddColumn<string>(
                name: "connectivity_reason",
                schema: "msosync",
                table: "sync_node",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "consecutive_probe_failures",
                schema: "msosync",
                table: "sync_node",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decommission_grace_until",
                schema: "msosync",
                table: "sync_node",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "decommission_initial_open_batches",
                schema: "msosync",
                table: "sync_node",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decommission_reason",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decommission_started_at",
                schema: "msosync",
                table: "sync_node",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "last_probe_error",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "maintenance_mode",
                schema: "msosync",
                table: "sync_node",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "maintenance_reason",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "maintenance_started_at",
                schema: "msosync",
                table: "sync_node",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "maintenance_started_by",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "maintenance_until",
                schema: "msosync",
                table: "sync_node",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "previous_lifecycle_state",
                schema: "msosync",
                table: "sync_node",
                type: "varchar(30)",
                unicode: false,
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "row_version",
                schema: "msosync",
                table: "sync_node",
                type: "rowversion",
                rowVersion: true,
                nullable: false,
                defaultValue: new byte[0]);

            // 5. Create history / bootstrap tables
            migrationBuilder.CreateTable(
                name: "sync_node_bootstrap_token",
                schema: "msosync",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    node_id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    token_hash = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: false),
                    issued_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    issued_by = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_bootstrap_token", x => x.id);
                    table.ForeignKey(
                        name: "FK_node_bootstrap_token_node",
                        column: x => x.node_id,
                        principalSchema: "msosync",
                        principalTable: "sync_node",
                        principalColumn: "node_id");
                });

            migrationBuilder.CreateTable(
                name: "sync_node_connectivity_history",
                schema: "msosync",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    node_id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    previous_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    new_status = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_connectivity_history", x => x.id);
                    table.ForeignKey(
                        name: "FK_node_connectivity_history_node",
                        column: x => x.node_id,
                        principalSchema: "msosync",
                        principalTable: "sync_node",
                        principalColumn: "node_id");
                });

            migrationBuilder.CreateTable(
                name: "sync_node_lifecycle_history",
                schema: "msosync",
                columns: table => new
                {
                    history_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    node_id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    from_state = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: true),
                    to_state = table.Column<string>(type: "varchar(30)", unicode: false, maxLength: 30, nullable: false),
                    trigger = table.Column<string>(type: "varchar(20)", unicode: false, maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    actor = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    correlation_id = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    metadata_json = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_lifecycle_history", x => x.history_id);
                    table.ForeignKey(
                        name: "FK_node_lifecycle_history_node",
                        column: x => x.node_id,
                        principalSchema: "msosync",
                        principalTable: "sync_node",
                        principalColumn: "node_id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_sync_node_status",
                schema: "msosync",
                table: "sync_node",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "IX_node_bootstrap_token_node",
                schema: "msosync",
                table: "sync_node_bootstrap_token",
                column: "node_id");

            migrationBuilder.CreateIndex(
                name: "IX_node_connectivity_history_node_time",
                schema: "msosync",
                table: "sync_node_connectivity_history",
                columns: new[] { "node_id", "occurred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_node_connectivity_history_time",
                schema: "msosync",
                table: "sync_node_connectivity_history",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "IX_node_lifecycle_history_node_time",
                schema: "msosync",
                table: "sync_node_lifecycle_history",
                columns: new[] { "node_id", "occurred_at" },
                descending: new[] { false, true });

            // 6. Seed lifecycle history: one row per node, FromState = NULL, Trigger = Migration
            migrationBuilder.Sql("""
                INSERT INTO [msosync].[sync_node_lifecycle_history]
                    (node_id, from_state, to_state, [trigger], reason, actor, occurred_at)
                SELECT node_id, NULL, status, 'Migration', 'M022 lifecycle model migration', 'system', SYSDATETIMEOFFSET()
                FROM [msosync].[sync_node];
                """);

            // 7. Permission seed (M018 pattern)
            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_permission",
                columns: ["PermissionKey", "DisplayName", "Description", "Category", "SortOrder", "IsSystem"],
                values: new object[,]
                {
                    { "PROVISION_NODES",       "Provision Nodes",       "Generate provisioning packages and bootstrap tokens",                    "OPERATIONS", 40, true },
                    { "MANAGE_NODE_LIFECYCLE", "Manage Node Lifecycle", "Enable, disable, maintenance, decommission, and force-complete nodes",  "OPERATIONS", 50, true },
                });

            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_role_permission",
                columns: ["RoleName", "PermissionKey"],
                values: new object[,]
                {
                    { "OPERATOR", "MANAGE_NODE_LIFECYCLE" },
                    { "ADMIN",    "PROVISION_NODES" },
                    { "ADMIN",    "MANAGE_NODE_LIFECYCLE" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Reverse permission seeds
            migrationBuilder.DeleteData(
                schema: "msosync",
                table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"],
                keyValues: new object[] { "OPERATOR", "MANAGE_NODE_LIFECYCLE" });

            migrationBuilder.DeleteData(
                schema: "msosync",
                table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"],
                keyValues: new object[] { "ADMIN", "PROVISION_NODES" });

            migrationBuilder.DeleteData(
                schema: "msosync",
                table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"],
                keyValues: new object[] { "ADMIN", "MANAGE_NODE_LIFECYCLE" });

            migrationBuilder.DeleteData(
                schema: "msosync",
                table: "sync_permission",
                keyColumn: "PermissionKey",
                keyValue: "PROVISION_NODES");

            migrationBuilder.DeleteData(
                schema: "msosync",
                table: "sync_permission",
                keyColumn: "PermissionKey",
                keyValue: "MANAGE_NODE_LIFECYCLE");

            // Drop history / bootstrap tables
            migrationBuilder.DropTable(
                name: "sync_node_bootstrap_token",
                schema: "msosync");

            migrationBuilder.DropTable(
                name: "sync_node_connectivity_history",
                schema: "msosync");

            migrationBuilder.DropTable(
                name: "sync_node_lifecycle_history",
                schema: "msosync");

            migrationBuilder.DropIndex(
                name: "IX_sync_node_status",
                schema: "msosync",
                table: "sync_node");

            // Drop new columns
            migrationBuilder.DropColumn(name: "connectivity_reason",              schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "consecutive_probe_failures",       schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "decommission_grace_until",         schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "decommission_initial_open_batches",schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "decommission_reason",              schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "decommission_started_at",          schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "last_probe_error",                 schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "maintenance_mode",                 schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "maintenance_reason",               schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "maintenance_started_at",           schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "maintenance_started_by",           schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "maintenance_until",                schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "previous_lifecycle_state",         schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "row_version",                      schema: "msosync", table: "sync_node");

            // Reverse-convert status values (lossy by design — hard cutover)
            migrationBuilder.Sql("""
                UPDATE [msosync].[sync_node] SET status = 'PENDING'     WHERE status = 'PendingApproval';
                UPDATE [msosync].[sync_node] SET status = 'PROVISIONED' WHERE status = 'PendingRegistration';
                UPDATE [msosync].[sync_node] SET status = 'REGISTERED'  WHERE status IN ('Active','Recovery','Decommissioning','Decommissioned');
                UPDATE [msosync].[sync_node] SET status = 'DISABLED'    WHERE status IN ('Disabled','Rejected');
                """);

            // Narrow status column back
            migrationBuilder.AlterColumn<string>(
                name: "status",
                schema: "msosync",
                table: "sync_node",
                type: "varchar(20)",
                unicode: false,
                maxLength: 20,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(30)",
                oldUnicode: false,
                oldMaxLength: 30);

            // Re-add sync_enabled
            migrationBuilder.AddColumn<bool>(
                name: "sync_enabled",
                schema: "msosync",
                table: "sync_node",
                type: "bit",
                nullable: false,
                defaultValue: true);
        }
    }
}
