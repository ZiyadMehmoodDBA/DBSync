using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M017_UserPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_user_preference",
                schema: "msosync",
                columns: table => new
                {
                    PreferenceId = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UserId = table.Column<long>(type: "bigint", nullable: false),
                    PreferenceKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PreferenceValue = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2(7)", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_user_preference", x => x.PreferenceId);
                    table.ForeignKey(
                        name: "FK_sync_user_preference_sync_user_UserId",
                        column: x => x.UserId,
                        principalSchema: "msosync",
                        principalTable: "sync_user",
                        principalColumn: "user_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sync_user_preference_user_key",
                schema: "msosync",
                table: "sync_user_preference",
                columns: new[] { "UserId", "PreferenceKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_user_preference",
                schema: "msosync");
        }
    }
}
