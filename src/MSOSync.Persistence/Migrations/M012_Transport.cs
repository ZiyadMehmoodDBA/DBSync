using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M012_Transport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // --- sync_node: add transport_mode ---
            migrationBuilder.AddColumn<byte>(
                name: "transport_mode",
                schema: "msosync",
                table: "sync_node",
                type: "tinyint",
                nullable: false,
                defaultValue: (byte)1);   // Pull = 1

            migrationBuilder.Sql(
                "ALTER TABLE [msosync].[sync_node] " +
                "ADD CONSTRAINT CK_sync_node_transport_mode " +
                "CHECK (transport_mode IN (1, 2))");

            // --- sync_incoming_batch: add batch_sequence, source_node_id, received_time ---
            migrationBuilder.AddColumn<long>(
                name: "batch_sequence",
                schema: "msosync",
                table: "sync_incoming_batch",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<string>(
                name: "source_node_id",
                schema: "msosync",
                table: "sync_incoming_batch",
                type: "varchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "received_time",
                schema: "msosync",
                table: "sync_incoming_batch",
                type: "datetime2(7)",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            // FK: source_node_id → sync_node.node_id
            migrationBuilder.AddForeignKey(
                name: "FK_sync_incoming_batch_source_node",
                schema: "msosync",
                table: "sync_incoming_batch",
                column: "source_node_id",
                principalSchema: "msosync",
                principalTable: "sync_node",
                principalColumn: "node_id",
                onDelete: ReferentialAction.Restrict);

            // Index for sequence lookups
            migrationBuilder.CreateIndex(
                name: "IX_sync_incoming_batch_source_channel_sequence",
                schema: "msosync",
                table: "sync_incoming_batch",
                columns: new[] { "source_node_id", "channel_id", "batch_sequence" });

            // Unique constraint: prevent duplicate replay at DB level
            migrationBuilder.Sql(
                "ALTER TABLE [msosync].[sync_incoming_batch] " +
                "ADD CONSTRAINT UQ_sync_incoming_batch_source_sequence " +
                "UNIQUE (source_node_id, batch_sequence)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE [msosync].[sync_incoming_batch] " +
                "DROP CONSTRAINT UQ_sync_incoming_batch_source_sequence");

            migrationBuilder.DropIndex(
                name: "IX_sync_incoming_batch_source_channel_sequence",
                schema: "msosync",
                table: "sync_incoming_batch");

            migrationBuilder.DropForeignKey(
                name: "FK_sync_incoming_batch_source_node",
                schema: "msosync",
                table: "sync_incoming_batch");

            migrationBuilder.DropColumn(name: "received_time", schema: "msosync", table: "sync_incoming_batch");
            migrationBuilder.DropColumn(name: "source_node_id", schema: "msosync", table: "sync_incoming_batch");
            migrationBuilder.DropColumn(name: "batch_sequence", schema: "msosync", table: "sync_incoming_batch");

            migrationBuilder.Sql(
                "ALTER TABLE [msosync].[sync_node] " +
                "DROP CONSTRAINT CK_sync_node_transport_mode");

            migrationBuilder.DropColumn(name: "transport_mode", schema: "msosync", table: "sync_node");
        }
    }
}
