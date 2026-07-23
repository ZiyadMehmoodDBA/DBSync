# Task 1: M038 Indexes Migration

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create EF Core migration `M038_ScaleIndexes` that adds 5 non-breaking performance indexes, with a `Down()` method that drops all 5.

**Why M038:** Migrations M031 through M037 already exist (`M031_CoreTopologyTenantId`, `M032_DomainTenantIdMigration`, `M033_RollingOperations`, `M034_BatchReplay`, and several others). M038 is the correct next number.

**No dependencies on other 2D.4 tasks.** This migration can land first so the other tasks benefit from indexes immediately.

## Files

- Create: `src/MSOSync.Persistence/Migrations/M038_ScaleIndexes.cs`
- Modify: `src/MSOSync.Persistence/Migrations/AppDbContextModelSnapshot.cs` (EF will update this automatically when you run `dotnet ef migrations add`; the step below does it manually via raw migration class, so snapshot is updated by `dotnet ef database update`)

## Context

The migration file pattern in this repo uses a plain class name (no timestamp prefix) when added manually. Examples: `M033_RollingOperations.cs`, `M034_BatchReplay.cs`. EF does not require a Designer file if you add the migration manually, but you must register it in the `__EFMigrationsHistory` table ordering. The simplest approach is to copy the class structure from `M034_BatchReplay.cs`, name it `M038_ScaleIndexes`, and give it the `[Migration("M038_ScaleIndexes")]` attribute so EF finds it in sequence.

Look at `src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs` line 1–7 for the namespace and class structure.

## Steps

- [ ] **Step 1: Read the existing last migration to confirm structure**

Open `src/MSOSync.Persistence/Migrations/M034_BatchReplay.cs`. Confirm it starts:
```csharp
using Microsoft.EntityFrameworkCore.Migrations;

namespace MSOSync.Persistence.Migrations;

public partial class M034_BatchReplay : Migration
{
    private const string Schema = "msosync";
    protected override void Up(MigrationBuilder migrationBuilder) { ... }
    protected override void Down(MigrationBuilder migrationBuilder) { ... }
}
```

- [ ] **Step 2: Create `M038_ScaleIndexes.cs`**

Create `src/MSOSync.Persistence/Migrations/M038_ScaleIndexes.cs`:

```csharp
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MSOSync.Persistence.Migrations;

[Migration("M038_ScaleIndexes")]
public partial class M038_ScaleIndexes : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Index 1: Covers EventQueryService correlated MAX(batch_id) subquery.
        // The existing composite PK (event_id, batch_id) is not efficiently used
        // by the nested-loop lookup EF generates; a dedicated single-column index
        // on event_id gives a clean seek.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_data_event_batch_event_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_data_event_batch]'))
            CREATE INDEX [IX_sync_data_event_batch_event_id]
                ON [msosync].[sync_data_event_batch] ([event_id] ASC);
        ");

        // Index 2: Covers GetTopologySummaryAsync status-bucket counts and
        // DashboardSummaryDto reachability counts (GROUP BY connectivity_status).
        // INCLUDE adds lifecycle_state + maintenance_mode so ClusterSummaryQueryService
        // projection (SELECT lifecycle_state, maintenance_mode) becomes a covering scan.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_connectivity_status'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            CREATE INDEX [IX_sync_node_connectivity_status]
                ON [msosync].[sync_node] ([connectivity_status] ASC)
                INCLUDE ([lifecycle_state], [maintenance_mode]);
        ");

        // Index 3: Covers GetGroupNodesAsync (filter by group_id, ORDER BY node_id
        // for cursor pagination). INCLUDE adds status fields used in
        // TopologyGroupNodeDto projection for a covering scan.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_group_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            CREATE INDEX [IX_sync_node_group_id]
                ON [msosync].[sync_node] ([group_id] ASC, [node_id] ASC)
                INCLUDE ([lifecycle_state], [connectivity_status]);
        ");

        // Index 4: Covers dashboard and metrics queries that filter outgoing batches
        // by time window. Existing IX_sync_outgoing_batch_node_status covers per-node
        // status lookups; this index covers time-range queries on create_time.
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_outgoing_batch_create_time'
                  AND object_id = OBJECT_ID('[msosync].[sync_outgoing_batch]'))
            CREATE INDEX [IX_sync_outgoing_batch_create_time]
                ON [msosync].[sync_outgoing_batch] ([create_time] DESC)
                INCLUDE ([node_id], [channel_id], [status]);
        ");

        // Index 5: Covers IBulkRoutingService.FanOutAsync WHERE lifecycle_state = 3
        // predicate and NodeSyncPolicy.EligibleExpression used by RoutingService.ResolveAsync.
        // INCLUDE adds group_id (JOIN predicate), maintenance_mode (eligibility filter),
        // tenant_id (isolation predicate).
        migrationBuilder.Sql(@"
            IF NOT EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_lifecycle_state'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            CREATE INDEX [IX_sync_node_lifecycle_state]
                ON [msosync].[sync_node] ([lifecycle_state] ASC)
                INCLUDE ([group_id], [maintenance_mode], [tenant_id]);
        ");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_data_event_batch_event_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_data_event_batch]'))
            DROP INDEX [IX_sync_data_event_batch_event_id]
                ON [msosync].[sync_data_event_batch];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_connectivity_status'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            DROP INDEX [IX_sync_node_connectivity_status]
                ON [msosync].[sync_node];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_group_id'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            DROP INDEX [IX_sync_node_group_id]
                ON [msosync].[sync_node];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_outgoing_batch_create_time'
                  AND object_id = OBJECT_ID('[msosync].[sync_outgoing_batch]'))
            DROP INDEX [IX_sync_outgoing_batch_create_time]
                ON [msosync].[sync_outgoing_batch];
        ");

        migrationBuilder.Sql(@"
            IF EXISTS (
                SELECT 1 FROM sys.indexes
                WHERE name = 'IX_sync_node_lifecycle_state'
                  AND object_id = OBJECT_ID('[msosync].[sync_node]'))
            DROP INDEX [IX_sync_node_lifecycle_state]
                ON [msosync].[sync_node];
        ");
    }
}
```

