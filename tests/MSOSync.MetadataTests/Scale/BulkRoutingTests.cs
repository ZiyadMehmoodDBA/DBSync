using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using Xunit;

namespace MSOSync.MetadataTests.Scale;

/// <summary>
/// Shared database fixture for BulkRoutingTests.
/// Created once per test collection; torn down after all tests complete.
/// </summary>
public sealed class BulkRoutingFixture : IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSync_BulkRouting_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    public static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public AppDbContext CreateDb()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        return new AppDbContext(opts);
    }

    public async Task InitializeAsync()
    {
        var db = CreateDb();
        await using (db)
        {
            // Drop and recreate to ensure a clean slate.
            await db.Database.EnsureDeletedAsync();
            await db.Database.MigrateAsync();

            // Seed: one node group, one router, one trigger-router, 3 active nodes, 1 inactive
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = "g-target", TenantId = TenantId });
            await db.SaveChangesAsync();

            db.Routers.Add(new SyncRouter
            {
                RouterId        = "r-test",
                SourceNodeGroup = "g-source",
                TargetNodeGroup = "g-target",
                Enabled         = true,
                TenantId        = TenantId
            });
            await db.SaveChangesAsync();

            db.Triggers.Add(new SyncTrigger
            {
                TriggerId   = "trig-test",
                SourceTable = "dbo.Orders",
                ChannelId   = "ch-default",
                TenantId    = TenantId
            });
            await db.SaveChangesAsync();

            db.TriggerRouters.Add(new SyncTriggerRouter
            {
                TriggerId = "trig-test",
                RouterId  = "r-test",
                Enabled   = true,
                TenantId  = TenantId
            });
            await db.SaveChangesAsync();

            // 3 active nodes in target group
            for (int i = 1; i <= 3; i++)
                db.Nodes.Add(new SyncNode
                {
                    NodeId          = $"node-{i:D3}",
                    GroupId         = "g-target",
                    SyncUrl         = $"http://node-{i}",
                    LifecycleState  = NodeLifecycleState.Active,
                    MaintenanceMode = false,
                    TenantId        = TenantId
                });

            // 1 disabled node (should NOT be included in fan-out)
            db.Nodes.Add(new SyncNode
            {
                NodeId          = "node-disabled",
                GroupId         = "g-target",
                SyncUrl         = "http://disabled",
                LifecycleState  = NodeLifecycleState.Disabled,
                MaintenanceMode = false,
                TenantId        = TenantId
            });
            await db.SaveChangesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        var db = CreateDb();
        await using (db)
        {
            // Force-drop the test database, terminating any active connections first.
            try
            {
                await db.Database.ExecuteSqlRawAsync(
                    "ALTER DATABASE [MSOSync_BulkRouting_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
            }
            catch
            {
                // Ignore if already in single-user mode or database does not exist.
            }

            await db.Database.EnsureDeletedAsync();
        }
    }
}

[CollectionDefinition("BulkRouting")]
public sealed class BulkRoutingCollection : ICollectionFixture<BulkRoutingFixture> { }

/// <summary>
/// Integration tests for BulkRoutingService.
/// These tests use a SQL Server LocalDB database because INSERT … OUTPUT
/// is not supported by SQLite in-memory.
/// Tests within this collection share one database and must restore state
/// if they mutate it (or use independent batch sequences to avoid conflicts).
/// </summary>
[Collection("BulkRouting")]
public sealed class BulkRoutingTests
{
    private readonly BulkRoutingFixture _fixture;

    public BulkRoutingTests(BulkRoutingFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task FanOut_3EligibleNodes_Inserts3Batches()
    {
        var db  = _fixture.CreateDb();
        await using (db)
        {
            var svc = new BulkRoutingService(db);

            var batchIds = await svc.FanOutAsync(
                triggerId:     "trig-test",
                channelId:     "ch-default",
                batchSequence: 1L,
                rowCount:      10,
                byteCount:     512L,
                tenantId:      BulkRoutingFixture.TenantId);

            batchIds.Should().HaveCount(3);

            var count = await db.OutgoingBatches.AsNoTracking()
                .CountAsync(b => b.TenantId == BulkRoutingFixture.TenantId
                              && b.BatchSequence == 1L);
            count.Should().Be(3);
        }
    }

    [Fact]
    public async Task FanOut_DisabledNodeExcluded_NotInserted()
    {
        var db  = _fixture.CreateDb();
        await using (db)
        {
            var svc = new BulkRoutingService(db);

            await svc.FanOutAsync("trig-test", "ch-default", 2L, 5, 256L,
                BulkRoutingFixture.TenantId);

            var nodeIds = await db.OutgoingBatches.AsNoTracking()
                .Where(b => b.TenantId == BulkRoutingFixture.TenantId
                         && b.BatchSequence == 2L)
                .Select(b => b.NodeId)
                .ToListAsync();

            nodeIds.Should().NotContain("node-disabled");
        }
    }

    [Fact]
    public async Task FanOut_NoEligibleNodes_ReturnsEmptyList()
    {
        // Use a unique trigger that has no routers so no nodes are matched,
        // avoiding global state mutation.
        var db  = _fixture.CreateDb();
        await using (db)
        {
            var svc = new BulkRoutingService(db);

            // A trigger ID that does not exist in sync_trigger_router → no eligible nodes.
            var batchIds = await svc.FanOutAsync(
                "nonexistent-trigger", "ch-default", 3L, 5, 100L,
                BulkRoutingFixture.TenantId);

            batchIds.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task FanOut_DisabledRouter_ReturnsEmptyList()
    {
        // Seed a separate disabled trigger-router so we don't mutate shared state.
        var setup = _fixture.CreateDb();
        await using (setup)
        {
            setup.Routers.Add(new SyncRouter
            {
                RouterId        = "r-disabled",
                SourceNodeGroup = "g-source",
                TargetNodeGroup = "g-target",
                Enabled         = false,           // router disabled
                TenantId        = BulkRoutingFixture.TenantId
            });
            await setup.SaveChangesAsync();

            setup.Triggers.Add(new SyncTrigger
            {
                TriggerId   = "trig-disabled-router",
                SourceTable = "dbo.Orders",
                ChannelId   = "ch-default",
                TenantId    = BulkRoutingFixture.TenantId
            });
            await setup.SaveChangesAsync();

            setup.TriggerRouters.Add(new SyncTriggerRouter
            {
                TriggerId = "trig-disabled-router",
                RouterId  = "r-disabled",
                Enabled   = true,
                TenantId  = BulkRoutingFixture.TenantId
            });
            await setup.SaveChangesAsync();
        }

        var db  = _fixture.CreateDb();
        await using (db)
        {
            var svc     = new BulkRoutingService(db);
            var batchIds = await svc.FanOutAsync(
                "trig-disabled-router", "ch-default", 4L, 1, 50L,
                BulkRoutingFixture.TenantId);

            batchIds.Should().BeEmpty();
        }
    }

    [Fact]
    public async Task FanOut_ReturnedBatchIds_MatchInsertedRows()
    {
        var db  = _fixture.CreateDb();
        await using (db)
        {
            var svc = new BulkRoutingService(db);

            var batchIds = await svc.FanOutAsync(
                "trig-test", "ch-default", 5L, 10, 1024L,
                BulkRoutingFixture.TenantId);

            // All returned IDs should exist in the outgoing batches table
            var existing = await db.OutgoingBatches.AsNoTracking()
                .Where(b => batchIds.Contains(b.BatchId))
                .Select(b => b.BatchId)
                .ToListAsync();

            existing.Should().BeEquivalentTo(batchIds);
        }
    }
}
