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
        var count = await _db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*) AS Value FROM sys.indexes
                WHERE name IN (
                    'IX_sync_data_event_batch_event_id',
                    'IX_sync_node_connectivity_status',
                    'IX_sync_node_group_id',
                    'IX_sync_outgoing_batch_create_time',
                    'IX_sync_node_lifecycle_state')
                """)
            .FirstAsync();

        count.Should().Be(5, because: "Up() must create all five M038 indexes");
    }

    [Fact]
    public async Task M038_Down_DropsAllFiveIndexes()
    {
        // Roll back to M037 (one migration before M038)
        await _db.Database.MigrateAsync("M037_MarketplaceCache");

        var count = await _db.Database
            .SqlQuery<int>($"""
                SELECT COUNT(*) AS Value FROM sys.indexes
                WHERE name IN (
                    'IX_sync_data_event_batch_event_id',
                    'IX_sync_node_connectivity_status',
                    'IX_sync_node_group_id',
                    'IX_sync_outgoing_batch_create_time',
                    'IX_sync_node_lifecycle_state')
                """)
            .FirstAsync();

        count.Should().Be(0, because: "Down() must drop all five M038 indexes");
    }

    public async Task DisposeAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSync_M038_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }
}
