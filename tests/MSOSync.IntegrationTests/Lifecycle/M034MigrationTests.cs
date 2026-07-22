using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.IntegrationTests.Lifecycle;

/// <summary>
/// M034 schema-smoke tests — use their own LocalDB database (NOT the shared Lifecycle fixture).
/// </summary>
public sealed class M034MigrationTests : IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSyncM034_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private AppDbContext _db = null!;

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        _db = new AppDbContext(opts);

        if (await _db.Database.CanConnectAsync())
        {
            await _db.Database.ExecuteSqlRawAsync(
                "ALTER DATABASE [MSOSyncM034_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
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
                    "ALTER DATABASE [MSOSyncM034_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
                await _db.Database.EnsureDeletedAsync();
            }
            catch { /* ignore teardown errors */ }
        }
    }

    [Fact]
    public async Task M034_creates_sync_replay_request_table()
    {
        var tables = await _db.Database
            .SqlQueryRaw<string>(
                "SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_replay_request'")
            .ToListAsync();

        tables.Should().ContainSingle(t => t == "sync_replay_request");
    }

    [Fact]
    public async Task M034_creates_sync_replay_item_table()
    {
        var tables = await _db.Database
            .SqlQueryRaw<string>(
                "SELECT TABLE_NAME AS Value FROM INFORMATION_SCHEMA.TABLES " +
                "WHERE TABLE_SCHEMA = 'msosync' AND TABLE_NAME = 'sync_replay_item'")
            .ToListAsync();

        tables.Should().ContainSingle(t => t == "sync_replay_item");
    }

    [Fact]
    public async Task M034_creates_expected_indexes_on_replay_item()
    {
        var indexes = await _db.Database
            .SqlQueryRaw<string>(
                "SELECT i.name AS Value FROM sys.indexes i " +
                "JOIN sys.objects o ON i.object_id = o.object_id " +
                "JOIN sys.schemas s ON o.schema_id = s.schema_id " +
                "WHERE s.name = 'msosync' AND o.name = 'sync_replay_item' AND i.is_primary_key = 0")
            .ToListAsync();

        indexes.Should().Contain("ix_sync_replay_item_op_status");
        indexes.Should().Contain("ix_sync_replay_item_tenant_node");
    }
}
