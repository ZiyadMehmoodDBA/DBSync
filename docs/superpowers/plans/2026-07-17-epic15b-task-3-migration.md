# Task 3: M032 Migration — tenant_id on 21 Tables

**Part of:** [Epic 15B Domain Tenant Migration](2026-07-17-epic15b-domain-tenant-migration.md)

**Goal:** Write M032 migration class + `.superpowers/apply-m032.sql` script. Apply the script to the real DB. Every existing row gets `tenant_id = SystemTenant` (00000000-0000-0000-0000-000000000001). Every table gets a composite index and FK to the `tenant` table.

**Files:**
- Create: `src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.cs`
- Create: `src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.Designer.cs`
- Modify: `src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs` (via EF scaffold)
- Create: `.superpowers/apply-m032.sql`

**Interfaces:**
- Consumes: 21 entities from Task 1 + EF configs from Task 2; `WellKnownTenantIds.SystemTenant` from `MSOSync.Common/Tenancy/WellKnownTenantIds.cs`
- Produces: DB with `tenant_id NOT NULL` on all 21 tables, composite indexes, FKs; `__EFMigrationsHistory` row for M032

---

## Migration Contract (Per Table)

```
ADD tenant_id NULL
    ↓
UPDATE SET tenant_id = SystemTenant WHERE tenant_id IS NULL
    ↓
(verify zero NULLs in SQL script comment)
    ↓
ALTER COLUMN tenant_id NOT NULL
    ↓
CREATE NONCLUSTERED INDEX (composite)
    ↓
ALTER TABLE ADD CONSTRAINT FK → [msosync].[tenant](tenant_id)
```

**SystemTenant GUID:** `00000000-0000-0000-0000-000000000001`

---

- [ ] **Step 1: Generate EF scaffold to update AppDbContextModelSnapshot**

Run scaffold (generates boilerplate Up/Down and updates the snapshot):
```
dotnet ef migrations add M032_DomainTenantIdMigration --project src/MSOSync.Persistence --startup-project src/MSOSync.App -- --environment Development
```

