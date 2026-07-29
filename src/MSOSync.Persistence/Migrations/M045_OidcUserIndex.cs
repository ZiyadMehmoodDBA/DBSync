using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

/// <summary>
/// Adds a composite index on sync_user(external_id, auth_provider) to speed up the
/// OIDC ProvisionAsync lookup (OidcUserProvisioningService.ProvisionAsync filters by both columns
/// on every OIDC login). The filtered index only covers rows where external_id IS NOT NULL.
/// </summary>
[Migration("M045_OidcUserIndex")]
public partial class M045_OidcUserIndex : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name:    "IX_sync_user_external_id_auth_provider",
            schema:  Schema,
            table:   "sync_user",
            columns: ["external_id", "auth_provider"],
            unique:  false,
            filter:  "[external_id] IS NOT NULL");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name:   "IX_sync_user_external_id_auth_provider",
            schema: Schema,
            table:  "sync_user");
    }
}