- [ ] **Step 3: Write a migration rollback integration test**

Create `tests/MSOSync.IntegrationTests/Scale/M038MigrationTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Scale;

/// <summary>
/// Verifies M038_ScaleIndexes can be applied and rolled back cleanly.
/// Requires LocalDB (runs in CI alongside other migration smoke tests).
/// </summary>
public sealed class M038MigrationTests : IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSync_M038_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        _db = new AppDbContext(opts);
        await _db.Database.MigrateAsync();
    }

    [Fact]
    public async Task M038_Up_CreatesAllFiveIndexes()
    {
        // Query sys.indexes for all five index names
        var indexNames = new[]
        {
            "IX_sync_data_event_batch_event_id",
            "IX_sync_node_connectivity_status",
            "IX_sync_node_group_id",
            "IX_sync_outgoing_batch_create_time",
            "IX_sync_node_lifecycle_state",
        };

        foreach (var name in indexNames)
        {
            var exists = await _db.Database
                .ExecuteSqlRawAsync($"""
                    IF NOT EXISTS (
                        SELECT 1 FROM sys.indexes WHERE name = '{name}')
                    RAISERROR('Index {name} not found', 16, 1)
                    """);
            // ExecuteSqlRawAsync returns rows affected; if the RAISERROR fires EF throws.
            // Simply reaching here without exception means the index exists.
        }
    }

    [Fact]
    public async Task M038_Down_DropsAllFiveIndexes()
    {
        // Roll back to M037 (one migration before M038)
        await _db.Database.MigrateAsync("M037");   // target migration name

        var indexNames = new[]
        {
            "IX_sync_data_event_batch_event_id",
            "IX_sync_node_connectivity_status",
            "IX_sync_node_group_id",
            "IX_sync_outgoing_batch_create_time",
            "IX_sync_node_lifecycle_state",
        };

        foreach (var name in indexNames)
        {
            var count = await _db.Database
                .SqlQueryRaw<int>(
                    $"SELECT COUNT(*) FROM sys.indexes WHERE name = '{name}'")
                .FirstAsync();
            count.Should().Be(0, because: $"Down() must drop {name}");
        }
    }

    public async Task DisposeAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSync_M038_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }
}
```

- [ ] **Step 4: Run the Up test to verify it passes**

```
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj \
  --filter "FullyQualifiedName~M038MigrationTests.M038_Up_CreatesAllFiveIndexes" \
  -v normal
```

Expected output: `1 passed`.

- [ ] **Step 5: Run the Down test to verify rollback works**

```
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj \
  --filter "FullyQualifiedName~M038MigrationTests.M038_Down_DropsAllFiveIndexes" \
  -v normal
```

Expected output: `1 passed`.

- [ ] **Step 6: Commit**

```
git add src/MSOSync.Persistence/Migrations/M038_ScaleIndexes.cs \
        tests/MSOSync.IntegrationTests/Scale/M038MigrationTests.cs
git commit -m "feat(2D.4-T1): add M038_ScaleIndexes with 5 covering indexes and rollback test"
```
