using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M021_AddNodeTypeExternalId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "external_id",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "node_name",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "node_type",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "external_id",
                schema: "msosync",
                table: "sync_node");

            migrationBuilder.DropColumn(
                name: "node_name",
                schema: "msosync",
                table: "sync_node");

            migrationBuilder.DropColumn(
                name: "node_type",
                schema: "msosync",
                table: "sync_node");
        }
    }
}
