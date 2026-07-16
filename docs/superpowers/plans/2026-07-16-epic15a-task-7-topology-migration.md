# Task 7: M031 — Core Topology TenantId Migration

**Part of:** [Epic 15A Multi-Tenancy](2026-07-16-epic15a-multi-tenancy.md)

**Goal:** Add `tenant_id NOT NULL` (defaulting to SystemTenant) to 12 core topology tables, add nullable `tenant_id` to 5 hybrid entity tables, rename `sync_monitor` → `sync_monitor_snapshot`, add `lock_scope` to `sync_lock`. Update all EF configurations. Backfill all existing rows to SystemTenant.

**Files:**
- Create: `src/MSOSync.Persistence/Migrations/M031_CoreTopologyTenantId.cs`
- Modify EF configs: `SyncNodeConfiguration.cs`, `SyncNodeGroupConfiguration.cs`, `SyncNodeSecurityConfiguration.cs`, `SyncNodeScopeConfiguration.cs`, `SyncNodeChannelAssignmentConfiguration.cs`, `SyncNodeTriggerAssignmentConfiguration.cs`, `SyncNodeRouterAssignmentConfiguration.cs`, `SyncChannelConfiguration.cs`, `SyncTriggerConfiguration.cs`, `SyncTriggerHistConfiguration.cs`, `SyncRouterConfiguration.cs`, `SyncTriggerRouterConfiguration.cs`
- Modify EF configs (hybrid): `SyncRoleConfiguration.cs`, `SyncUserRoleConfiguration.cs`, `SyncParameterConfiguration.cs`, `SyncParameterHistConfiguration.cs`, `SyncUserPreferenceConfiguration.cs`
- Modify entity: `SyncLock.cs` — add `LockScope` enum + property
- Modify EF config: `SyncLockConfiguration.cs` — add `lock_scope` column
- Rename: `SyncMonitorConfiguration.cs` → update table name to `sync_monitor_snapshot`

**Interfaces:**
- Consumes: `ITenantScoped` on 12 topology entities (Task 1), `WellKnownTenantIds.SystemTenant` (Task 1), `IHybridEntity` on 5 hybrid entities (Task 1), `ICurrentTenantAccessor` + `ApplyTenantFilters` (Task 6)
- Produces: tenant-isolated topology tables, backfilled rows, active EF global query filters on all 12 topology entities

---

- [ ] **Step 1: Add LockScope to SyncLock entity**

Open `src/MSOSync.Persistence/Entities/SyncLock.cs`. Add the enum and property:
```csharp
using MSOSync.Common.Tenancy;

public enum LockScope { Platform, Tenant }

[GlobalEntity]
public class SyncLock
{
    // ... existing properties ...
    public LockScope Scope { get; set; } = LockScope.Platform;
}
```

- [ ] **Step 2: Update EF configurations for the 12 topology entities**

For each topology entity's configuration class, add the `tenant_id` column configuration inside `Configure()`. Pattern shown for `SyncNodeConfiguration.cs` — apply identically to all 12:

Open `src/MSOSync.Persistence/Configurations/SyncNodeConfiguration.cs` and add inside `Configure()`:
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired();

builder.HasIndex(e => e.TenantId)
    .HasDatabaseName("IX_sync_node_tenant_id");
```

Apply the same two-block addition (Property + HasIndex with matching names) to:
- `SyncNodeGroupConfiguration.cs` → index name `IX_sync_node_group_tenant_id`
- `SyncNodeSecurityConfiguration.cs` → index name `IX_sync_node_security_tenant_id`
- `SyncNodeScopeConfiguration.cs` → index name `IX_sync_node_scope_tenant_id`
- `SyncNodeChannelAssignmentConfiguration.cs` → index name `IX_sync_node_channel_assignment_tenant_id`
- `SyncNodeTriggerAssignmentConfiguration.cs` → index name `IX_sync_node_trigger_assignment_tenant_id`
- `SyncNodeRouterAssignmentConfiguration.cs` → index name `IX_sync_node_router_assignment_tenant_id`
- `SyncChannelConfiguration.cs` → index name `IX_sync_channel_tenant_id`
- `SyncTriggerConfiguration.cs` → index name `IX_sync_trigger_tenant_id`
- `SyncTriggerHistConfiguration.cs` → index name `IX_sync_trigger_hist_tenant_id`
- `SyncRouterConfiguration.cs` → index name `IX_sync_router_tenant_id`
- `SyncTriggerRouterConfiguration.cs` → index name `IX_sync_trigger_router_tenant_id`

- [ ] **Step 3: Update EF configurations for the 5 hybrid entities**

For each hybrid entity's configuration, add the nullable `tenant_id` column. Pattern for `SyncRoleConfiguration.cs`:

Open `src/MSOSync.Persistence/Configurations/SyncRoleConfiguration.cs` and add inside `Configure()`:
```csharp
builder.Property(e => e.TenantId)
    .HasColumnName("tenant_id")
    .HasColumnType("uniqueidentifier")
    .IsRequired(false);   // NULL = system/platform role
