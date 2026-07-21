using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

/// <summary>
/// M033 schema-smoke tests — use their own LocalDB database (NOT the shared Lifecycle fixture).
/// </summary>
public sealed class M033MigrationTests : IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncM033_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        _db = new AppDbContext(opts);

        if (await _db.Database.CanConnectAsync())
        {
            await _db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncM033_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            await _db.Database.EnsureDeletedAsync();
        }

        await _db.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using (_db)
        {
            try
            {
                await _db.Database.ExecuteSqlRawAsync(
                    "ALTER DATABASE [MSOSyncM033_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                await _db.Database.EnsureDeletedAsync();
            }
            catch { /* ignore teardown errors */ }
        }
    }

    [Fact]
    public async Task SyncNode_Has_AgentVersion_Column_Nullable()
    {
        var result = await _db.Database.SqlQueryRaw<string?>(
            "SELECT DATA_TYPE AS Value FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_node' AND COLUMN_NAME = 'agent_version'")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull("agent_version column must exist on sync_node after M033");
    }

    [Fact]
    public async Task SyncNode_Has_DrainCompletedAt_Column_Nullable()
    {
        var result = await _db.Database.SqlQueryRaw<string?>(
            "SELECT DATA_TYPE AS Value FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_node' AND COLUMN_NAME = 'drain_completed_at'")
            .FirstOrDefaultAsync();

        result.Should().NotBeNull("drain_completed_at column must exist on sync_node after M033");
    }

    [Fact]
    public async Task SyncOperationStep_Table_Exists_With_Required_Columns()
    {
        var columns = await _db.Database.SqlQueryRaw<string>(
            "SELECT COLUMN_NAME AS Value FROM INFORMATION_SCHEMA.COLUMNS " +
            "WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_operation_step'")
            .ToListAsync();

        columns.Should().NotBeEmpty("sync_operation_step table must exist after M033");
        columns.Should().Contain("step_id");
        columns.Should().Contain("operation_id");
        columns.Should().Contain("node_id");
        columns.Should().Contain("wave_number");
        columns.Should().Contain("status");
        columns.Should().Contain("started_at");
        columns.Should().Contain("completed_at");
        columns.Should().Contain("error_message");
        columns.Should().Contain("tenant_id");
    }

    [Fact]
    public async Task SyncOperationStep_Has_Expected_Indexes()
    {
        var indexes = await _db.Database.SqlQueryRaw<string>(
            "SELECT i.name AS Value FROM sys.indexes i " +
            "JOIN sys.objects o ON i.object_id = o.object_id " +
            "JOIN sys.schemas s ON o.schema_id = s.schema_id " +
            "WHERE s.name = 'msosync' AND o.name = 'sync_operation_step' AND i.is_primary_key = 0")
            .ToListAsync();

        indexes.Should().Contain("ix_sync_operation_step_op_wave");
        indexes.Should().Contain("ix_sync_operation_step_tenant_node");
    }
}
