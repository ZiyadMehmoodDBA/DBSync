using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("M041_OidcConfiguration")]
public partial class M041_OidcConfiguration : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add OIDC identity columns to sync_user
        migrationBuilder.AddColumn<string>(
            name:      "external_id",
            schema:    Schema,
            table:     "sync_user",
            type:      "nvarchar(500)",
            maxLength: 500,
            nullable:  true);

        migrationBuilder.AddColumn<string>(
            name:      "auth_provider",
            schema:    Schema,
            table:     "sync_user",
            type:      "nvarchar(100)",
            maxLength: 100,
            nullable:  true);

        migrationBuilder.AddColumn<string>(
            name:      "email",
            schema:    Schema,
            table:     "sync_user",
            type:      "nvarchar(320)",
            maxLength: 320,
            nullable:  true);

        // Create oidc_configuration table
        migrationBuilder.CreateTable(
            name:   "oidc_configuration",
            schema: Schema,
            columns: table => new
            {
                id = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                name             = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                authority        = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                client_id        = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                client_secret_key = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                scopes           = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false, defaultValue: "openid profile email"),
                callback_path    = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false, defaultValue: "/auth/oidc/callback"),
                is_enabled       = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                created_at       = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_oidc_configuration", x => x.id);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "oidc_configuration", schema: Schema);

        migrationBuilder.DropColumn(name: "external_id",   schema: Schema, table: "sync_user");
        migrationBuilder.DropColumn(name: "auth_provider", schema: Schema, table: "sync_user");
        migrationBuilder.DropColumn(name: "email",         schema: Schema, table: "sync_user");
    }
}
