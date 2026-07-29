using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("M042_TotpMfa")]
public partial class M042_TotpMfa : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Add IsMfaEnabled column to sync_user
        migrationBuilder.AddColumn<bool>(
            name:         "is_mfa_enabled",
            schema:       Schema,
            table:        "sync_user",
            type:         "bit",
            nullable:     false,
            defaultValue: false);

        // Create sync_user_totp_secret table
        migrationBuilder.CreateTable(
            name:   "sync_user_totp_secret",
            schema: Schema,
            columns: table => new
            {
                user_id  = table.Column<long>(type: "bigint", nullable: false),
                secret   = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                is_enabled = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                enabled_at = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_user_totp_secret", x => x.user_id);
                table.ForeignKey(
                    name:          "FK_sync_user_totp_secret_user_id",
                    column:        x => x.user_id,
                    principalSchema: Schema,
                    principalTable: "sync_user",
                    principalColumn: "user_id",
                    onDelete:      ReferentialAction.Cascade);
            });

        // Create sync_user_backup_code table
        migrationBuilder.CreateTable(
            name:   "sync_user_backup_code",
            schema: Schema,
            columns: table => new
            {
                id       = table.Column<int>(type: "int", nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                user_id  = table.Column<long>(type: "bigint", nullable: false),
                code_hash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                is_used  = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                used_at  = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_user_backup_code", x => x.id);
                table.ForeignKey(
                    name:          "FK_sync_user_backup_code_user_id",
                    column:        x => x.user_id,
                    principalSchema: Schema,
                    principalTable: "sync_user",
                    principalColumn: "user_id",
                    onDelete:      ReferentialAction.Cascade);
            });

        // Create index on sync_user_backup_code.user_id
        migrationBuilder.CreateIndex(
            name:   "IX_sync_user_backup_code_user_id",
            schema: Schema,
            table:  "sync_user_backup_code",
            column: "user_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_user_backup_code", schema: Schema);
        migrationBuilder.DropTable(name: "sync_user_totp_secret", schema: Schema);
        migrationBuilder.DropColumn(name: "is_mfa_enabled", schema: Schema, table: "sync_user");
    }
}
