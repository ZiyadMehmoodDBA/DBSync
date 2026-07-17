using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Common.Tenancy;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M032_DomainTenantIdMigration : Migration
{
    private const string Schema    = "msosync";
    private const string DboSchema = "dbo";
    private static readonly string SystemTenantId = WellKnownTenantIds.SystemTenant.ToString();

    // 20 tables in msosync schema
    private static readonly (string table, string indexName, string[] indexColumns)[] MsoSyncTables =
    [
        ("sync_registration_request",           "IX_sync_registration_request_TenantId_Status",               ["tenant_id", "registration_status"]),
        ("sync_node_bootstrap_token",            "IX_sync_node_bootstrap_token_TenantId_NodeId",               ["tenant_id", "node_id"]),
        ("sync_node_lifecycle_history",          "IX_sync_node_lifecycle_history_TenantId_NodeId",             ["tenant_id", "node_id"]),
        ("sync_node_connectivity_history",       "IX_sync_node_connectivity_history_TenantId_NodeId",          ["tenant_id", "node_id"]),
        ("sync_data_event",                      "IX_sync_data_event_TenantId_CreateTime",                     ["tenant_id", "create_time"]),
        ("sync_data_event_batch",                "IX_sync_data_event_batch_TenantId_BatchId",                  ["tenant_id", "batch_id"]),
        ("sync_outgoing_batch",                  "IX_sync_outgoing_batch_TenantId_Status",                     ["tenant_id", "status"]),
        ("sync_incoming_batch",                  "IX_sync_incoming_batch_TenantId_Status",                     ["tenant_id", "status"]),
        ("sync_batch_error",                     "IX_sync_batch_error_TenantId_CreateTime",                    ["tenant_id", "create_time"]),
        ("sync_configuration_template",          "IX_sync_configuration_template_TenantId",                    ["tenant_id"]),
        ("sync_configuration_template_version",  "IX_sync_configuration_template_version_TenantId_TemplateId", ["tenant_id", "template_id"]),
        ("sync_node_configuration_override",     "IX_sync_node_configuration_override_TenantId_NodeId",        ["tenant_id", "node_id"]),
        ("sync_node_configuration_history",      "IX_sync_node_configuration_history_TenantId_NodeId",         ["tenant_id", "node_id"]),
        ("sync_configuration_rollout",           "IX_sync_configuration_rollout_TenantId_Status",              ["tenant_id", "status"]),
        ("sync_runtime_stats",                   "IX_sync_runtime_stats_TenantId_CreateTime",                  ["tenant_id", "create_time"]),
        ("sync_audit",                           "IX_sync_audit_TenantId_CreateTime",                          ["tenant_id", "create_time"]),
        ("sync_operation",                       "IX_sync_operation_TenantId_Status",                          ["tenant_id", "status"]),
        // NOTE: sync_notification uses created_at (not create_time) — confirmed in M028 + SyncNotificationConfiguration
        ("sync_notification",                    "IX_sync_notification_TenantId_CreatedAt",                    ["tenant_id", "created_at"]),
        ("sync_user_notification",               "IX_sync_user_notification_TenantId_UserId",                  ["tenant_id", "user_id"]),
        ("sync_user_refresh_token",              "IX_sync_user_refresh_token_TenantId_UserId",                 ["tenant_id", "user_id"]),
    ];

    // 1 table in dbo schema
    private static readonly (string table, string indexName, string[] indexColumns)[] DboTables =
    [
        ("sync_export_job", "IX_sync_export_job_TenantId_Status", ["tenant_id", "status"]),
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── msosync schema tables ──────────────────────────────────────────────
        foreach (var (table, indexName, indexColumns) in MsoSyncTables)
        {
            // 1. Add nullable column
            migrationBuilder.AddColumn<Guid>(
                name:     "tenant_id",
                schema:   Schema,
                table:    table,
                type:     "uniqueidentifier",
                nullable: true);

            // 2. Backfill
            migrationBuilder.Sql(
                $"UPDATE [{Schema}].[{table}] SET [tenant_id] = '{SystemTenantId}' WHERE [tenant_id] IS NULL;");

            // 3. Make NOT NULL
            migrationBuilder.AlterColumn<Guid>(
                name:        "tenant_id",
                schema:      Schema,
                table:       table,
                type:        "uniqueidentifier",
                nullable:    false,
                oldClrType:  typeof(Guid),
                oldType:     "uniqueidentifier",
                oldNullable: true);

            // 4. Create composite index
            migrationBuilder.CreateIndex(
                name:    indexName,
                schema:  Schema,
                table:   table,
                columns: indexColumns);

            // 5. Add FK
            migrationBuilder.AddForeignKey(
                name:            $"FK_{table}_tenant_id",
                schema:          Schema,
                table:           table,
                column:          "tenant_id",
                principalSchema: Schema,
                principalTable:  "tenant",
                principalColumn: "tenant_id",
                onDelete:        ReferentialAction.Restrict);
        }

        // ── SyncConfigurationTemplate: convert global unique Name → composite unique (TenantId, Name) ──
        migrationBuilder.DropIndex(
            name:   "UX_sync_configuration_template_name",
            schema: Schema,
            table:  "sync_configuration_template");

        migrationBuilder.CreateIndex(
            name:    "UX_sync_configuration_template_tenant_id_name",
            schema:  Schema,
            table:   "sync_configuration_template",
            columns: ["tenant_id", "name"],
            unique:  true);

        // ── dbo schema tables ──────────────────────────────────────────────────
        foreach (var (table, indexName, indexColumns) in DboTables)
        {
            migrationBuilder.AddColumn<Guid>(
                name:     "tenant_id",
                table:    table,
                type:     "uniqueidentifier",
                nullable: true);

            migrationBuilder.Sql(
                $"UPDATE [dbo].[{table}] SET [tenant_id] = '{SystemTenantId}' WHERE [tenant_id] IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name:        "tenant_id",
                table:       table,
                type:        "uniqueidentifier",
                nullable:    false,
                oldClrType:  typeof(Guid),
                oldType:     "uniqueidentifier",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name:    indexName,
                table:   table,
                columns: indexColumns);

            // dbo.sync_export_job gets FK pointing to msosync.tenant
            migrationBuilder.AddForeignKey(
                name:            $"FK_{table}_tenant_id",
                table:           table,
                column:          "tenant_id",
                principalSchema: Schema,
                principalTable:  "tenant",
                principalColumn: "tenant_id",
                onDelete:        ReferentialAction.Restrict);
        }
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // dbo tables
        foreach (var (table, indexName, _) in DboTables)
        {
            migrationBuilder.DropForeignKey(name: $"FK_{table}_tenant_id", table: table);
            migrationBuilder.DropIndex(name: indexName, table: table);
            migrationBuilder.DropColumn(name: "tenant_id", table: table);
        }

        // Restore SyncConfigurationTemplate unique index
        migrationBuilder.DropIndex(
            name:   "UX_sync_configuration_template_tenant_id_name",
            schema: Schema,
            table:  "sync_configuration_template");

        migrationBuilder.CreateIndex(
            name:   "UX_sync_configuration_template_name",
            schema: Schema,
            table:  "sync_configuration_template",
            column: "name",
            unique: true);

        // msosync tables (reverse order)
        foreach (var (table, indexName, _) in MsoSyncTables.Reverse())
        {
            migrationBuilder.DropForeignKey(name: $"FK_{table}_tenant_id", schema: Schema, table: table);
            migrationBuilder.DropIndex(name: indexName, schema: Schema, table: table);
            migrationBuilder.DropColumn(name: "tenant_id", schema: Schema, table: table);
        }
    }
}
