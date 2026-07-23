using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("M036_PluginPackagingColumns")]
public partial class M036_PluginPackagingColumns : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name:      "package_hash",
            schema:    Schema,
            table:     "sync_plugin",
            type:      "nvarchar(64)",
            maxLength: 64,
            nullable:  true);

        migrationBuilder.AddColumn<string>(
            name:      "signed_by",
            schema:    Schema,
            table:     "sync_plugin",
            type:      "nvarchar(200)",
            maxLength: 200,
            nullable:  true);

        migrationBuilder.AddColumn<string>(
            name:      "signature_algorithm",
            schema:    Schema,
            table:     "sync_plugin",
            type:      "nvarchar(50)",
            maxLength: 50,
            nullable:  true);

        migrationBuilder.AddColumn<bool>(
            name:         "is_package_install",
            schema:       Schema,
            table:        "sync_plugin",
            type:         "bit",
            nullable:     false,
            defaultValue: false);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "package_hash",        schema: Schema, table: "sync_plugin");
        migrationBuilder.DropColumn(name: "signed_by",           schema: Schema, table: "sync_plugin");
        migrationBuilder.DropColumn(name: "signature_algorithm", schema: Schema, table: "sync_plugin");
        migrationBuilder.DropColumn(name: "is_package_install",  schema: Schema, table: "sync_plugin");
    }
}