```

Apply the same single-block addition to:
- `SyncUserRoleConfiguration.cs` — `NULL = platform role assignment`
- `SyncParameterConfiguration.cs` — `NULL = platform setting`
- `SyncParameterHistConfiguration.cs` — `NULL = platform setting history`
- `SyncUserPreferenceConfiguration.cs` — `NULL = global preference`

- [ ] **Step 4: Update SyncMonitorConfiguration table name**

Open `src/MSOSync.Persistence/Configurations/SyncMonitorConfiguration.cs`.

Change the `ToTable` call from `"sync_monitor"` to `"sync_monitor_snapshot"`:
```csharp
builder.ToTable("sync_monitor_snapshot", Schema);
```

- [ ] **Step 5: Update SyncLockConfiguration**

Open `src/MSOSync.Persistence/Configurations/SyncLockConfiguration.cs` and add:
```csharp
builder.Property(e => e.Scope)
    .HasColumnName("lock_scope")
    .HasColumnType("int")
    .IsRequired()
    .HasDefaultValue(LockScope.Platform);
```

- [ ] **Step 6: Write migration M031**

Create `src/MSOSync.Persistence/Migrations/M031_CoreTopologyTenantId.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Common.Tenancy;

#nullable disable

namespace MSOSync.Persistence.Migrations;

public partial class M031_CoreTopologyTenantId : Migration
{
    private const string Schema        = "msosync";
    private static readonly string SystemTenantId = WellKnownTenantIds.SystemTenant.ToString();

    // The 12 core topology tables getting NOT NULL tenant_id
    private static readonly (string table, string indexName)[] TopologyTables =
    [
        ("sync_node",                      "IX_sync_node_tenant_id"),
        ("sync_node_group",                "IX_sync_node_group_tenant_id"),
        ("sync_node_security",             "IX_sync_node_security_tenant_id"),
        ("sync_node_scope",                "IX_sync_node_scope_tenant_id"),
        ("sync_node_channel_assignment",   "IX_sync_node_channel_assignment_tenant_id"),
        ("sync_node_trigger_assignment",   "IX_sync_node_trigger_assignment_tenant_id"),
        ("sync_node_router_assignment",    "IX_sync_node_router_assignment_tenant_id"),
        ("sync_channel",                   "IX_sync_channel_tenant_id"),
        ("sync_trigger",                   "IX_sync_trigger_tenant_id"),
        ("sync_trigger_hist",              "IX_sync_trigger_hist_tenant_id"),
        ("sync_router",                    "IX_sync_router_tenant_id"),
        ("sync_trigger_router",            "IX_sync_trigger_router_tenant_id"),
    ];

    // The 5 hybrid tables getting NULL-able tenant_id
    private static readonly string[] HybridTables =
    [
        "sync_role",
        "sync_user_role",
        "sync_parameter",
        "sync_parameter_hist",
        "sync_user_preference",
    ];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // 1. Add NOT NULL tenant_id to topology tables (default = SystemTenant for backfill)
        foreach (var (table, indexName) in TopologyTables)
        {
            migrationBuilder.AddColumn<Guid>(
                name:         "tenant_id",
                schema:       Schema,
                table:        table,
                type:         "uniqueidentifier",
                nullable:     false,
                defaultValue: WellKnownTenantIds.SystemTenant);

            // Backfill existing rows (already covered by defaultValue above, but explicit for clarity)
            migrationBuilder.Sql(
                $"UPDATE [{Schema}].[{table}] SET [tenant_id] = '{SystemTenantId}' WHERE [tenant_id] = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.CreateIndex(
                name:   indexName,
                schema: Schema,
                table:  table,
                column: "tenant_id");
        }

