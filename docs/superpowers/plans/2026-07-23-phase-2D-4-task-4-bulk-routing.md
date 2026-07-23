# Task 4: IBulkRoutingService + Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Introduce `IBulkRoutingService` in `MSOSync.Routing` with a `FanOutAsync` method that bulk-inserts `SyncOutgoingBatch` rows via a single `INSERT INTO … SELECT … OUTPUT` SQL statement. Register it as scoped. Write unit/integration tests.

**Prerequisites:** T1 (M038 indexes) is recommended — `IX_sync_node_lifecycle_state` covers the `WHERE lifecycle_state = 3` predicate inside the bulk insert. The code works without it.

**No dependency on T2 or T3.** T4 is independent.

## Files

- Create: `src/MSOSync.Routing/IBulkRoutingService.cs`
- Create: `src/MSOSync.Routing/BulkRoutingService.cs`
- Modify: `src/MSOSync.Routing/RoutingServiceExtensions.cs`
- Create: `tests/MSOSync.MetadataTests/Scale/BulkRoutingTests.cs`

## Interfaces

**Produces (consumed by T5 benchmarks):**

```csharp
// IBulkRoutingService:
Task<IReadOnlyList<long>> FanOutAsync(
    string triggerId, string channelId, long batchSequence,
    int rowCount, long byteCount, Guid tenantId,
    CancellationToken ct = default);
```

## Context

`RoutingService` (in `MSOSync.Routing`) resolves target node IDs and caches them. The batch pipeline then loops and inserts one `SyncOutgoingBatch` per node. At 1000 nodes, this is 1000 round-trips.

`BulkRoutingService` bypasses the loop entirely with a single SQL statement:

```sql
INSERT INTO [msosync].[sync_outgoing_batch]
    ([batch_sequence], [node_id], [channel_id], [status],
     [row_count], [byte_count], [retry_count], [create_time], [tenant_id])
SELECT
    @batchSequence, n.[node_id], @channelId, 0,
    @rowCount, @byteCount, 0, SYSUTCDATETIME(), @tenantId
FROM [msosync].[sync_node] n
INNER JOIN [msosync].[sync_trigger_router] tr
    ON tr.[trigger_id] = @triggerId AND tr.[enabled] = 1
INNER JOIN [msosync].[sync_router] r
    ON r.[router_id] = tr.[router_id]
    AND r.[enabled] = 1
    AND r.[target_node_group] = n.[group_id]
WHERE n.[lifecycle_state] = 3
  AND n.[maintenance_mode] = 0
  AND n.[tenant_id] = @tenantId
OUTPUT INSERTED.[batch_id];
```

`AppDbContext` is not thread-safe; `BulkRoutingService` is scoped and never uses `Task.WhenAll` or parallel execution.

The unit tests use SQLite in-memory (same pattern as `BatchErrorQueryServiceTests`). Because `ExecuteSqlRawAsync` with `OUTPUT` is SQL Server-specific and not supported by SQLite, unit tests use a **mock** or verify behaviour via `db.OutgoingBatches.CountAsync`. The integration tests use LocalDB.

## Steps

- [ ] **Step 1: Write failing tests**

Create `tests/MSOSync.MetadataTests/Scale/BulkRoutingTests.cs`:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using Xunit;

namespace MSOSync.MetadataTests.Scale;

