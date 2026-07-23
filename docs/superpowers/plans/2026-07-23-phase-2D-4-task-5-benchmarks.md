# Task 5: BenchmarkDotNet Project

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create `MSOSync.Benchmarks` — a standalone BenchmarkDotNet project with 4 benchmarks that measure the four optimised hot paths from Phase 2D.4. Run benchmarks manually and record the baseline results in `docs/superpowers/benchmarks/2D-4-baseline.md`. This project is **not added to CI** — it exists for manual performance validation.

**Prerequisites:** T2 (node cursor pagination), T3 (topology query optimization), T4 (bulk routing) must all be complete and committed before T5.

## Files

- Create: `src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj`
- Create: `src/MSOSync.Benchmarks/BenchmarkDbSeeder.cs`
- Create: `src/MSOSync.Benchmarks/TopologyGraphBenchmark.cs`
- Create: `src/MSOSync.Benchmarks/NodeCursorPageBenchmark.cs`
- Create: `src/MSOSync.Benchmarks/BulkFanOutBenchmark.cs`
- Create: `src/MSOSync.Benchmarks/DashboardSummaryBenchmark.cs`
- Create: `src/MSOSync.Benchmarks/Program.cs`
- Create: `docs/superpowers/benchmarks/2D-4-baseline.md`
- **Do NOT add** `MSOSync.Benchmarks` to `MSOSync.sln` CI targets or GitHub Actions.

## Steps

- [ ] **Step 1: Create the `.csproj`**

Create `src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <!-- Benchmarks require Release build -->
    <Optimize>true</Optimize>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="BenchmarkDotNet" Version="0.14.0" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.SqlServer" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" Version="9.0.0" />
    <PackageReference Include="Microsoft.Extensions.Options" Version="9.0.0" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\MSOSync.Metadata\MSOSync.Metadata.csproj" />
    <ProjectReference Include="..\MSOSync.Routing\MSOSync.Routing.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add the project to the solution (manually, not CI)**

```
dotnet sln MSOSync.sln add src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj
```

- [ ] **Step 3: Create `BenchmarkDbSeeder`**

Create `src/MSOSync.Benchmarks/BenchmarkDbSeeder.cs`:

```csharp
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
```

- [ ] **Step 4: Create `TopologyGraphBenchmark`**

Create `src/MSOSync.Benchmarks/TopologyGraphBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Topology;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures GetTopologyGraphAsync at 1000 nodes / 200 groups / 400 routers.
/// Target: P95 &lt; 500 ms.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class TopologyGraphBenchmark
{
    private TopologyQueryService _svc = null!;
    private IMemoryCache         _cache = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();
        _cache = new MemoryCache(new MemoryCacheOptions());
        var signer = new CursorSigner(new byte[32]);
        _svc = new TopologyQueryService(BenchmarkDbSeeder.CreateDb(), _cache, signer);
    }

    [Benchmark]
    public async Task GetTopologyGraph_1000Nodes()
    {
        // Clear cache before each iteration so we measure DB round-trips
        _cache.Remove("topology:graph");
        _ = await _svc.GetTopologyGraphAsync(default);
    }

    [Benchmark]
    public async Task GetTopologyGraph_WithNodeIdFilter()
    {
        // Filter to a 50-node subgraph
        var filter = Enumerable.Range(1, 50).Select(i => $"node-{i:D4}").ToArray();
        _ = await _svc.GetTopologyGraphAsync(filter, default);
    }
}
```

- [ ] **Step 5: Create `NodeCursorPageBenchmark`**

Create `src/MSOSync.Benchmarks/NodeCursorPageBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using MediatR;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Metadata.NodeManagement;
using MSOSync.Metadata.Pagination;
using MSOSync.Metadata.Services;
using MSOSync.Security;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures cursor page retrieval at 1000 nodes.
/// Target: P95 &lt; 50 ms per page.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class NodeCursorPageBenchmark
{
    private NodeMetadataService _svc    = null!;
    private string?             _page5Cursor;
    private string?             _page20Cursor;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();

        var db     = BenchmarkDbSeeder.CreateDb();
        var cache  = new MemoryCache(new MemoryCacheOptions());
        var signer = new CursorSigner(new byte[32]);

        var protMock = new Mock<IDataProtector>();
        protMock.Setup(p => p.Protect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        protMock.Setup(p => p.Unprotect(It.IsAny<byte[]>())).Returns((byte[] b) => b);
        var dpMock = new Mock<IDataProtectionProvider>();
        dpMock.Setup(dp => dp.CreateProtector(It.IsAny<string>())).Returns(protMock.Object);

        _svc = new NodeMetadataService(db, cache, new Mock<IMediator>().Object,
            new NodeSecurityService(db, new BCryptPasswordHasher()), dpMock.Object, signer);

        // Pre-compute cursors for page 5 and page 20 (pageSize = 50)
        string? cursor = null;
        for (int page = 1; page <= 20; page++)
        {
            var result = await _svc.GetNodesCursorAsync(
                new NodeCursorFilter { PageSize = 50, Cursor = cursor }, default);
            if (page == 4)  _page5Cursor  = result.NextCursor;
            if (page == 19) _page20Cursor = result.NextCursor;
            cursor = result.NextCursor;
            if (!result.HasMore) break;
        }
    }

    [Benchmark]
    public async Task FirstPage()
        => _ = await _svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 50, Cursor = null }, default);

    [Benchmark]
    public async Task Page5()
        => _ = await _svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 50, Cursor = _page5Cursor }, default);

    [Benchmark]
    public async Task Page20()
        => _ = await _svc.GetNodesCursorAsync(
            new NodeCursorFilter { PageSize = 50, Cursor = _page20Cursor }, default);
}
```

- [ ] **Step 6: Create `BulkFanOutBenchmark`**

Create `src/MSOSync.Benchmarks/BulkFanOutBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using MSOSync.Routing;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures IBulkRoutingService.FanOutAsync at 1000 active nodes.
/// Target: P95 &lt; 100 ms for a single bulk insert vs N individual inserts baseline.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class BulkFanOutBenchmark
{
    private BulkRoutingService _svc = null!;
    private long _seq = 1;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();
        _svc = new BulkRoutingService(BenchmarkDbSeeder.CreateDb());
    }

    [Benchmark]
    public async Task FanOut_1000Nodes_SingleBulkInsert()
    {
        // Each benchmark iteration inserts to all eligible nodes.
        // We increment batchSequence to avoid PK conflicts.
        _ = await _svc.FanOutAsync(
            triggerId:     "trig-bench-01",
            channelId:     "ch-bench",
            batchSequence: Interlocked.Increment(ref _seq),
            rowCount:      100,
            byteCount:     4096L,
            tenantId:      BenchmarkDbSeeder.TenantId);
    }

    [GlobalCleanup]
    public async Task Cleanup()
    {
        // Remove inserted batch rows between benchmark runs to keep table small
        var db = BenchmarkDbSeeder.CreateDb();
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM [msosync].[sync_outgoing_batch] WHERE [tenant_id] = @p0",
            BenchmarkDbSeeder.TenantId);
    }
}
```

- [ ] **Step 7: Create `DashboardSummaryBenchmark`**

Create `src/MSOSync.Benchmarks/DashboardSummaryBenchmark.cs`:

```csharp
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Metadata.Dashboard;
using MSOSync.Metadata.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Tenancy;