This will create:
- `src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.cs` (auto-generated)
- `src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.Designer.cs` (auto-generated)
- Updates `AppDbContextModelSnapshot.cs` (keep EF's changes here)

**Do NOT apply this migration via `dotnet ef database update`** — we apply manually via SQL script in Step 4.

- [ ] **Step 2: Replace the auto-generated Up/Down with the hand-written migration**

Open `src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.cs` and replace the entire file content with:

```csharp
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
        ("sync_registration_request",           "IX_sync_registration_request_TenantId_Status",             ["tenant_id", "registration_status"]),
        ("sync_node_bootstrap_token",            "IX_sync_node_bootstrap_token_TenantId_NodeId",             ["tenant_id", "node_id"]),
        ("sync_node_lifecycle_history",          "IX_sync_node_lifecycle_history_TenantId_NodeId",           ["tenant_id", "node_id"]),
        ("sync_node_connectivity_history",       "IX_sync_node_connectivity_history_TenantId_NodeId",        ["tenant_id", "node_id"]),
        ("sync_data_event",                      "IX_sync_data_event_TenantId_CreateTime",                   ["tenant_id", "create_time"]),
        ("sync_data_event_batch",                "IX_sync_data_event_batch_TenantId_BatchId",                ["tenant_id", "batch_id"]),
        ("sync_outgoing_batch",                  "IX_sync_outgoing_batch_TenantId_Status",                   ["tenant_id", "status"]),
        ("sync_incoming_batch",                  "IX_sync_incoming_batch_TenantId_Status",                   ["tenant_id", "status"]),
        ("sync_batch_error",                     "IX_sync_batch_error_TenantId_CreateTime",                  ["tenant_id", "create_time"]),
        ("sync_configuration_template",          "IX_sync_configuration_template_TenantId",                  ["tenant_id"]),
        ("sync_configuration_template_version",  "IX_sync_configuration_template_version_TenantId_TemplateId", ["tenant_id", "template_id"]),
        ("sync_node_configuration_override",     "IX_sync_node_configuration_override_TenantId_NodeId",      ["tenant_id", "node_id"]),
        ("sync_node_configuration_history",      "IX_sync_node_configuration_history_TenantId_NodeId",       ["tenant_id", "node_id"]),
        ("sync_configuration_rollout",           "IX_sync_configuration_rollout_TenantId_Status",            ["tenant_id", "status"]),
        ("sync_runtime_stats",                   "IX_sync_runtime_stats_TenantId_CreateTime",                ["tenant_id", "create_time"]),
        ("sync_audit",                           "IX_sync_audit_TenantId_CreateTime",                        ["tenant_id", "create_time"]),
        ("sync_operation",                       "IX_sync_operation_TenantId_Status",                        ["tenant_id", "status"]),
        ("sync_notification",                    "IX_sync_notification_TenantId_CreateTime",                 ["tenant_id", "create_time"]),
        ("sync_user_notification",               "IX_sync_user_notification_TenantId_UserId",                ["tenant_id", "user_id"]),
        ("sync_user_refresh_token",              "IX_sync_user_refresh_token_TenantId_UserId",               ["tenant_id", "user_id"]),
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
                name:     "tenant_id",
                schema:   Schema,
                table:    table,
                type:     "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType:    "uniqueidentifier",
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
                name:     "tenant_id",
                table:    table,
                type:     "uniqueidentifier",
                nullable: false,
                oldClrType: typeof(Guid),
                oldType:    "uniqueidentifier",
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
```

> **Note on `indexColumns` for single-column index:** `sync_configuration_template` uses `["tenant_id"]` — a single-element array. `migrationBuilder.CreateIndex` accepts both `column:` (single string) and `columns:` (array). Using `columns:` with a single element works correctly.

> **Note on `AlterColumn`:** The `oldClrType`, `oldType`, `oldNullable` parameters are required by EF Core's migration builder to track the before/after state. Fill them with the nullable Guid type (the state before this migration).

- [ ] **Step 3: Verify the Designer.cs companion file exists**

The `dotnet ef migrations add` command from Step 1 should have created `M032_DomainTenantIdMigration.Designer.cs` automatically. Open it and verify it contains the `[DbContext(typeof(AppDbContext))]` and `[Migration("M032_DomainTenantIdMigration")]` attributes. If the file is missing or malformed, create it manually:

```csharp
// <auto-generated />
using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;

#nullable disable

namespace MSOSync.Persistence.Migrations
{
    [DbContext(typeof(AppDbContext))]
    [Migration("M032_DomainTenantIdMigration")]
    partial class M032_DomainTenantIdMigration
    {
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
            // EF uses AppDbContextModelSnapshot.cs at runtime; this stub satisfies the migration runner.
        }
    }
}
```

- [ ] **Step 4: Write the SQL application script**

Create `.superpowers/apply-m032.sql`:

```sql
-- ============================================================
-- M032: Domain TenantId Migration
-- 21 tables: NULL → backfill → NOT NULL → composite index → FK
-- SystemTenant: 00000000-0000-0000-0000-000000000001
-- ============================================================

DECLARE @SystemTenant UNIQUEIDENTIFIER = '00000000-0000-0000-0000-000000000001';

-- ─────────────────────────────────────────────────────────────
-- GROUP 1: Node Management (msosync)
-- ─────────────────────────────────────────────────────────────

-- sync_registration_request
ALTER TABLE [msosync].[sync_registration_request] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_registration_request] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
-- Verify: SELECT COUNT(*) FROM [msosync].[sync_registration_request] WHERE [tenant_id] IS NULL; -- Expected: 0
ALTER TABLE [msosync].[sync_registration_request] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_registration_request_TenantId_Status]
    ON [msosync].[sync_registration_request] ([tenant_id] ASC, [registration_status] ASC);
ALTER TABLE [msosync].[sync_registration_request]
    ADD CONSTRAINT [FK_sync_registration_request_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_bootstrap_token
ALTER TABLE [msosync].[sync_node_bootstrap_token] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_node_bootstrap_token] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_node_bootstrap_token] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_node_bootstrap_token_TenantId_NodeId]
    ON [msosync].[sync_node_bootstrap_token] ([tenant_id] ASC, [node_id] ASC);
ALTER TABLE [msosync].[sync_node_bootstrap_token]
    ADD CONSTRAINT [FK_sync_node_bootstrap_token_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_lifecycle_history
ALTER TABLE [msosync].[sync_node_lifecycle_history] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_node_lifecycle_history] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_node_lifecycle_history] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_node_lifecycle_history_TenantId_NodeId]
    ON [msosync].[sync_node_lifecycle_history] ([tenant_id] ASC, [node_id] ASC);
ALTER TABLE [msosync].[sync_node_lifecycle_history]
    ADD CONSTRAINT [FK_sync_node_lifecycle_history_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_connectivity_history
ALTER TABLE [msosync].[sync_node_connectivity_history] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_node_connectivity_history] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_node_connectivity_history] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_node_connectivity_history_TenantId_NodeId]
    ON [msosync].[sync_node_connectivity_history] ([tenant_id] ASC, [node_id] ASC);
ALTER TABLE [msosync].[sync_node_connectivity_history]
    ADD CONSTRAINT [FK_sync_node_connectivity_history_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ─────────────────────────────────────────────────────────────
-- GROUP 2: Synchronization Engine (msosync)
-- ─────────────────────────────────────────────────────────────

-- sync_data_event
ALTER TABLE [msosync].[sync_data_event] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_data_event] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_data_event] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_data_event_TenantId_CreateTime]
    ON [msosync].[sync_data_event] ([tenant_id] ASC, [create_time] DESC);
ALTER TABLE [msosync].[sync_data_event]
    ADD CONSTRAINT [FK_sync_data_event_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_data_event_batch
ALTER TABLE [msosync].[sync_data_event_batch] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_data_event_batch] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_data_event_batch] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_data_event_batch_TenantId_BatchId]
    ON [msosync].[sync_data_event_batch] ([tenant_id] ASC, [batch_id] ASC);
ALTER TABLE [msosync].[sync_data_event_batch]
    ADD CONSTRAINT [FK_sync_data_event_batch_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_outgoing_batch
ALTER TABLE [msosync].[sync_outgoing_batch] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_outgoing_batch] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_outgoing_batch] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_outgoing_batch_TenantId_Status]
    ON [msosync].[sync_outgoing_batch] ([tenant_id] ASC, [status] ASC);
ALTER TABLE [msosync].[sync_outgoing_batch]
    ADD CONSTRAINT [FK_sync_outgoing_batch_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_incoming_batch
ALTER TABLE [msosync].[sync_incoming_batch] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_incoming_batch] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_incoming_batch] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_incoming_batch_TenantId_Status]
    ON [msosync].[sync_incoming_batch] ([tenant_id] ASC, [status] ASC);
ALTER TABLE [msosync].[sync_incoming_batch]
    ADD CONSTRAINT [FK_sync_incoming_batch_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_batch_error
ALTER TABLE [msosync].[sync_batch_error] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_batch_error] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_batch_error] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_batch_error_TenantId_CreateTime]
    ON [msosync].[sync_batch_error] ([tenant_id] ASC, [create_time] DESC);
ALTER TABLE [msosync].[sync_batch_error]
    ADD CONSTRAINT [FK_sync_batch_error_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ─────────────────────────────────────────────────────────────
-- GROUP 3: Configuration Management (msosync)
-- ─────────────────────────────────────────────────────────────

-- sync_configuration_template (+ unique constraint migration)
ALTER TABLE [msosync].[sync_configuration_template] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_configuration_template] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_configuration_template] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
-- Convert global unique name → per-tenant unique (tenant_id, name)
DROP INDEX [UX_sync_configuration_template_name] ON [msosync].[sync_configuration_template];
CREATE UNIQUE NONCLUSTERED INDEX [UX_sync_configuration_template_tenant_id_name]
    ON [msosync].[sync_configuration_template] ([tenant_id] ASC, [name] ASC);
CREATE NONCLUSTERED INDEX [IX_sync_configuration_template_TenantId]
    ON [msosync].[sync_configuration_template] ([tenant_id] ASC);
ALTER TABLE [msosync].[sync_configuration_template]
    ADD CONSTRAINT [FK_sync_configuration_template_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_configuration_template_version
ALTER TABLE [msosync].[sync_configuration_template_version] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_configuration_template_version] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_configuration_template_version] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_configuration_template_version_TenantId_TemplateId]
    ON [msosync].[sync_configuration_template_version] ([tenant_id] ASC, [template_id] ASC);
ALTER TABLE [msosync].[sync_configuration_template_version]
    ADD CONSTRAINT [FK_sync_configuration_template_version_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_configuration_override
ALTER TABLE [msosync].[sync_node_configuration_override] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_node_configuration_override] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_node_configuration_override] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_node_configuration_override_TenantId_NodeId]
    ON [msosync].[sync_node_configuration_override] ([tenant_id] ASC, [node_id] ASC);
ALTER TABLE [msosync].[sync_node_configuration_override]
    ADD CONSTRAINT [FK_sync_node_configuration_override_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_node_configuration_history
ALTER TABLE [msosync].[sync_node_configuration_history] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_node_configuration_history] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_node_configuration_history] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_node_configuration_history_TenantId_NodeId]
    ON [msosync].[sync_node_configuration_history] ([tenant_id] ASC, [node_id] ASC);
ALTER TABLE [msosync].[sync_node_configuration_history]
    ADD CONSTRAINT [FK_sync_node_configuration_history_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_configuration_rollout
ALTER TABLE [msosync].[sync_configuration_rollout] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_configuration_rollout] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_configuration_rollout] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_configuration_rollout_TenantId_Status]
    ON [msosync].[sync_configuration_rollout] ([tenant_id] ASC, [status] ASC);
ALTER TABLE [msosync].[sync_configuration_rollout]
    ADD CONSTRAINT [FK_sync_configuration_rollout_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ─────────────────────────────────────────────────────────────
-- GROUP 4: Operations & Audit (msosync)
-- ─────────────────────────────────────────────────────────────

-- sync_runtime_stats
ALTER TABLE [msosync].[sync_runtime_stats] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_runtime_stats] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_runtime_stats] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_runtime_stats_TenantId_CreateTime]
    ON [msosync].[sync_runtime_stats] ([tenant_id] ASC, [create_time] DESC);
ALTER TABLE [msosync].[sync_runtime_stats]
    ADD CONSTRAINT [FK_sync_runtime_stats_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_audit
ALTER TABLE [msosync].[sync_audit] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_audit] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_audit] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_audit_TenantId_CreateTime]
    ON [msosync].[sync_audit] ([tenant_id] ASC, [create_time] DESC);
ALTER TABLE [msosync].[sync_audit]
    ADD CONSTRAINT [FK_sync_audit_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_operation
ALTER TABLE [msosync].[sync_operation] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_operation] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_operation] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_operation_TenantId_Status]
    ON [msosync].[sync_operation] ([tenant_id] ASC, [status] ASC);
ALTER TABLE [msosync].[sync_operation]
    ADD CONSTRAINT [FK_sync_operation_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ─────────────────────────────────────────────────────────────
-- GROUP 5: User & Runtime (msosync)
-- ─────────────────────────────────────────────────────────────

-- sync_notification
ALTER TABLE [msosync].[sync_notification] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_notification] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_notification] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_notification_TenantId_CreateTime]
    ON [msosync].[sync_notification] ([tenant_id] ASC, [create_time] DESC);
ALTER TABLE [msosync].[sync_notification]
    ADD CONSTRAINT [FK_sync_notification_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_user_notification
ALTER TABLE [msosync].[sync_user_notification] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_user_notification] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_user_notification] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_user_notification_TenantId_UserId]
    ON [msosync].[sync_user_notification] ([tenant_id] ASC, [user_id] ASC);
ALTER TABLE [msosync].[sync_user_notification]
    ADD CONSTRAINT [FK_sync_user_notification_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- sync_user_refresh_token
ALTER TABLE [msosync].[sync_user_refresh_token] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [msosync].[sync_user_refresh_token] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [msosync].[sync_user_refresh_token] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_user_refresh_token_TenantId_UserId]
    ON [msosync].[sync_user_refresh_token] ([tenant_id] ASC, [user_id] ASC);
ALTER TABLE [msosync].[sync_user_refresh_token]
    ADD CONSTRAINT [FK_sync_user_refresh_token_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ─────────────────────────────────────────────────────────────
-- dbo schema tables
-- ─────────────────────────────────────────────────────────────

-- sync_export_job (dbo schema)
ALTER TABLE [dbo].[sync_export_job] ADD [tenant_id] UNIQUEIDENTIFIER NULL;
UPDATE [dbo].[sync_export_job] SET [tenant_id] = @SystemTenant WHERE [tenant_id] IS NULL;
ALTER TABLE [dbo].[sync_export_job] ALTER COLUMN [tenant_id] UNIQUEIDENTIFIER NOT NULL;
CREATE NONCLUSTERED INDEX [IX_sync_export_job_TenantId_Status]
    ON [dbo].[sync_export_job] ([tenant_id] ASC, [status] ASC);
ALTER TABLE [dbo].[sync_export_job]
    ADD CONSTRAINT [FK_sync_export_job_tenant_id]
    FOREIGN KEY ([tenant_id]) REFERENCES [msosync].[tenant]([tenant_id]);
GO

-- ─────────────────────────────────────────────────────────────
-- Mark migration as applied in EF history
-- ─────────────────────────────────────────────────────────────

INSERT INTO [dbo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES ('M032_DomainTenantIdMigration', '9.0.0');
GO

PRINT 'M032_DomainTenantIdMigration applied successfully.';
```

> **Note on `sync_notification` timestamp column:** Verify that `SyncNotification` has a `create_time` column. If the column is named differently (e.g., `created_at`), update the `CREATE INDEX` for `sync_notification` above accordingly.

- [ ] **Step 5: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: `Build succeeded. 0 Error(s) 0 Warning(s)`

If `AlterColumn` gives "oldNullable" errors, check the EF Core version — in EF Core 9 the parameter is `oldNullable: true`.

- [ ] **Step 6: Apply the migration to the real database**

Open SQL Server Management Studio (SSMS) or sqlcmd and run `.superpowers/apply-m032.sql` against the development database.

```
sqlcmd -S localhost -d MSOSync -E -i .superpowers/apply-m032.sql
```

Expected: Script runs to completion with `PRINT 'M032_DomainTenantIdMigration applied successfully.'`

If a table does not exist (e.g., `sync_data_event_batch`), check the actual table name by running `SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA IN ('msosync','dbo') ORDER BY TABLE_NAME` before re-trying.

- [ ] **Step 7: Verify schema and backfill**

```sql
-- All 21 tables should have tenant_id NOT NULL
SELECT t.TABLE_SCHEMA, t.TABLE_NAME, c.IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS c
JOIN INFORMATION_SCHEMA.TABLES t ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
WHERE c.COLUMN_NAME = 'tenant_id'
ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME;
-- Expected: 21 rows, all IS_NULLABLE = 'NO'

-- Zero NULL rows in any table
SELECT 'sync_audit' AS tbl, COUNT(*) AS nulls FROM [msosync].[sync_audit] WHERE [tenant_id] IS NULL
UNION ALL SELECT 'sync_operation', COUNT(*) FROM [msosync].[sync_operation] WHERE [tenant_id] IS NULL
UNION ALL SELECT 'sync_export_job', COUNT(*) FROM [dbo].[sync_export_job] WHERE [tenant_id] IS NULL;
-- Expected: all 0

-- Migration recorded
SELECT * FROM [dbo].[__EFMigrationsHistory] WHERE [MigrationId] = 'M032_DomainTenantIdMigration';
-- Expected: 1 row
```

- [ ] **Step 8: Run all unit tests**

```
dotnet test MSOSync.sln --filter "Category!=Integration" -v minimal
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```
git add src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.cs
git add src/MSOSync.Persistence/Migrations/M032_DomainTenantIdMigration.Designer.cs
git add src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs
git add .superpowers/apply-m032.sql
git commit -m "feat(15B-3): M032 tenant_id on 21 tables — composite indexes, FKs, template unique constraint migration"
```
