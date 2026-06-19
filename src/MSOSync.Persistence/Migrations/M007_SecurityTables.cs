// src/MSOSync.Persistence/Migrations/M007_SecurityTables.cs
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("20260619000007_SecurityTables")]
public partial class M007_SecurityTables : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_user",
            schema: "msosync",
            columns: table => new
            {
                user_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                username = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                password_hash = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                enabled = table.Column<bool>(nullable: false, defaultValue: true),
                last_login = table.Column<DateTime>(type: "datetime2(7)", nullable: true),
                failed_attempts = table.Column<int>(nullable: false, defaultValue: 0),
                created_time = table.Column<DateTime>(type: "datetime2(7)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_sync_user", x => x.user_id));

        migrationBuilder.CreateIndex(
            name: "UQ_sync_user_username",
            schema: "msosync",
            table: "sync_user",
            column: "username",
            unique: true);

        migrationBuilder.CreateTable(
            name: "sync_role",
            schema: "msosync",
            columns: table => new
            {
                role_id = table.Column<long>(nullable: false)
                    .Annotation("SqlServer:Identity", "1, 1"),
                role_name = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_sync_role", x => x.role_id));

        migrationBuilder.CreateIndex(
            name: "UQ_sync_role_role_name",
            schema: "msosync",
            table: "sync_role",
            column: "role_name",
            unique: true);

        migrationBuilder.CreateTable(
            name: "sync_user_role",
            schema: "msosync",
            columns: table => new
            {
                user_id = table.Column<long>(nullable: false),
                role_id = table.Column<long>(nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sync_user_role", x => new { x.user_id, x.role_id });
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "sync_user_role", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_role", schema: "msosync");
        migrationBuilder.DropTable(name: "sync_user", schema: "msosync");
    }
}