namespace MSOSync.Benchmarks;

/// <summary>
/// Measures DashboardQueryService.GetSummaryAsync (cache miss) at 1000 nodes with mixed statuses.
/// Target: P95 &lt; 100 ms for the full summary computation.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(BenchmarkDotNet.Jobs.RuntimeMoniker.Net90)]
public class DashboardSummaryBenchmark
{
    private DashboardQueryService _svc    = null!;
    private DashboardSummaryCache _cache  = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        await BenchmarkDbSeeder.EnsureSeededAsync();

        _cache = new DashboardSummaryCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DashboardOptions { SummaryTtlSeconds = 30 }));

        var db        = BenchmarkDbSeeder.CreateDb();
        var auditRepo = new NullPlatformRepository<SyncAudit>();
        _svc = new DashboardQueryService(db, auditRepo, _cache);
    }

    [Benchmark]
    public async Task GetSummary_CacheMiss()
    {
        // Invalidate cache before each iteration to measure DB cost
        // DashboardSummaryCache does not expose an invalidate method, so we create a fresh one
        var freshCache = new DashboardSummaryCache(
            new MemoryCache(new MemoryCacheOptions()),
            Options.Create(new DashboardOptions { SummaryTtlSeconds = 30 }));
        var db        = BenchmarkDbSeeder.CreateDb();
        var svc       = new DashboardQueryService(db, new NullPlatformRepository<SyncAudit>(), freshCache);
        _ = await svc.GetSummaryAsync(default);
    }

    [Benchmark]
    public async Task GetSummary_CacheHit()
    {
        // Warm up once, then measure cache hit
        _ = await _svc.GetSummaryAsync(default);
    }
}