        // 2. Add FK constraint on sync_node (reference table for all node-related tables)
        migrationBuilder.AddForeignKey(
            name:              "FK_sync_node_tenant_id",
            schema:            Schema,
            table:             "sync_node",
            column:            "tenant_id",
            principalSchema:   Schema,
            principalTable:    "tenant",
            principalColumn:   "tenant_id",
            onDelete:          ReferentialAction.Restrict);

        // 3. Add nullable tenant_id to hybrid tables
        foreach (var table in HybridTables)
        {
            migrationBuilder.AddColumn<Guid>(
                name:     "tenant_id",
                schema:   Schema,
                table:    table,
                type:     "uniqueidentifier",
                nullable: true,
                defaultValue: null);
        }

        // 4. Rename sync_monitor → sync_monitor_snapshot
        migrationBuilder.RenameTable(
            name:      "sync_monitor",
            schema:    Schema,
            newName:   "sync_monitor_snapshot",
            newSchema: Schema);

        // 5. Add lock_scope to sync_lock
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
        // Remove lock_scope
        migrationBuilder.DropColumn(name: "lock_scope", schema: Schema, table: "sync_lock");

        // Rename back
        migrationBuilder.RenameTable(
            name:      "sync_monitor_snapshot",
            schema:    Schema,
            newName:   "sync_monitor",
            newSchema: Schema);

        // Remove nullable tenant_id from hybrid tables
        foreach (var table in HybridTables)
            migrationBuilder.DropColumn(name: "tenant_id", schema: Schema, table: table);

        // Remove FK and tenant_id from topology tables
        migrationBuilder.DropForeignKey(
            name: "FK_sync_node_tenant_id", schema: Schema, table: "sync_node");

        foreach (var (table, indexName) in TopologyTables)
        {
            migrationBuilder.DropIndex(name: indexName, schema: Schema, table: table);
            migrationBuilder.DropColumn(name: "tenant_id", schema: Schema, table: table);
        }
    }
}
```

> **Note on table names:** The exact SQL table names (`sync_node`, `sync_channel`, etc.) must match what the existing EF configurations declare in `ToTable(...)`. Verify each name before applying. If a table is named differently (e.g., `Nodes` without the `sync_` prefix), use the correct name from the existing configuration file.

- [ ] **Step 7: Update EF model snapshot**

Run the migration scaffold to update `AppDbContextModelSnapshot.cs`:
```
dotnet ef migrations add M031_CoreTopologyTenantId --project src/MSOSync.Persistence --startup-project src/MSOSync.App -- --environment Development
```

This generates a scaffold. Replace the generated `Up`/`Down` with the content from Step 6 above. Keep EF's auto-generated changes to `AppDbContextModelSnapshot.cs`.

- [ ] **Step 8: Apply migration**

```
dotnet ef database update M031_CoreTopologyTenantId --project src/MSOSync.Persistence --startup-project src/MSOSync.App
```
Expected: `Done. Applied 1 migration.`

- [ ] **Step 9: Verify schema and backfill**

```sql
-- Verify tenant_id column exists on sync_node
SELECT column_name, is_nullable, column_default
FROM information_schema.columns
WHERE table_schema = 'msosync' AND table_name = 'sync_node' AND column_name = 'tenant_id';
-- Expected: 1 row, is_nullable = NO

-- Verify all existing nodes now have SystemTenant
SELECT COUNT(*) FROM [msosync].[sync_node]
WHERE [tenant_id] != '00000000-0000-0000-0000-000000000001';
-- Expected: 0 rows (all backfilled)

-- Verify sync_monitor_snapshot exists
SELECT table_name FROM information_schema.tables
WHERE table_schema = 'msosync' AND table_name = 'sync_monitor_snapshot';
-- Expected: 1 row
```

- [ ] **Step 10: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```
Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 11: Run all unit tests**

```
dotnet test D:\MSOSync\MSOSync.sln --filter "NOT IntegrationTests" -v normal
```
Expected: all existing + new unit tests pass

- [ ] **Step 12: Commit**

```
git add src/MSOSync.Persistence/Migrations/M031_CoreTopologyTenantId.cs
git add src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs
git add src/MSOSync.Persistence/Configurations/
git add src/MSOSync.Persistence/Entities/SyncLock.cs
git commit -m "feat(15A-7): M031 topology TenantId migration, hybrid nullable TenantId, monitor rename, lock scope"
```
