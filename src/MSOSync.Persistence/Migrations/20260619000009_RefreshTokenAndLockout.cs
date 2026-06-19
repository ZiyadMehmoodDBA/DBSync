using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    [Migration("20260619000009_RefreshTokenAndLockout")]
    /// <inheritdoc />
    public partial class M009_RefreshTokenAndLockout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "locked_until",
                schema: "msosync",
                table: "sync_user",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "password_changed_at",
                schema: "msosync",
                table: "sync_user",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "sync_user_refresh_token",
                schema: "msosync",
                columns: table => new
                {
                    token_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    user_id = table.Column<long>(type: "bigint", nullable: false),
                    token_hash = table.Column<string>(type: "varchar(255)", unicode: false, maxLength: 255, nullable: false),
                    issued_at = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    expires_at = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    revoked_at = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                    family_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_user_refresh_token", x => x.token_id);
                    table.ForeignKey(
                        name: "FK_sync_user_refresh_token_user_id",
                        column: x => x.user_id,
                        principalSchema: "msosync",
                        principalTable: "sync_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sync_user_refresh_token_hash",
                schema: "msosync",
                table: "sync_user_refresh_token",
                column: "token_hash");

            migrationBuilder.CreateIndex(
                name: "IX_sync_user_refresh_token_user_id",
                schema: "msosync",
                table: "sync_user_refresh_token",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_user_refresh_token",
                schema: "msosync");

            migrationBuilder.DropColumn(
                name: "locked_until",
                schema: "msosync",
                table: "sync_user");

            migrationBuilder.DropColumn(
                name: "password_changed_at",
                schema: "msosync",
                table: "sync_user");
        }
    }
}
