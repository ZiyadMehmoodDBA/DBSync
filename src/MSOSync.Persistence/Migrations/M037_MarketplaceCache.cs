using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M037_MarketplaceCache : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sync_marketplace_cache",
            schema: Schema,
            columns: t => new
            {
                id             = t.Column<int>(type: "int", nullable: false)
                                  .Annotation("SqlServer:Identity", "1, 1"),
                registry_url   = t.Column<string>(type: "nvarchar(500)",  maxLength: 500,  nullable: false),
                plugin_id      = t.Column<string>(type: "nvarchar(200)",  maxLength: 200,  nullable: false),
                latest_version = t.Column<string>(type: "nvarchar(50)",   maxLength: 50,   nullable: false),
                metadata_json  = t.Column<string>(type: "nvarchar(max)",               nullable: false),
                cached_at      = t.Column<DateTime>(type: "datetime2",                nullable: false),
                expires_at     = t.Column<DateTime>(type: "datetime2",                nullable: false),
            },
            constraints: t =>
            {
                t.PrimaryKey("PK_sync_marketplace_cache", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sync_marketplace_cache_registry_plugin",
            schema: Schema,
            table: "sync_marketplace_cache",
            columns: new[] { "registry_url", "plugin_id" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sync_marketplace_cache_expires_at",
            schema: Schema,
            table: "sync_marketplace_cache",
            column: "expires_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sync_marketplace_cache",
            schema: Schema);
    }
}
