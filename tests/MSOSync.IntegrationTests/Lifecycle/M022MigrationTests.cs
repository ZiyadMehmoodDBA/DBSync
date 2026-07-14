using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

/// <summary>
/// M022 legacy conversion tests â€” use their own LocalDB database (NOT the shared Lifecycle fixture).
/// Each test class creates + drops its own DB so tests can run independently.
/// </summary>
public sealed class M022MigrationTests : IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncM022_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        _db = new AppDbContext(opts);

        // Drop and recreate for a clean slate
        if (await _db.Database.CanConnectAsync())
        {
            await _db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncM022_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await _db.Database.EnsureDeletedAsync();
        }

        // Step 1: Migrate to M021 (stop BEFORE M022)
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260706131159_M021_AddNodeTypeExternalId");

        // Step 2: Ensure schema exists
        await _db.Database.ExecuteSqlRawAsync(
            "IF NOT EXISTS (SELECT 1 FROM sys.schemas WHERE name = 'msosync') EXEC('CREATE SCHEMA msosync')");

        // Step 3: We need the base tables. By this point M001-M021 have run.
        // Insert the required supporting data (group, roles, etc.) before inserting nodes.
        await _db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_node_group] WHERE group_id = 'g')
                INSERT INTO [msosync].[sync_node_group] (group_id, group_name) VALUES ('g', 'Test Group');
            """);

        // Step 4: Insert legacy status rows via raw SQL (one per legacy status value)
        await _db.Database.ExecuteSqlRawAsync("""
            INSERT INTO [msosync].[sync_node]
                (node_id, group_id, sync_url, status, sync_enabled, node_type)
            VALUES
                ('leg-pending',    'g', 'http://x', 'PENDING',    1, 'source'),
                ('leg-approved',   'g', 'http://x', 'APPROVED',   1, 'source'),
                ('leg-provisioned','g', 'http://x', 'PROVISIONED',1, 'source'),
                ('leg-registered', 'g', 'http://x', 'REGISTERED', 1, 'source'),
                ('leg-offline',    'g', 'http://x', 'OFFLINE',    1, 'source'),
                ('leg-disabled',   'g', 'http://x', 'DISABLED',   0, 'source');
            """);
    }

    public async Task DisposeAsync()
    {
        await using (_db)
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "ALTER DATABASE [MSOSyncM022_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                await _db.Database.EnsureDeletedAsync();
            }
            catch { /* ignore teardown errors */ }
        }
    }

    private async Task ApplyM022Async()
    {
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync(); // applies M022 and anything after
    }

    [Fact]
    public async Task Pending_MapsTo_PendingApproval()
    {
        await ApplyM022Async();
        var node = await _db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == "leg-pending");
        node.LifecycleState.Should().Be(NodeLifecycleState.PendingApproval);
    }

    [Fact]
    public async Task Approved_And_Provisioned_MapTo_PendingRegistration()
    {
        await ApplyM022Async();
        var approved    = await _db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == "leg-approved");
        var provisioned = await _db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == "leg-provisioned");
        approved.LifecycleState.Should().Be(NodeLifecycleState.PendingRegistration);
        provisioned.LifecycleState.Should().Be(NodeLifecycleState.PendingRegistration);
    }

    [Fact]
    public async Task Registered_And_Offline_MapTo_Active()
    {
        await ApplyM022Async();
        var registered = await _db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == "leg-registered");
        var offline    = await _db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == "leg-offline");
        registered.LifecycleState.Should().Be(NodeLifecycleState.Active);
        offline.LifecycleState.Should().Be(NodeLifecycleState.Active);
    }

    [Fact]
    public async Task Disabled_MapsTo_Disabled()
    {
        await ApplyM022Async();
        var node = await _db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == "leg-disabled");
        node.LifecycleState.Should().Be(NodeLifecycleState.Disabled);
    }

    [Fact]
    public async Task EveryNode_HasSeedHistoryRow_FromStateNull_TriggerMigration_ReasonM022()
    {
        await ApplyM022Async();
        var nodeIds = new[] { "leg-pending", "leg-approved", "leg-provisioned", "leg-registered", "leg-offline", "leg-disabled" };
        foreach (var id in nodeIds)
        {
            var history = await _db.NodeLifecycleHistories.AsNoTracking()
                .Where(h => h.NodeId == id && h.Trigger == LifecycleTrigger.Migration)
                .ToListAsync();
            history.Should().NotBeEmpty($"node {id} must have a Migration history row");
            var row = history.First();
            row.FromState.Should().BeNull("M022 seed rows have FromState = NULL");
            row.Reason.Should().Contain("M022", because: "seed reason references the migration");
        }
    }

    [Fact]
    public async Task SyncEnabledColumn_Gone()
    {
        await ApplyM022Async();
        // COL_LENGTH returns NULL if column does not exist
        var result = await _db.Database.SqlQueryRaw<int?>(
            "SELECT COL_LENGTH('msosync.sync_node', 'sync_enabled') AS Value")
            .FirstOrDefaultAsync();
        result.Should().BeNull("sync_enabled column must be removed by M022");
    }

    [Fact]
    public async Task Permissions_Seeded()
    {
        await ApplyM022Async();
        var permKeys = await _db.Permissions.Select(p => p.PermissionKey).ToListAsync();
        permKeys.Should().Contain("PROVISION_NODES");
        permKeys.Should().Contain("MANAGE_NODE_LIFECYCLE");

        var operatorHasLifecycle = await _db.RolePermissions.AnyAsync(
            rp => rp.RoleName == "OPERATOR" && rp.PermissionKey == "MANAGE_NODE_LIFECYCLE");
        operatorHasLifecycle.Should().BeTrue("OPERATOR must have MANAGE_NODE_LIFECYCLE per M022 seed");
    }
}

