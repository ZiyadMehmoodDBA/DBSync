using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M018_Permissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_permission",
                schema: "msosync",
                columns: table => new
                {
                    PermissionKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    Category = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false, defaultValue: 0),
                    IsSystem = table.Column<bool>(type: "bit", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_permission", x => x.PermissionKey);
                });

            migrationBuilder.CreateTable(
                name: "sync_role_permission",
                schema: "msosync",
                columns: table => new
                {
                    RoleName = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PermissionKey = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_role_permission", x => new { x.RoleName, x.PermissionKey });
                    table.ForeignKey(
                        name: "FK_sync_role_permission_sync_permission_PermissionKey",
                        column: x => x.PermissionKey,
                        principalSchema: "msosync",
                        principalTable: "sync_permission",
                        principalColumn: "PermissionKey",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sync_role_permission_PermissionKey",
                schema: "msosync",
                table: "sync_role_permission",
                column: "PermissionKey");

            // Seed permissions catalog
            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_permission",
                columns: ["PermissionKey", "DisplayName", "Description", "Category", "SortOrder", "IsSystem"],
                values: new object[,]
                {
                    { "VIEW_EVENTS",     "View Events",     "Access event list, filters, and details",                         "DATA",           10, true },
                    { "VIEW_METRICS",    "View Metrics",    "Access dashboard metrics and charts",                             "DATA",           20, true },
                    { "VIEW_AUDIT",      "View Audit",      "Access audit log and intelligence",                              "DATA",           30, true },
                    { "VIEW_TOPOLOGY",   "View Topology",   "Access topology graph and node details",                         "DATA",           40, true },
                    { "EXPORT_DATA",     "Export Data",     "Export events, batches, and audit records to CSV or JSON",        "DATA",           50, true },
                    { "RETRY_BATCHES",   "Retry Batches",   "Retry failed outgoing batches",                                  "OPERATIONS",     10, true },
                    { "APPROVE_NODES",   "Approve Nodes",   "Approve or reject node registration requests",                   "OPERATIONS",     20, true },
                    { "RELEASE_LOCKS",   "Release Locks",   "Release active sync locks",                                      "OPERATIONS",     30, true },
                    { "EDIT_PARAMETERS", "Edit Parameters", "Modify sync parameter values",                                   "CONFIGURATION",  10, true },
                    { "MANAGE_TRIGGERS", "Manage Triggers", "Create, edit, enable, disable, and delete triggers and routers", "CONFIGURATION",  20, true },
                    { "MANAGE_ROUTERS",  "Manage Routers",  "Create, edit, and delete routers and channels",                  "CONFIGURATION",  30, true },
                    { "MANAGE_USERS",    "Manage Users",    "Create, edit, and delete user accounts",                         "ADMINISTRATION", 10, true },
                });

            // Seed VIEWER permissions
            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_role_permission",
                columns: ["RoleName", "PermissionKey"],
                values: new object[,]
                {
                    { "VIEWER", "VIEW_EVENTS" },
                    { "VIEWER", "VIEW_METRICS" },
                    { "VIEWER", "VIEW_AUDIT" },
                    { "VIEWER", "VIEW_TOPOLOGY" },
                });

            // Seed OPERATOR permissions (VIEWER set + extras)
            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_role_permission",
                columns: ["RoleName", "PermissionKey"],
                values: new object[,]
                {
                    { "OPERATOR", "VIEW_EVENTS" },
                    { "OPERATOR", "VIEW_METRICS" },
                    { "OPERATOR", "VIEW_AUDIT" },
                    { "OPERATOR", "VIEW_TOPOLOGY" },
                    { "OPERATOR", "EXPORT_DATA" },
                    { "OPERATOR", "RETRY_BATCHES" },
                    { "OPERATOR", "APPROVE_NODES" },
                    { "OPERATOR", "RELEASE_LOCKS" },
                    { "OPERATOR", "EDIT_PARAMETERS" },
                    { "OPERATOR", "MANAGE_TRIGGERS" },
                    { "OPERATOR", "MANAGE_ROUTERS" },
                });

            // Seed ADMIN permissions (all 12)
            migrationBuilder.InsertData(
                schema: "msosync",
                table: "sync_role_permission",
                columns: ["RoleName", "PermissionKey"],
                values: new object[,]
                {
                    { "ADMIN", "VIEW_EVENTS" },
                    { "ADMIN", "VIEW_METRICS" },
                    { "ADMIN", "VIEW_AUDIT" },
                    { "ADMIN", "VIEW_TOPOLOGY" },
                    { "ADMIN", "EXPORT_DATA" },
                    { "ADMIN", "RETRY_BATCHES" },
                    { "ADMIN", "APPROVE_NODES" },
                    { "ADMIN", "RELEASE_LOCKS" },
                    { "ADMIN", "EDIT_PARAMETERS" },
                    { "ADMIN", "MANAGE_TRIGGERS" },
                    { "ADMIN", "MANAGE_ROUTERS" },
                    { "ADMIN", "MANAGE_USERS" },
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Delete seed data in reverse order
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "MANAGE_USERS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "MANAGE_ROUTERS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "MANAGE_TRIGGERS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "EDIT_PARAMETERS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "RELEASE_LOCKS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "APPROVE_NODES" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "RETRY_BATCHES" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "EXPORT_DATA" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "VIEW_TOPOLOGY" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "VIEW_AUDIT" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "VIEW_METRICS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "ADMIN", "VIEW_EVENTS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "MANAGE_ROUTERS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "MANAGE_TRIGGERS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "EDIT_PARAMETERS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "RELEASE_LOCKS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "APPROVE_NODES" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "RETRY_BATCHES" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "EXPORT_DATA" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "VIEW_TOPOLOGY" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "VIEW_AUDIT" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "VIEW_METRICS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "OPERATOR", "VIEW_EVENTS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "VIEWER", "VIEW_TOPOLOGY" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "VIEWER", "VIEW_AUDIT" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "VIEWER", "VIEW_METRICS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_role_permission",
                keyColumns: ["RoleName", "PermissionKey"], keyValues: new object[] { "VIEWER", "VIEW_EVENTS" });
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "MANAGE_USERS");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "MANAGE_ROUTERS");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "MANAGE_TRIGGERS");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "EDIT_PARAMETERS");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "RELEASE_LOCKS");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "APPROVE_NODES");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "RETRY_BATCHES");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "EXPORT_DATA");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "VIEW_TOPOLOGY");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "VIEW_AUDIT");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "VIEW_METRICS");
            migrationBuilder.DeleteData(schema: "msosync", table: "sync_permission",
                keyColumn: "PermissionKey", keyValue: "VIEW_EVENTS");

            migrationBuilder.DropTable(
                name: "sync_role_permission",
                schema: "msosync");

            migrationBuilder.DropTable(
                name: "sync_permission",
                schema: "msosync");
        }
    }
}