/// <summary>
/// Unit tests for BulkRoutingService.
/// These tests use a SQL Server LocalDB database because ExecuteSqlRawAsync with OUTPUT
/// is not supported by SQLite in-memory.
/// </summary>
public sealed class BulkRoutingTests : IAsyncLifetime
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSync_BulkRouting_Test;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private AppDbContext _db = null!;
    private static readonly Guid TenantId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    public async Task InitializeAsync()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        _db = new AppDbContext(opts);
        await _db.Database.MigrateAsync();

        // Seed: one node group, one router, one trigger-router, 3 active nodes, 1 inactive
        _db.NodeGroups.Add(new SyncNodeGroup { GroupId = "g-target", TenantId = TenantId });
        await _db.SaveChangesAsync();

        _db.Routers.Add(new SyncRouter
        {
            RouterId        = "r-test",
            SourceNodeGroup = "g-source",
            TargetNodeGroup = "g-target",
            Enabled         = true,
            TenantId        = TenantId
        });
        await _db.SaveChangesAsync();

        _db.Triggers.Add(new SyncTrigger
        {
            TriggerId   = "trig-test",
            SourceTable = "dbo.Orders",
            ChannelId   = "ch-default",
            TenantId    = TenantId
        });
        await _db.SaveChangesAsync();

        _db.TriggerRouters.Add(new SyncTriggerRouter
        {
            TriggerId = "trig-test",
            RouterId  = "r-test",
            Enabled   = true,
            TenantId  = TenantId
        });
        await _db.SaveChangesAsync();

        // 3 active nodes in target group
        for (int i = 1; i <= 3; i++)
            _db.Nodes.Add(new SyncNode
            {
                NodeId         = $"node-{i:D3}",
                GroupId        = "g-target",
                SyncUrl        = $"http://node-{i}",
                LifecycleState = NodeLifecycleState.Active,
                MaintenanceMode = false,
                TenantId       = TenantId
            });

        // 1 disabled node (should NOT be included in fan-out)
        _db.Nodes.Add(new SyncNode
        {
            NodeId         = "node-disabled",
            GroupId        = "g-target",
            SyncUrl        = "http://disabled",
            LifecycleState = NodeLifecycleState.Disabled,
            MaintenanceMode = false,
            TenantId       = TenantId
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task FanOut_3EligibleNodes_Inserts3Batches()
    {
        var svc = new BulkRoutingService(_db);

        var batchIds = await svc.FanOutAsync(
            triggerId: "trig-test",
            channelId: "ch-default",
            batchSequence: 1L,
            rowCount: 10,
            byteCount: 512L,
            tenantId: TenantId);

        batchIds.Should().HaveCount(3);
        var count = await _db.OutgoingBatches.AsNoTracking()
            .CountAsync(b => b.TenantId == TenantId);
        count.Should().Be(3);
    }

    [Fact]
    public async Task FanOut_DisabledNodeExcluded_NotInserted()
    {
        var svc = new BulkRoutingService(_db);

        await svc.FanOutAsync("trig-test", "ch-default", 2L, 5, 256L, TenantId);

        var nodeIds = await _db.OutgoingBatches.AsNoTracking()
            .Where(b => b.TenantId == TenantId)
            .Select(b => b.NodeId)
            .ToListAsync();

        nodeIds.Should().NotContain("node-disabled");
    }

    [Fact]
    public async Task FanOut_NoEligibleNodes_ReturnsEmptyList()
    {
        // Deactivate all nodes
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_node] SET [lifecycle_state] = 7 WHERE [tenant_id] = @p0",
            TenantId);  // 7 = Disabled enum value

        var svc = new BulkRoutingService(_db);
        var batchIds = await svc.FanOutAsync(
            "trig-test", "ch-default", 3L, 5, 100L, TenantId);

        batchIds.Should().BeEmpty();
    }

    [Fact]
    public async Task FanOut_PartiallyDisabledRouter_OnlyInsertsForEnabledRouters()
    {
        // Disable the trigger-router
        await _db.Database.ExecuteSqlRawAsync(
            "UPDATE [msosync].[sync_trigger_router] SET [enabled] = 0 WHERE [trigger_id] = 'trig-test'");

        var svc = new BulkRoutingService(_db);
        var batchIds = await svc.FanOutAsync(
            "trig-test", "ch-default", 4L, 1, 50L, TenantId);

        batchIds.Should().BeEmpty();
    }

    [Fact]
    public async Task FanOut_ReturnedBatchIds_MatchInsertedRows()
    {
        var svc = new BulkRoutingService(_db);

        var batchIds = await svc.FanOutAsync(
            "trig-test", "ch-default", 5L, 10, 1024L, TenantId);

        // All returned IDs should exist in the outgoing batches table
        var existing = await _db.OutgoingBatches.AsNoTracking()
            .Where(b => batchIds.Contains(b.BatchId))
            .Select(b => b.BatchId)
            .ToListAsync();

        existing.Should().BeEquivalentTo(batchIds);
    }

    public async Task DisposeAsync()
    {
        await _db.Database.ExecuteSqlRawAsync(
            "ALTER DATABASE [MSOSync_BulkRouting_Test] SET SINGLE_USER WITH ROLLBACK IMMEDIATE");
        await _db.Database.EnsureDeletedAsync();
        await _db.DisposeAsync();
    }
}
```

- [ ] **Step 2: Run tests — confirm compile failures**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj \
  --filter "FullyQualifiedName~BulkRoutingTests" -v normal
```