/// <summary>No-op IPlatformRepository for benchmarks (no audit rows needed).</summary>
internal sealed class NullPlatformRepository<T> : IPlatformRepository<T> where T : class
{
    private static readonly IQueryable<T> Empty = Enumerable.Empty<T>().AsQueryable();
    public IQueryable<T> QueryAll() => Empty;
}
```

- [ ] **Step 8: Create `Program.cs`**

Create `src/MSOSync.Benchmarks/Program.cs`:

```csharp
using BenchmarkDotNet.Running;
using MSOSync.Benchmarks;

// Run: dotnet run -c Release --project src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj
// To run a single benchmark: add -- --filter *TopologyGraph* after the command

var summary = BenchmarkRunner.Run(new[]
{
    typeof(TopologyGraphBenchmark),
    typeof(NodeCursorPageBenchmark),
    typeof(BulkFanOutBenchmark),
    typeof(DashboardSummaryBenchmark),
});

Console.WriteLine("Benchmarks complete. Results in BenchmarkDotNet.Artifacts/");
```

- [ ] **Step 9: Build the benchmarks project in Release mode**

```
dotnet build src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj -c Release
```

Expected: builds with 0 errors. Fix any compilation issues before proceeding.

- [ ] **Step 10: Run all four benchmarks**

```
dotnet run -c Release --project src/MSOSync.Benchmarks/MSOSync.Benchmarks.csproj
```

This will:
1. Spin up `MSOSync_Benchmarks` LocalDB database and seed 1000 nodes.
2. Run each benchmark with 3 warm-up iterations + 5 measured iterations (BenchmarkDotNet default).
3. Print results to console and save to `BenchmarkDotNet.Artifacts/results/`.

Expected targets:
- `TopologyGraphBenchmark.GetTopologyGraph_1000Nodes`: < 500 ms mean.
- `NodeCursorPageBenchmark.FirstPage` / `Page5` / `Page20`: < 50 ms mean each.
- `BulkFanOutBenchmark.FanOut_1000Nodes_SingleBulkInsert`: < 100 ms mean.
- `DashboardSummaryBenchmark.GetSummary_CacheMiss`: < 100 ms mean.

- [ ] **Step 11: Create the baseline results file**

Create `docs/superpowers/benchmarks/2D-4-baseline.md` and fill in the actual numbers from the benchmark run:

```markdown
# Phase 2D.4 — Baseline Benchmark Results

**Run date:** 2026-07-23
**Machine:** [fill in CPU + RAM]
**LocalDB version:** [fill in]
**Dataset:** 1000 nodes / 200 groups / 400 routers / 1 trigger / 400 trigger-router mappings

## Results

| Benchmark | Method | Mean | StdDev | Target |
|---|---|---|---|---|
| TopologyGraphBenchmark | GetTopologyGraph_1000Nodes | [fill] ms | [fill] ms | < 500 ms |
| TopologyGraphBenchmark | GetTopologyGraph_WithNodeIdFilter | [fill] ms | [fill] ms | — |
| NodeCursorPageBenchmark | FirstPage | [fill] ms | [fill] ms | < 50 ms |
| NodeCursorPageBenchmark | Page5 | [fill] ms | [fill] ms | < 50 ms |
| NodeCursorPageBenchmark | Page20 | [fill] ms | [fill] ms | < 50 ms |
| BulkFanOutBenchmark | FanOut_1000Nodes_SingleBulkInsert | [fill] ms | [fill] ms | < 100 ms |
| DashboardSummaryBenchmark | GetSummary_CacheMiss | [fill] ms | [fill] ms | < 100 ms |
| DashboardSummaryBenchmark | GetSummary_CacheHit | [fill] µs | [fill] µs | < 1 ms |

## Notes

[Add any deviations from targets, recommendations for further optimisation, or observations about index usage.]
```

After running the benchmarks, replace the `[fill]` placeholders with actual numbers from the `BenchmarkDotNet.Artifacts/` output.

- [ ] **Step 12: Commit**

```
git add src/MSOSync.Benchmarks/ \
        docs/superpowers/benchmarks/2D-4-baseline.md \
        MSOSync.sln
git commit -m "feat(2D.4-T5): add BenchmarkDotNet project with 4 benchmarks and 2D.4 baseline results"
```
