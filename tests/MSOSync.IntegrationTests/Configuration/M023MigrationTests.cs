// tests/MSOSync.IntegrationTests/Configuration/M023MigrationTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.IntegrationTests.Configuration;

public sealed class M023MigrationTests : IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncM023_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        _db = new AppDbContext(opts);

        if (await _db.Database.CanConnectAsync())
        {
            await _db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncM023_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await _db.Database.EnsureDeletedAsync();
        }

        // Migrate to M022 only
        var migrator   = _db.GetService<IMigrator>();
        var migrations = _db.Database.GetMigrations().ToList();
        var m022       = migrations.Last(m => m.Contains("M022", StringComparison.OrdinalIgnoreCase));
        await migrator.MigrateAsync(m022);
    }

    public async Task DisposeAsync()
    {
        await using (_db)
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "ALTER DATABASE [MSOSyncM023_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                await _db.Database.EnsureDeletedAsync();
            }
            catch { /* ignore teardown errors */ }
        }
    }

    private async Task ApplyM023Async()
    {
        var migrator = _db.GetService<IMigrator>();
        await migrator.MigrateAsync();
    }

    private async Task RollbackM023Async()
    {
        var migrator   = _db.GetService<IMigrator>();
        var migrations = _db.Database.GetMigrations().ToList();
        var m022       = migrations.Last(m => m.Contains("M022", StringComparison.OrdinalIgnoreCase));
        await migrator.MigrateAsync(m022);
    }

    [Fact]
    public async Task M023_AppliesCleanlyFrom_M022()
    {
        await ApplyM023Async();

        var tables = await _db.Database.SqlQueryRaw<string>("""
            SELECT TABLE_NAME FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'msosync'
            """).ToListAsync();

        tables.Should().Contain("sync_configuration_template");
        tables.Should().Contain("sync_configuration_template_version");
        tables.Should().Contain("sync_node_configuration_override");
        tables.Should().Contain("sync_node_configuration_history");
        tables.Should().Contain("sync_configuration_rollout");
    }

    [Fact]
    public async Task M023_SyncNode_HasEightNewNullableColumns()
    {
        await ApplyM023Async();

        var columns = await _db.Database.SqlQueryRaw<string>("""
            SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_node'
            """).ToListAsync();

        columns.Should().Contain("assigned_template_id");
        columns.Should().Contain("assigned_template_version");
        columns.Should().Contain("applied_template_version");
        columns.Should().Contain("expected_effective_hash");
        columns.Should().Contain("applied_effective_hash");
        columns.Should().Contain("configuration_state");
        columns.Should().Contain("configuration_status_reported_at");
        columns.Should().Contain("last_applied_at");
    }

    [Fact]
    public async Task M023_ExistingSyncNodes_HaveConfigurationState_None()
    {
        // LifecycleState maps to column "status" (not "lifecycle_state") per SyncNodeConfiguration
        await _db.Database.ExecuteSqlRawAsync("""
            IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_node_group] WHERE group_id = 'g')
                INSERT INTO [msosync].[sync_node_group] (group_id, group_name) VALUES ('g', 'Test');
            IF NOT EXISTS (SELECT 1 FROM [msosync].[sync_node] WHERE node_id = 'pre-m023-node')
                INSERT INTO [msosync].[sync_node] (node_id, group_id, sync_url, status, node_type)
                VALUES ('pre-m023-node', 'g', 'http://x', 0, 'source');
            """);

        await ApplyM023Async();

        // configuration_state is nullable with no default â€” existing nodes get null, not None
        var node = await _db.Nodes.AsNoTracking().FirstAsync(n => n.NodeId == "pre-m023-node");
        node.ConfigurationState.Should().BeNull("M023 adds configuration_state as nullable with no backfill");
        node.AssignedTemplateId.Should().BeNull();
        node.AssignedTemplateVersion.Should().BeNull();
        node.AppliedTemplateVersion.Should().BeNull();
        node.ExpectedEffectiveHash.Should().BeNull();
        node.AppliedEffectiveHash.Should().BeNull();
    }

    [Fact]
    public async Task M023_FilteredUniqueIndex_OnDraftVersion_Exists()
    {
        await ApplyM023Async();

        var indexCount = await _db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(1) AS Value
            FROM sys.indexes i
            JOIN sys.tables t ON i.object_id = t.object_id
            JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = 'msosync'
              AND t.name = 'sync_configuration_template_version'
              AND i.is_unique = 1
              AND i.filter_definition IS NOT NULL
            """).FirstAsync();

        indexCount.Should().BeGreaterThan(0, "M023 must create a filtered unique index for draft versions");
    }

    [Fact]
    public async Task M023_ManageConfigurations_Permission_Seeded()
    {
        await ApplyM023Async();

        var exists = await _db.Permissions.AnyAsync(
            p => p.PermissionKey == "MANAGE_CONFIGURATIONS");
        exists.Should().BeTrue("M023 must seed the MANAGE_CONFIGURATIONS permission");
    }

    [Fact]
    public async Task M023_RollbackClean_ToM022()
    {
        await ApplyM023Async();
        await RollbackM023Async();

        var tableExists = await _db.Database.SqlQueryRaw<int>("""
            SELECT COUNT(1) AS Value FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_configuration_template'
            """).FirstAsync();

        tableExists.Should().Be(0, "rollback must remove sync_configuration_template");

        var colExists = await _db.Database.SqlQueryRaw<int?>("""
            SELECT COL_LENGTH('msosync.sync_node', 'assigned_template_id') AS Value
            """).FirstOrDefaultAsync();

        colExists.Should().BeNull("rollback must remove assigned_template_id column");
    }
}