Expected: `MSOSync.Routing.IBulkRoutingService` and `BulkRoutingService` not found.

- [ ] **Step 3: Create `IBulkRoutingService`**

Create `src/MSOSync.Routing/IBulkRoutingService.cs`:

```csharp
namespace MSOSync.Routing;

/// <summary>
/// Resolves target nodes for a trigger and bulk-inserts one SyncOutgoingBatch row
/// per eligible node in a single SQL round-trip using INSERT … SELECT … OUTPUT.
/// </summary>
public interface IBulkRoutingService
{
    /// <summary>
    /// Inserts one outgoing batch row per eligible target node for the given trigger.
    /// Returns the list of <c>batch_id</c> identity values assigned by SQL Server.
    /// Returns an empty list when no eligible nodes are found.
    /// </summary>
    /// <param name="triggerId">The trigger whose router-node resolution determines target nodes.</param>
    /// <param name="channelId">The channel to record on each inserted batch row.</param>
    /// <param name="batchSequence">The batch sequence number shared across all inserted rows.</param>
    /// <param name="rowCount">Data row count to store on each batch row.</param>
    /// <param name="byteCount">Compressed byte count to store on each batch row.</param>
    /// <param name="tenantId">The tenant scope for both the node lookup and the insert.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<IReadOnlyList<long>> FanOutAsync(
        string            triggerId,
        string            channelId,
        long              batchSequence,
        int               rowCount,
        long              byteCount,
        Guid              tenantId,
        CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `BulkRoutingService`**

Create `src/MSOSync.Routing/BulkRoutingService.cs`:

```csharp
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.Routing;

/// <summary>
/// Implements <see cref="IBulkRoutingService"/> using a single parameterised
/// <c>INSERT INTO … SELECT … OUTPUT</c> SQL statement.
/// Registered as <b>scoped</b>. Do NOT use <c>Task.WhenAll</c> with the shared
/// <see cref="AppDbContext"/> — all operations on this context must be sequential.
/// </summary>
public sealed class BulkRoutingService(AppDbContext db) : IBulkRoutingService
{
    // NodeLifecycleState.Active = 3
    private const byte ActiveState = 3;

    private const string FanOutSql = """
        INSERT INTO [msosync].[sync_outgoing_batch]
            ([batch_sequence], [node_id], [channel_id], [status],
             [row_count], [byte_count], [retry_count], [create_time], [tenant_id])
        SELECT
            @batchSequence,
            n.[node_id],
            @channelId,
            0,
            @rowCount,
            @byteCount,
            0,
            SYSUTCDATETIME(),
            @tenantId
        FROM [msosync].[sync_node] n
        INNER JOIN [msosync].[sync_trigger_router] tr
            ON tr.[trigger_id] = @triggerId
            AND tr.[enabled]   = 1
            AND tr.[tenant_id] = @tenantId
        INNER JOIN [msosync].[sync_router] r
            ON r.[router_id]        = tr.[router_id]
            AND r.[enabled]         = 1
            AND r.[target_node_group] = n.[group_id]
            AND r.[tenant_id]       = @tenantId
        WHERE n.[lifecycle_state]   = @activeState
          AND n.[maintenance_mode]  = 0
          AND n.[tenant_id]         = @tenantId
        OUTPUT INSERTED.[batch_id];
        """;

