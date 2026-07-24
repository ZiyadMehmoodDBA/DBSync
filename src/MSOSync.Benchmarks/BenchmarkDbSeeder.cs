using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Benchmarks;

/// <summary>
/// Seeds a LocalDB database with 1000 nodes across 200 groups and 400 router edges
/// for use in Phase 2D.4 benchmarks.
/// Call EnsureSeededAsync() once in benchmark [GlobalSetup].
/// </summary>
public sealed class BenchmarkDbSeeder
{
    public const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSync_Benchmarks;" +
        "Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;";

    public static readonly Guid TenantId = Guid.Parse("10000000-0000-0000-0000-000000000001");

    public static AppDbContext CreateDb()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        return new AppDbContext(opts);
    }

    public static async Task EnsureSeededAsync()
    {
        using var db = CreateDb();
        await db.Database.MigrateAsync();

        if (await db.NodeGroups.AnyAsync())
            return; // already seeded

        Console.WriteLine("[BenchmarkDbSeeder] Seeding 200 groups + 1000 nodes...");

        const int groupCount  = 200;
        const int nodeCount   = 1000;
        const int routerCount = 400;

        // Groups
        var groups = Enumerable.Range(1, groupCount).Select(i => new SyncNodeGroup
        {
            GroupId  = $"group-{i:D3}",
            GroupName = $"Group {i}",
            TenantId = TenantId
        }).ToList();
        db.NodeGroups.AddRange(groups);
        await db.SaveChangesAsync();

        // Nodes: distribute evenly — 5 nodes per group
        var nodes = Enumerable.Range(1, nodeCount).Select(i =>
        {
            int groupIndex = ((i - 1) % groupCount) + 1;
            return new SyncNode
            {
                NodeId              = $"node-{i:D4}",
                GroupId             = $"group-{groupIndex:D3}",
                SyncUrl             = $"http://node-{i}",
                LifecycleState      = NodeLifecycleState.Active,
                MaintenanceMode     = false,
                ConnectivityStatus  = (ConnectivityStatus)(i % 4),  // mix statuses
                TenantId            = TenantId
            };
        }).ToList();

        // Bulk insert nodes in chunks to avoid parameter limits
        foreach (var chunk in nodes.Chunk(100))
        {
            db.Nodes.AddRange(chunk);
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        // Channel
        db.Channels.Add(new SyncChannel
        {
            ChannelId = "ch-bench",
            TenantId  = TenantId
        });
        await db.SaveChangesAsync();

        // Routers: 400 edges distributed across groups (source → target)
        var routers = Enumerable.Range(1, routerCount).Select(i =>
        {
            int srcGroup = ((i - 1) % groupCount) + 1;
            int tgtGroup = (i % groupCount) + 1;  // next group (wraps)
            return new SyncRouter
            {
                RouterId        = $"router-{i:D4}",
                SourceNodeGroup = $"group-{srcGroup:D3}",
                TargetNodeGroup = $"group-{tgtGroup:D3}",
                Enabled         = true,
                TenantId        = TenantId
            };
        }).ToList();
        db.Routers.AddRange(routers);
        await db.SaveChangesAsync();

        // One trigger
        db.Triggers.Add(new SyncTrigger
        {
            TriggerId   = "trig-bench-01",
            SourceTable = "dbo.Orders",
            ChannelId   = "ch-bench",
            TenantId    = TenantId
        });
        await db.SaveChangesAsync();

        // Link trigger to all 400 routers
        foreach (var chunk in routers.Chunk(100))
        {
            db.TriggerRouters.AddRange(chunk.Select(r => new SyncTriggerRouter
            {
                TriggerId = "trig-bench-01",
                RouterId  = r.RouterId,
                Enabled   = true,
                TenantId  = TenantId
            }));
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();
        }

        Console.WriteLine("[BenchmarkDbSeeder] Seeding complete.");
    }

    public static async Task TeardownAsync()
    {
        using var db = CreateDb();
        await db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSync_Benchmarks] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await db.Database.EnsureDeletedAsync();
    }
}
