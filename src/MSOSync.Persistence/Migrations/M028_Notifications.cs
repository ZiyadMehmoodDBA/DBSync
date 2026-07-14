// src/MSOSync.Persistence/Migrations/M028_Notifications.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    public partial class M028_Notifications : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_notification",
                schema: "msosync",
                columns: table => new
                {
                    notification_id  = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    event_type        = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    severity          = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    title             = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    body              = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    source_entity_type = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    source_entity_id  = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    dedup_key         = table.Column<string>(type: "nvarchar(260)", maxLength: 260, nullable: true),
                    occurrence_count  = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    correlation_id    = table.Column<string>(type: "varchar(100)", unicode: false, maxLength: 100, nullable: true),
                    created_at        = table.Column<DateTime>(type: "datetime2", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                    last_occurred_at  = table.Column<DateTime>(type: "datetime2", nullable: false,
                        defaultValueSql: "SYSUTCDATETIME()"),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_notification", x => x.notification_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_user_notification",
                schema: "msosync",
                columns: table => new
                {
                    user_id         = table.Column<long>(type: "bigint", nullable: false),
                    notification_id = table.Column<long>(type: "bigint", nullable: false),
                    is_read         = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    read_at         = table.Column<DateTime>(type: "datetime2", nullable: true),
                    is_archived     = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    archived_at     = table.Column<DateTime>(type: "datetime2", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_user_notification", x => new { x.user_id, x.notification_id });
                    table.ForeignKey(
                        name: "FK_sun_user",
                        column: x => x.user_id,
                        principalSchema: "msosync",
                        principalTable: "sync_user",
                        principalColumn: "user_id");
                    table.ForeignKey(
                        name: "FK_sun_notif",
                        column: x => x.notification_id,
                        principalSchema: "msosync",
                        principalTable: "sync_notification",
                        principalColumn: "notification_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_sn_dedup",
                schema: "msosync",
                table: "sync_notification",
                columns: new[] { "dedup_key", "created_at" },
                filter: "[dedup_key] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_sun_user_unread",
                schema: "msosync",
                table: "sync_user_notification",
                columns: new[] { "user_id", "is_read", "notification_id" });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "sync_user_notification", schema: "msosync");
            migrationBuilder.DropTable(name: "sync_notification", schema: "msosync");
        }
    }
}
