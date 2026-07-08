using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M023_ConfigurationManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. New tables
            migrationBuilder.CreateTable(
                name: "sync_configuration_template",
                schema: "msosync",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "NEWID()"),
                    name = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    current_published_version = table.Column<int>(nullable: true),
                    latest_draft_version = table.Column<int>(nullable: true),
                    created_by = table.Column<Guid>(nullable: false),
                    created_at = table.Column<DateTime>(nullable: false),
                    updated_at = table.Column<DateTime>(nullable: false),
                },
                constraints: table => table.PrimaryKey("PK_sync_configuration_template", x => x.id));

            migrationBuilder.CreateTable(
                name: "sync_configuration_template_version",
                schema: "msosync",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "NEWID()"),
                    template_id = table.Column<Guid>(nullable: false),
                    version_number = table.Column<int>(nullable: false),
                    is_draft = table.Column<bool>(nullable: false),
                    settings_json = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    template_content_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    schema_version = table.Column<int>(nullable: false, defaultValue: 1),
                    row_version = table.Column<byte[]>(rowVersion: true, nullable: false),
                    published_at = table.Column<DateTime>(nullable: true),
                    published_by = table.Column<Guid>(nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_configuration_template_version", x => x.id);
                    table.ForeignKey("FK_template_version_template", x => x.template_id,
                        principalSchema: "msosync", principalTable: "sync_configuration_template",
                        principalColumn: "id", onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "sync_node_configuration_override",
                schema: "msosync",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "NEWID()"),
                    node_id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    setting_key = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    setting_value = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    override_source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    updated_by = table.Column<Guid>(nullable: false),
                    updated_at = table.Column<DateTime>(nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_configuration_override", x => x.id);
                    table.ForeignKey("FK_node_config_override_node", x => x.node_id,
                        principalSchema: "msosync", principalTable: "sync_node",
                        principalColumn: "node_id", onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "sync_node_configuration_history",
                schema: "msosync",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "NEWID()"),
                    node_id = table.Column<string>(type: "varchar(50)", unicode: false, maxLength: 50, nullable: false),
                    event_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    template_id = table.Column<Guid>(nullable: true),
                    template_version = table.Column<int>(nullable: true),
                    configuration_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    correlation_id = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    actor_id = table.Column<Guid>(nullable: true),
                    occurred_at = table.Column<DateTime>(nullable: false),
                    notes = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_configuration_history", x => x.id);
                    table.ForeignKey("FK_node_config_history_node", x => x.node_id,
                        principalSchema: "msosync", principalTable: "sync_node",
                        principalColumn: "node_id", onDelete: ReferentialAction.NoAction);
                });

            migrationBuilder.CreateTable(
                name: "sync_configuration_rollout",
                schema: "msosync",
                columns: table => new
                {
                    id = table.Column<Guid>(nullable: false, defaultValueSql: "NEWID()"),
                    status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    template_id = table.Column<Guid>(nullable: false),
                    template_version = table.Column<int>(nullable: false),
                    target_node_count = table.Column<int>(nullable: false),
                    applied_count = table.Column<int>(nullable: false, defaultValue: 0),
                    failed_count = table.Column<int>(nullable: false, defaultValue: 0),
                    pending_count = table.Column<int>(nullable: false, defaultValue: 0),
                    progress_percent = table.Column<int>(nullable: false, defaultValue: 0),
                    initiated_by = table.Column<Guid>(nullable: false),
                    started_at = table.Column<DateTime>(nullable: false),
                    completed_at = table.Column<DateTime>(nullable: true),
                },
                constraints: table => table.PrimaryKey("PK_sync_configuration_rollout", x => x.id));

            // 2. New indexes
            migrationBuilder.CreateIndex("UX_sync_configuration_template_name", "sync_configuration_template",
                "name", schema: "msosync", unique: true);
            migrationBuilder.CreateIndex("UX_template_version_number", "sync_configuration_template_version",
                new[] { "template_id", "version_number" }, schema: "msosync", unique: true);
            migrationBuilder.CreateIndex("UX_template_single_draft", "sync_configuration_template_version",
                "template_id", schema: "msosync", unique: true, filter: "[is_draft] = 1");
            migrationBuilder.CreateIndex("UX_node_override_key", "sync_node_configuration_override",
                new[] { "node_id", "setting_key" }, schema: "msosync", unique: true);
            migrationBuilder.CreateIndex("IX_node_config_history_node_time", "sync_node_configuration_history",
                new[] { "node_id", "occurred_at" }, schema: "msosync");
            migrationBuilder.CreateIndex("IX_rollout_status", "sync_configuration_rollout",
                "status", schema: "msosync");

            // 3. SyncNode columns (all nullable — zero impact on existing rows)
            migrationBuilder.AddColumn<Guid>("assigned_template_id", "sync_node", schema: "msosync", nullable: true);
            migrationBuilder.AddColumn<int>("assigned_template_version", "sync_node", schema: "msosync", nullable: true);
            migrationBuilder.AddColumn<int>("applied_template_version", "sync_node", schema: "msosync", nullable: true);
            migrationBuilder.AddColumn<string>("expected_effective_hash", "sync_node", type: "nvarchar(64)",
                maxLength: 64, schema: "msosync", nullable: true);
            migrationBuilder.AddColumn<string>("applied_effective_hash", "sync_node", type: "nvarchar(64)",
                maxLength: 64, schema: "msosync", nullable: true);
            migrationBuilder.AddColumn<string>("configuration_state", "sync_node", type: "nvarchar(20)",
                maxLength: 20, schema: "msosync", nullable: true);
            migrationBuilder.AddColumn<DateTime>("configuration_status_reported_at", "sync_node",
                schema: "msosync", nullable: true);
            migrationBuilder.AddColumn<DateTime>("last_applied_at", "sync_node", schema: "msosync", nullable: true);

            // 4. MANAGE_CONFIGURATIONS permission seed
            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_permission",
                columns: ["PermissionKey", "DisplayName", "Description", "Category", "SortOrder", "IsSystem"],
                values: new object[,]
                {
                    { "MANAGE_CONFIGURATIONS", "Manage Configurations",
                      "Author templates, assign configurations to nodes, manage overrides and rollouts",
                      "OPERATIONS", 60, true },
                });

            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_role_permission",
                columns: ["RoleName", "PermissionKey"],
                values: new object[,]
                {
                    { "ADMIN", "MANAGE_CONFIGURATIONS" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Delete permission seeds
            migrationBuilder.DeleteData("sync_role_permission", "PermissionKey", "MANAGE_CONFIGURATIONS", schema: "msosync");
            migrationBuilder.DeleteData("sync_permission", "PermissionKey", "MANAGE_CONFIGURATIONS", schema: "msosync");

            // Remove SyncNode columns
            migrationBuilder.DropColumn("assigned_template_id", "sync_node", schema: "msosync");
            migrationBuilder.DropColumn("assigned_template_version", "sync_node", schema: "msosync");
            migrationBuilder.DropColumn("applied_template_version", "sync_node", schema: "msosync");
            migrationBuilder.DropColumn("expected_effective_hash", "sync_node", schema: "msosync");
            migrationBuilder.DropColumn("applied_effective_hash", "sync_node", schema: "msosync");
            migrationBuilder.DropColumn("configuration_state", "sync_node", schema: "msosync");
            migrationBuilder.DropColumn("configuration_status_reported_at", "sync_node", schema: "msosync");
            migrationBuilder.DropColumn("last_applied_at", "sync_node", schema: "msosync");

            // Drop tables
            migrationBuilder.DropTable("sync_node_configuration_history", schema: "msosync");
            migrationBuilder.DropTable("sync_node_configuration_override", schema: "msosync");
            migrationBuilder.DropTable("sync_configuration_rollout", schema: "msosync");
            migrationBuilder.DropTable("sync_configuration_template_version", schema: "msosync");
            migrationBuilder.DropTable("sync_configuration_template", schema: "msosync");
        }
    }
}
