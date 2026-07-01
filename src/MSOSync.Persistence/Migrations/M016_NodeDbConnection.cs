using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M016_NodeDbConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "db_server",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "db_name",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "db_auth_mode",
                schema: "msosync",
                table: "sync_node",
                type: "varchar(10)",
                unicode: false,
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "db_user",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "db_password_encrypted",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "db_password_encrypted", schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "db_user",               schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "db_auth_mode",          schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "db_name",               schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "db_server",             schema: "msosync", table: "sync_node");
        }
    }
}
