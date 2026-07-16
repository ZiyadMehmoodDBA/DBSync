using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Common.Tenancy;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M031_CoreTopologyTenantId : Migration
{
    private const string Schema = "msosync";
    private static readonly string SystemTenantId = WellKnownTenantIds.SystemTenant.ToString();

    // The 12 core topology tables getting NOT NULL tenant_id.
    // NOTE: actual SQL table names differ from EF class names:
    //   sync_node_channel   ← SyncNodeChannelAssignment
    //   sync_node_trigger   ← SyncNodeTriggerAssignment
    //   sync_node_router    ← SyncNodeRouterAssignment
    private static readonly (string table, string indexName)[] TopologyTables =
    [
        ("sync_node",          "IX_sync_node_tenant_id"),
        ("sync_node_group",    "IX_sync_node_group_tenant_id"),
        ("sync_node_security", "IX_sync_node_security_tenant_id"),
        ("sync_node_scope",    "IX_sync_node_scope_tenant_id"),
        ("sync_node_channel",  "IX_sync_node_channel_tenant_id"),
        ("sync_node_trigger",  "IX_sync_node_trigger_tenant_id"),
        ("sync_node_router",   "IX_sync_node_router_tenant_id"),
        ("sync_channel",       "IX_sync_channel_tenant_id"),
        ("sync_trigger",       "IX_sync_trigger_tenant_id"),
        ("sync_trigger_hist",  "IX_sync_trigger_hist_tenant_id"),
        ("sync_router",        "IX_sync_router_tenant_id"),
        ("sync_trigger_router","IX_sync_trigger_router_tenant_id"),
    ];

    // 3 simple hybrid tables — sync_parameter and sync_parameter_hist are handled separately (require surrogate PK swap first)
    private static readonly string[] SimpleHybridTables =
    [
        "sync_role",
        "sync_user_role",
        "sync_user_preference",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ── 1. Add NOT NULL tenant_id to 12 topology tables (backfill to SystemTenant) ──────

        foreach (var (table, indexName) in TopologyTables)
        {
            migrationBuilder.AddColumn<Guid>(
                name:         "tenant_id",
                schema:       Schema,
                table:        table,
                type:         "uniqueidentifier",
                nullable:     false,
                defaultValue: WellKnownTenantIds.SystemTenant);

            // Backfill any rows that got 00000000-0000-0000-0000-000000000000 (empty Guid default)
            migrationBuilder.Sql(
                $"UPDATE [{Schema}].[{table}] SET [tenant_id] = '{SystemTenantId}' WHERE [tenant_id] = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name:   indexName,
                schema: Schema,
                table:  table,
                column: "tenant_id");
        }

        // ── 2. FK on sync_node → tenant ────────────────────────────────────────────────────

        migrationBuilder.AddForeignKey(
            name:            "FK_sync_node_tenant_id",
            schema:          Schema,
            table:           "sync_node",
            column:          "tenant_id",
            principalSchema: Schema,
            principalTable:  "tenant",
            principalColumn: "tenant_id",
            onDelete:        ReferentialAction.Restrict);

        // ── 3. Add nullable tenant_id to simple hybrid tables ──────────────────────────────

        foreach (var table in SimpleHybridTables)
        {
            migrationBuilder.AddColumn<Guid>(
                name:         "tenant_id",
                schema:       Schema,
                table:        table,
                type:         "uniqueidentifier",
                nullable:     true,
                defaultValue: null);
        }

        // ── 4. sync_parameter: surrogate PK (id) + nullable tenant_id ─────────────────────
        //    Original: PK on parameter_name (varchar)
        //    New: PK on id (bigint identity), unique index on (parameter_name, tenant_id)

        // 4a. Drop dependent objects on sync_parameter_hist first (none that reference PK)
        // 4b. Add id column to sync_parameter (not yet the PK)
        migrationBuilder.Sql($"""
            ALTER TABLE [{Schema}].[sync_parameter]
                ADD [id] bigint IDENTITY(1,1) NOT NULL;
            """);

        // 4c. Drop old PK constraint on parameter_name
        migrationBuilder.Sql($"""
            ALTER TABLE [{Schema}].[sync_parameter]
                DROP CONSTRAINT [PK_sync_parameter];
            """);

        // 4d. Add new PK on id
        migrationBuilder.Sql($"""
            ALTER TABLE [{Schema}].[sync_parameter]
                ADD CONSTRAINT [PK_sync_parameter] PRIMARY KEY ([id]);
            """);

        // 4e. Add nullable tenant_id to sync_parameter
        migrationBuilder.AddColumn<Guid>(
            name:         "tenant_id",
            schema:       Schema,
            table:        "sync_parameter",
            type:         "uniqueidentifier",
            nullable:     true,
            defaultValue: null);

        // 4f. Unique index on (parameter_name, tenant_id)
        migrationBuilder.CreateIndex(
            name:    "UX_sync_parameter_name_tenant",
            schema:  Schema,
            table:   "sync_parameter",
            columns: ["parameter_name", "tenant_id"],
            unique:  true);

        // ── 5. sync_parameter_hist: add nullable tenant_id ─────────────────────────────────

        migrationBuilder.AddColumn<Guid>(
            name:         "tenant_id",
            schema:       Schema,
            table:        "sync_parameter_hist",
            type:         "uniqueidentifier",
            nullable:     true,
            defaultValue: null);

        // ── 6. Rename sync_monitor → sync_monitor_snapshot ─────────────────────────────────

        migrationBuilder.RenameTable(
            name:      "sync_monitor",
            schema:    Schema,
            newName:   "sync_monitor_snapshot",
            newSchema: Schema);

        // ── 7. Add lock_scope to sync_lock (0 = Platform, 1 = Tenant) ─────────────────────

        migrationBuilder.AddColumn<int>(
            name:         "lock_scope",
            schema:       Schema,
            table:        "sync_lock",
            type:         "int",
            nullable:     false,
            defaultValue: 0);   // 0 = LockScope.Platform
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Reverse order

        // 7. Remove lock_scope
        migrationBuilder.DropColumn(name: "lock_scope", schema: Schema, table: "sync_lock");

        // 6. Rename back
        migrationBuilder.RenameTable(
            name:      "sync_monitor_snapshot",
            schema:    Schema,
            newName:   "sync_monitor",
            newSchema: Schema);

        // 5. Remove tenant_id from sync_parameter_hist
        migrationBuilder.DropColumn(name: "tenant_id", schema: Schema, table: "sync_parameter_hist");

        // 4. Revert sync_parameter surrogate PK
        migrationBuilder.DropIndex(name: "UX_sync_parameter_name_tenant", schema: Schema, table: "sync_parameter");
        migrationBuilder.DropColumn(name: "tenant_id", schema: Schema, table: "sync_parameter");

        // Restore original PK on parameter_name
        migrationBuilder.Sql($"""
            ALTER TABLE [{Schema}].[sync_parameter]
                DROP CONSTRAINT [PK_sync_parameter];
            """);

        migrationBuilder.Sql($"""
            ALTER TABLE [{Schema}].[sync_parameter]
                ADD CONSTRAINT [PK_sync_parameter] PRIMARY KEY ([parameter_name]);
            """);

        migrationBuilder.DropColumn(name: "id", schema: Schema, table: "sync_parameter");

        // 3. Remove nullable tenant_id from simple hybrid tables
        foreach (var table in SimpleHybridTables)
            migrationBuilder.DropColumn(name: "tenant_id", schema: Schema, table: table);

        // 2. Drop FK
        migrationBuilder.DropForeignKey(
            name: "FK_sync_node_tenant_id", schema: Schema, table: "sync_node");

        // 1. Remove indexes and tenant_id from topology tables
        foreach (var (table, indexName) in TopologyTables)
        {
            migrationBuilder.DropIndex(name: indexName, schema: Schema, table: table);
            migrationBuilder.DropColumn(name: "tenant_id", schema: Schema, table: table);
        }
    }
}
