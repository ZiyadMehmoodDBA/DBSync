using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("M043_ApiKeysServiceAccounts")]
public partial class M043_ApiKeysServiceAccounts : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Create sync_user_api_key table
        migrationBuilder.CreateTable(
            name:   "sync_user_api_key",
            schema: Schema,
            columns: table => new
            {
                id              = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                user_id         = table.Column<long>(type: "bigint", nullable: false),
                key_hash        = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                key_prefix      = table.Column<string>(type: "nvarchar(8)", maxLength: 8, nullable: false),
                name            = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                created_at      = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                last_used_at    = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                expires_at      = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                is_revoked      = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                revoked_at      = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_user_api_key", x => x.id);
                table.ForeignKey(
                    name:          "FK_sync_user_api_key_user_id",
                    column:        x => x.user_id,
                    principalSchema: Schema,
                    principalTable: "sync_user",
                    principalColumn: "user_id",
                    onDelete:      ReferentialAction.Cascade);
            });

        // Create sync_service_account table
        migrationBuilder.CreateTable(
            name:   "sync_service_account",
            schema: Schema,
            columns: table => new
            {
                id                  = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                name                = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                description         = table.Column<string>(type: "nvarchar(1024)", maxLength: 1024, nullable: true),
                client_id           = table.Column<string>(type: "nvarchar(128)", maxLength: 128, nullable: false),
                client_secret_hash  = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                created_at          = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                last_used_at        = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                expires_at          = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                is_enabled          = table.Column<bool>(type: "bit", nullable: false, defaultValue: true),
                is_revoked          = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                revoked_at          = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                revoked_reason      = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_service_account", x => x.id);
            });

        // Create indexes on sync_user_api_key
        migrationBuilder.CreateIndex(
            name:   "IX_sync_user_api_key_user_id",
            schema: Schema,
            table:  "sync_user_api_key",
            column: "user_id");

        migrationBuilder.CreateIndex(
            name:   "IX_sync_user_api_key_key_hash",
            schema: Schema,
            table:  "sync_user_api_key",
            column: "key_hash",
            unique: true);

        // Create indexes on sync_service_account
        migrationBuilder.CreateIndex(
            name:   "IX_sync_service_account_client_id",
            schema: Schema,
            table:  "sync_service_account",
            column: "client_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name:   "IX_sync_service_account_is_enabled",
            schema: Schema,
            table:  "sync_service_account",
            column: "is_enabled");

        // Index for ValidateServiceAccountKeyAsync — filters by client_secret_hash per auth request.
        migrationBuilder.CreateIndex(
            name:   "IX_sync_service_account_client_secret_hash",
            schema: Schema,
            table:  "sync_service_account",
            column: "client_secret_hash",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name:   "IX_sync_service_account_client_secret_hash",
            schema: Schema,
            table:  "sync_service_account");

        migrationBuilder.DropTable(name: "sync_user_api_key", schema: Schema);
        migrationBuilder.DropTable(name: "sync_service_account", schema: Schema);
    }
}
