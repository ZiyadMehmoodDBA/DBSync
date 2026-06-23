using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    public partial class M014_SecurityAndHeartbeat : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // sync_node — heartbeat columns
            migrationBuilder.AddColumn<string>(
                name: "upstream_node_id",
                schema: "msosync",
                table: "sync_node",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "last_probe_time",
                schema: "msosync",
                table: "sync_node",
                type: "datetime2(7)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "last_probe_latency_ms",
                schema: "msosync",
                table: "sync_node",
                nullable: true);

            migrationBuilder.AddColumn<byte>(
                name: "connectivity_status",
                schema: "msosync",
                table: "sync_node",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)0);

            migrationBuilder.AddForeignKey(
                name: "FK_sync_node_upstream_node_id",
                schema: "msosync",
                table: "sync_node",
                column: "upstream_node_id",
                principalSchema: "msosync",
                principalTable: "sync_node",
                principalColumn: "node_id",
                onDelete: ReferentialAction.NoAction);

            migrationBuilder.CreateIndex(
                name: "IX_sync_node_upstream",
                schema: "msosync",
                table: "sync_node",
                column: "upstream_node_id");

            // sync_user_refresh_token — three-step lookup hash
            migrationBuilder.AddColumn<string>(
                name: "token_lookup_hash",
                schema: "msosync",
                table: "sync_user_refresh_token",
                type: "char(64)",
                maxLength: 64,
                unicode: false,
                nullable: true);

            // Backfill: hash of token_id (existing tokens become unreachable — they expire naturally)
            migrationBuilder.Sql(
                "UPDATE msosync.sync_user_refresh_token " +
                "SET token_lookup_hash = LOWER(CONVERT(CHAR(64), HASHBYTES('SHA2_256', CONVERT(NVARCHAR(20), token_id)), 2)) " +
                "WHERE token_lookup_hash IS NULL");

            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX IX_sync_user_refresh_token_lookup_hash " +
                "ON msosync.sync_user_refresh_token(token_lookup_hash) " +
                "WHERE revoked_at IS NULL");

            migrationBuilder.AlterColumn<string>(
                name: "token_lookup_hash",
                schema: "msosync",
                table: "sync_user_refresh_token",
                type: "char(64)",
                maxLength: 64,
                unicode: false,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "char(64)",
                oldMaxLength: 64,
                oldUnicode: false,
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP INDEX IF EXISTS IX_sync_user_refresh_token_lookup_hash " +
                "ON msosync.sync_user_refresh_token");

            migrationBuilder.DropColumn(
                name: "token_lookup_hash",
                schema: "msosync",
                table: "sync_user_refresh_token");

            migrationBuilder.DropForeignKey(
                name: "FK_sync_node_upstream_node_id",
                schema: "msosync",
                table: "sync_node");

            migrationBuilder.DropIndex(
                name: "IX_sync_node_upstream",
                schema: "msosync",
                table: "sync_node");

            migrationBuilder.DropColumn(name: "upstream_node_id", schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "last_probe_time", schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "last_probe_latency_ms", schema: "msosync", table: "sync_node");
            migrationBuilder.DropColumn(name: "connectivity_status", schema: "msosync", table: "sync_node");
        }
    }
}
