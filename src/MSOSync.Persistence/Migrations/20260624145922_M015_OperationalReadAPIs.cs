using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M015_OperationalReadAPIs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Add create_time to sync_batch_error (nullable → backfill → NOT NULL + default)
            migrationBuilder.Sql("""
                ALTER TABLE [msosync].[sync_batch_error] ADD [create_time] datetime2(7) NULL;
                """);
            migrationBuilder.Sql("""
                UPDATE [msosync].[sync_batch_error] SET [create_time] = SYSUTCDATETIME() WHERE [create_time] IS NULL;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE [msosync].[sync_batch_error] ALTER COLUMN [create_time] datetime2(7) NOT NULL;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE [msosync].[sync_batch_error]
                    ADD CONSTRAINT [DF_sync_batch_error_create_time] DEFAULT SYSUTCDATETIME() FOR [create_time];
                """);

            // Indexes on sync_data_event (IX_sync_data_event_create_time already exists — skip)
            migrationBuilder.CreateIndex(
                name:   "IX_sync_data_event_source_node_id",
                schema: "msosync",
                table:  "sync_data_event",
                column: "source_node_id");

            migrationBuilder.CreateIndex(
                name:   "IX_sync_data_event_trigger_id",
                schema: "msosync",
                table:  "sync_data_event",
                column: "trigger_id");

            migrationBuilder.Sql("""
                CREATE INDEX [IX_sync_data_event_channel_time]
                    ON [msosync].[sync_data_event] ([channel_id] ASC, [create_time] DESC);
                """);

            // Indexes on sync_incoming_batch
            migrationBuilder.Sql("""
                CREATE INDEX [IX_sync_incoming_batch_received_time]
                    ON [msosync].[sync_incoming_batch] ([received_time] DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX [IX_sync_incoming_batch_source_node_time]
                    ON [msosync].[sync_incoming_batch] ([source_node_id] ASC, [received_time] DESC);
                """);

            migrationBuilder.Sql("""
                CREATE INDEX [IX_sync_incoming_batch_status_time]
                    ON [msosync].[sync_incoming_batch] ([status] ASC, [received_time] DESC);
                """);

            // Index on sync_batch_error
            // IX_sync_batch_error_batch_id already exists from M005 — skip
            migrationBuilder.Sql("""
                CREATE INDEX [IX_sync_batch_error_conflict_create]
                    ON [msosync].[sync_batch_error] ([conflict_type] ASC, [create_time] DESC);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_batch_error_conflict_create] ON [msosync].[sync_batch_error];");
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_incoming_batch_status_time] ON [msosync].[sync_incoming_batch];");
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_incoming_batch_source_node_time] ON [msosync].[sync_incoming_batch];");
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_incoming_batch_received_time] ON [msosync].[sync_incoming_batch];");
            migrationBuilder.Sql("DROP INDEX IF EXISTS [IX_sync_data_event_channel_time] ON [msosync].[sync_data_event];");
            migrationBuilder.DropIndex(name: "IX_sync_data_event_trigger_id",    schema: "msosync", table: "sync_data_event");
            migrationBuilder.DropIndex(name: "IX_sync_data_event_source_node_id", schema: "msosync", table: "sync_data_event");
            migrationBuilder.Sql("ALTER TABLE [msosync].[sync_batch_error] DROP CONSTRAINT [DF_sync_batch_error_create_time];");
            migrationBuilder.DropColumn(name: "create_time", schema: "msosync", table: "sync_batch_error");
        }
    }
}
