using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    public partial class M029_Plugins : Migration
    {
        private const string Schema = "msosync";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_plugin",
                schema: Schema,
                columns: table => new
                {
                    plugin_id      = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    plugin_name    = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    plugin_version = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: false),
                    status         = table.Column<string>(type: "nvarchar(20)",  maxLength: 20,  nullable: false),
                    enabled        = table.Column<bool>  (type: "bit",                           nullable: false, defaultValue: true),
                    installed_at   = table.Column<DateTime>(type: "datetime2",                   nullable: false),
                    last_seen_at   = table.Column<DateTime>(type: "datetime2",                   nullable: false),
                    last_error     = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    manifest_hash  = table.Column<string>(type: "nvarchar(64)",  maxLength: 64,  nullable: true),
                    host_version   = table.Column<string>(type: "nvarchar(50)",  maxLength: 50,  nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_plugin", x => x.plugin_id);
                });

            // Seed MANAGE_PLUGINS permission (Admin-only) — use Sql() because InsertData
            // cannot resolve column types for tables created in previous migrations.
            migrationBuilder.Sql($"""
                INSERT INTO [{Schema}].[sync_permission]
                    ([PermissionKey], [DisplayName], [Description], [Category], [SortOrder], [IsSystem])
                VALUES
                    ('MANAGE_PLUGINS', 'Manage Plugins', 'View and manage loaded plugins', 'ADMINISTRATION', 50, 1);

                INSERT INTO [{Schema}].[sync_role_permission]
                    ([RoleName], [PermissionKey])
                VALUES
                    ('ADMIN', 'MANAGE_PLUGINS');
                """);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                DELETE FROM [{Schema}].[sync_role_permission]
                WHERE [RoleName] = 'ADMIN' AND [PermissionKey] = 'MANAGE_PLUGINS';

                DELETE FROM [{Schema}].[sync_permission]
                WHERE [PermissionKey] = 'MANAGE_PLUGINS';
                """);

            migrationBuilder.DropTable(name: "sync_plugin", schema: Schema);
        }
    }
}