    public async Task<IReadOnlyList<long>> FanOutAsync(
        string            triggerId,
        string            channelId,
        long              batchSequence,
        int               rowCount,
        long              byteCount,
        Guid              tenantId,
        CancellationToken ct = default)
    {
        var batchIds = new List<long>();

        // EF Core exposes the underlying connection for raw ADO.NET reads with OUTPUT.
        // We use FormattableString overload via EF's SqlQueryRaw to get the inserted IDs.
        // SqlQueryRaw<T> requires a scalar or keyless entity. Since batch_id is a bigint,
        // we use the SqlQueryRaw<long> path available in EF Core 8+.

        var results = await db.Database
            .SqlQueryRaw<long>(
                FanOutSql,
                new SqlParameter("@triggerId",     triggerId),
                new SqlParameter("@channelId",     channelId),
                new SqlParameter("@batchSequence", batchSequence),
                new SqlParameter("@rowCount",      rowCount),
                new SqlParameter("@byteCount",     byteCount),
                new SqlParameter("@tenantId",      tenantId),
                new SqlParameter("@activeState",   ActiveState))
            .ToListAsync(ct);

        return results.AsReadOnly();
    }
}
```

**Note on `db.Database.SqlQueryRaw<long>`:** EF Core 8 introduced `Database.SqlQueryRaw<T>` for scalar/primitive queries. If the project is on EF Core 7 (it is on 9 per architecture spec), this is available. The return type from `OUTPUT INSERTED.[batch_id]` is a scalar column named `batch_id` (bigint). EF Core 9 maps `SqlQueryRaw<long>` directly to this scalar.

If for any reason `SqlQueryRaw<long>` does not work (EF may require a keyless entity for complex shapes), the fallback is to use raw ADO.NET:

```csharp
// Fallback via raw ADO.NET (use if SqlQueryRaw<long> doesn't compile):
var conn = db.Database.GetDbConnection();
await conn.OpenAsync(ct);
await using var cmd = conn.CreateCommand();
cmd.CommandText = FanOutSql;
cmd.Parameters.Add(new SqlParameter("@triggerId",     triggerId));
cmd.Parameters.Add(new SqlParameter("@channelId",     channelId));
cmd.Parameters.Add(new SqlParameter("@batchSequence", batchSequence));
cmd.Parameters.Add(new SqlParameter("@rowCount",      rowCount));
cmd.Parameters.Add(new SqlParameter("@byteCount",     byteCount));
cmd.Parameters.Add(new SqlParameter("@tenantId",      tenantId));
cmd.Parameters.Add(new SqlParameter("@activeState",   ActiveState));

await using var reader = await cmd.ExecuteReaderAsync(ct);
while (await reader.ReadAsync(ct))
    batchIds.Add(reader.GetInt64(0));

return batchIds.AsReadOnly();
```

Use the `SqlQueryRaw<long>` path first. If EF throws a mapping error, switch to ADO.NET fallback.

- [ ] **Step 5: Register `IBulkRoutingService` in DI**

Open `src/MSOSync.Routing/RoutingServiceExtensions.cs`. Add the scoped registration after the `IRoutingService` line:

```csharp
public static IServiceCollection AddRoutingServices(this IServiceCollection services)
{
    services.AddMemoryCache();
    services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<RoutingService>());
    services.AddSingleton<RouteCacheState>();
    services.AddScoped<IRoutingService, RoutingService>();
    services.AddScoped<IBulkRoutingService, BulkRoutingService>();  // NEW
    return services;
}
```

- [ ] **Step 6: Run the bulk routing tests**

```
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj \
  --filter "FullyQualifiedName~BulkRoutingTests" -v normal
```

Expected: `5 passed`. If `SqlQueryRaw<long>` fails, apply the ADO.NET fallback in Step 4 and re-run.

- [ ] **Step 7: Verify no regressions in EngineTests (routing)**

```
dotnet test tests/MSOSync.EngineTests/MSOSync.EngineTests.csproj \
  --filter "FullyQualifiedName~RoutingServiceTests" -v normal
```

Expected: all pass (we did not change `IRoutingService` or `RoutingService`).

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Routing/IBulkRoutingService.cs \
        src/MSOSync.Routing/BulkRoutingService.cs \
        src/MSOSync.Routing/RoutingServiceExtensions.cs \
        tests/MSOSync.MetadataTests/Scale/BulkRoutingTests.cs
git commit -m "feat(2D.4-T4): add IBulkRoutingService with single INSERT-SELECT-OUTPUT fan-out and integration tests"
```
