using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    public partial class M027_NodeSyncScope : Migration
    {
        private static readonly string Schema =
            System.Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "sync_node_scope",
                schema: Schema,
                columns: table => new
                {
                    node_id            = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    sync_direction     = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, defaultValue: "Bidirectional"),
                    initial_load_policy = table.Column<string>(type: "varchar(30)", maxLength: 30, nullable: false, defaultValue: "None"),
                    created_time       = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                    updated_time       = table.Column<DateTime>(type: "datetime2(7)", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_scope", x => x.node_id);
                });

            migrationBuilder.CreateTable(
                name: "sync_node_channel",
                schema: Schema,
                columns: table => new
                {
                    node_id    = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    channel_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_channel", x => new { x.node_id, x.channel_id });
                });

            migrationBuilder.CreateTable(
                name: "sync_node_trigger",
                schema: Schema,
                columns: table => new
                {
                    node_id    = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    trigger_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_trigger", x => new { x.node_id, x.trigger_id });
                });

            migrationBuilder.CreateTable(
                name: "sync_node_router",
                schema: Schema,
                columns: table => new
                {
                    node_id   = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                    router_id = table.Column<string>(type: "varchar(50)", maxLength: 50, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_node_router", x => new { x.node_id, x.router_id });
                });
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "sync_node_router",  schema: Schema);
            migrationBuilder.DropTable(name: "sync_node_trigger", schema: Schema);
            migrationBuilder.DropTable(name: "sync_node_channel", schema: Schema);
            migrationBuilder.DropTable(name: "sync_node_scope",   schema: Schema);
        }
    }
}
