# Task 9: Unit Tests — MSOSync.EngineTests

**Part of:** [Epic 5 master plan](2026-06-22-epic5-event-capture.md)

**Files:**
- Create: `tests/MSOSync.EngineTests/MSOSync.EngineTests.csproj`
- Create: `tests/MSOSync.EngineTests/TestDbContext.cs`
- Create: `tests/MSOSync.EngineTests/FakeClock.cs`
- Create: `tests/MSOSync.EngineTests/SqlServerTriggerBuilderTests.cs`
- Create: `tests/MSOSync.EngineTests/BatchStateMachineTests.cs`
- Create: `tests/MSOSync.EngineTests/BatchCreatorTests.cs`
- Create: `tests/MSOSync.EngineTests/RetryProcessorTests.cs`
- Create: `tests/MSOSync.EngineTests/RoutingServiceTests.cs`
- Create: `tests/MSOSync.EngineTests/SyncEngineTests.cs`
- Modify: `MSOSync.sln`

**Interfaces:**
- Consumes: all Task 1–6 interfaces (SqlServerTriggerBuilder, BatchStateMachine, BatchCreator, RetryProcessor, RoutingService, SyncEngine)

---

- [ ] **Step 1: Create `MSOSync.EngineTests.csproj`**

```xml
<!-- tests/MSOSync.EngineTests/MSOSync.EngineTests.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit" />
    <PackageReference Include="xunit.runner.visualstudio">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Moq" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\MSOSync.Common\MSOSync.Common.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Persistence\MSOSync.Persistence.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Trigger\MSOSync.Trigger.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Event\MSOSync.Event.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Routing\MSOSync.Routing.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Batch\MSOSync.Batch.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Engine\MSOSync.Engine.csproj" />
    <ProjectReference Include="..\..\src\MSOSync.Metadata\MSOSync.Metadata.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add project to solution**

```pwsh
dotnet sln MSOSync.sln add tests/MSOSync.EngineTests/MSOSync.EngineTests.csproj
```

- [ ] **Step 3: Create `TestDbContext.cs`**

Same pattern as `MSOSync.MetadataTests/TestDbContext.cs` — SQLite, strips column types.

```csharp
// tests/MSOSync.EngineTests/TestDbContext.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence;

namespace MSOSync.EngineTests;

internal sealed class TestAppDbContext : AppDbContext
{
    public TestAppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
            foreach (var prop in entity.GetProperties())
                prop.SetColumnType(null);
    }
}

internal static class TestDbContext
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("DataSource=:memory:")
            .Options;
        var db = new TestAppDbContext(options);
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }
}
```

- [ ] **Step 4: Create `FakeClock.cs`**

```csharp
// tests/MSOSync.EngineTests/FakeClock.cs
using MSOSync.Common;

namespace MSOSync.EngineTests;

internal sealed class FakeClock : IClock
{
    public DateTime UtcNow { get; set; } = DateTime.UtcNow;

    public void Advance(TimeSpan span) => UtcNow += span;
}
```

- [ ] **Step 5: Create `SqlServerTriggerBuilderTests.cs`**

```csharp
// tests/MSOSync.EngineTests/SqlServerTriggerBuilderTests.cs
using FluentAssertions;
using MSOSync.Persistence.Entities;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class SqlServerTriggerBuilderTests
{
    private static SyncTrigger MakeTrigger(bool insert = true, bool update = true, bool delete = true) =>
        new()
        {
            TriggerId    = "t-orders",
            SourceTable  = "dbo.Orders",
            ChannelId    = "default",
            SyncOnInsert = insert,
            SyncOnUpdate = update,
            SyncOnDelete = delete
        };

    private readonly SqlServerTriggerBuilder _builder = new();

    [Fact]
    public void BuildDdl_ContainsCreateOrAlterTrigger()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("CREATE OR ALTER TRIGGER");
    }

    [Fact]
    public void BuildDdl_ContainsTableName()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("[dbo].[Orders]");
    }

    [Fact]
    public void BuildDdl_ContainsForJsonPath()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("FOR JSON PATH");
    }

    [Fact]
    public void BuildDdl_ContainsCurrentTransactionId()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("CURRENT_TRANSACTION_ID()");
    }

    [Fact]
    public void BuildDdl_EmbedsNodeIdAsLiteral()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("N'hub'");
    }

    [Fact]
    public void BuildDdl_InsertOnlyFlag_AfterClauseOnlyHasInsert()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(insert: true, update: false, delete: false), "hub");
        ddl.Should().Contain("AFTER INSERT");
        ddl.Should().NotContain("UPDATE");
        ddl.Should().NotContain("DELETE");
    }

    [Fact]
    public void BuildDdl_AllFlags_AfterClauseHasAllThree()
    {
        var ddl = _builder.BuildDdl(MakeTrigger(), "hub");
        ddl.Should().Contain("INSERT");
        ddl.Should().Contain("UPDATE");
        ddl.Should().Contain("DELETE");
    }

    [Fact]
    public void GetTriggerName_ReturnsPrefixedId()
    {
        _builder.GetTriggerName("t-orders").Should().Be("msosync__t-orders");
    }
}
```

- [ ] **Step 6: Create `BatchStateMachineTests.cs`**

```csharp
// tests/MSOSync.EngineTests/BatchStateMachineTests.cs
using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchStateMachineTests
{
    private static (BatchStateMachine Sm, AppDbContext Db) Create()
    {
        var db = TestDbContext.Create();
        return (new BatchStateMachine(db), db);
    }

    private static SyncOutgoingBatch MakeBatch(BatchStatus status)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1,
            NodeId        = "hub",
            ChannelId     = "default",
            Status        = (byte)status
        };
        return b;
    }

    [Theory]
    [InlineData(BatchStatus.New,   BatchStatus.Sent)]
    [InlineData(BatchStatus.Sent,  BatchStatus.Ok)]
    [InlineData(BatchStatus.Sent,  BatchStatus.Error)]
    [InlineData(BatchStatus.Error, BatchStatus.Retry)]
    [InlineData(BatchStatus.Retry, BatchStatus.Sent)]
    [InlineData(BatchStatus.Retry, BatchStatus.Error)]
    public void CanTransition_ValidPairs_ReturnsTrue(BatchStatus from, BatchStatus to)
    {
        var (sm, _) = Create();
        sm.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(BatchStatus.New,   BatchStatus.Ok)]
    [InlineData(BatchStatus.New,   BatchStatus.Error)]
    [InlineData(BatchStatus.Ok,    BatchStatus.Sent)]
    [InlineData(BatchStatus.Error, BatchStatus.Ok)]
    public void CanTransition_InvalidPairs_ReturnsFalse(BatchStatus from, BatchStatus to)
    {
        var (sm, _) = Create();
        sm.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public async Task TransitionAsync_ValidTransition_ReturnsTrue()
    {
        var (sm, db) = Create();
        var batch = MakeBatch(BatchStatus.New);
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var result = await sm.TransitionAsync(batch.BatchId, BatchStatus.New, BatchStatus.Sent);

        result.Should().BeTrue();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Sent);
    }

    [Fact]
    public async Task TransitionAsync_WrongCurrentStatus_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch = MakeBatch(BatchStatus.Error);
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var result = await sm.TransitionAsync(batch.BatchId, BatchStatus.New, BatchStatus.Sent);

        result.Should().BeFalse();
        var unchanged = await db.OutgoingBatches.FindAsync(batch.BatchId);
        unchanged!.Status.Should().Be((byte)BatchStatus.Error);
    }

    [Fact]
    public async Task TransitionAsync_InvalidTransitionPair_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch = MakeBatch(BatchStatus.New);
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var result = await sm.TransitionAsync(batch.BatchId, BatchStatus.New, BatchStatus.Ok);

        result.Should().BeFalse();
    }
}
```

- [ ] **Step 7: Create `BatchCreatorTests.cs`**

```csharp
// tests/MSOSync.EngineTests/BatchCreatorTests.cs
using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchCreatorTests
{
    private static BatchCreator CreateService(out AppDbContext db, out FakeClock clock)
    {
        db = TestDbContext.Create();
        clock = new FakeClock();
        db.Channels.Add(new SyncChannel
        {
            ChannelId = "default", Priority = 1,
            BatchSize = 1000, MaxBatchToSend = 100, MaxDataSize = 1048576L
        });
        db.SaveChanges();
        return new BatchCreator(db, clock);
    }

    private static SyncDataEvent MakeEvent(long id, string triggerId = "t1",
        string channelId = "default", long txId = 1, string? rowData = null) =>
        new()
        {
            EventId      = id,
            TriggerId    = triggerId,
            SourceNodeId = "hub",
            ChannelId    = channelId,
            EventType    = 'I',
            TableName    = "dbo.T",
            TransactionId = txId,
            RowData      = rowData ?? "{}",
            CreateTime   = DateTime.UtcNow,
            IsProcessed  = false
        };

    [Fact]
    public async Task CreateBatchesAsync_EmptyEvents_ReturnsEmpty()
    {
        var svc = CreateService(out _, out _);
        var result = await svc.CreateBatchesAsync([], new Dictionary<long, IReadOnlyList<string>>());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBatchesAsync_SingleEvent_CreatesBatch()
    {
        var svc = CreateService(out var db, out _);
        var evt = MakeEvent(1);
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        var routes = new Dictionary<long, IReadOnlyList<string>> { [1L] = ["node-b"] };
        var result = await svc.CreateBatchesAsync([evt], routes);

        result.Should().HaveCount(1);
        result[0].NodeId.Should().Be("node-b");
        result[0].ChannelId.Should().Be("default");
        result[0].RowCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateBatchesAsync_MarksEventsProcessed()
    {
        var svc = CreateService(out var db, out _);
        var evt = MakeEvent(1);
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var routes = new Dictionary<long, IReadOnlyList<string>> { [1L] = ["node-b"] };
        await svc.CreateBatchesAsync([evt], routes);

        var refreshed = await db.DataEvents.FindAsync(1L);
        refreshed!.IsProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBatchesAsync_TransactionBoundaryNeverSplit()
    {
        var svc = CreateService(out var db, out _);
        // Channel allows max 2 rows per batch, but tx has 3 rows — stays together
        db.Channels.First().MaxBatchToSend = 2;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var events = Enumerable.Range(1, 3)
            .Select(i => MakeEvent(i, txId: 1))  // same transaction
            .ToList();
        db.DataEvents.AddRange(events);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var routes = events.ToDictionary(
            e => e.EventId,
            _ => (IReadOnlyList<string>)["node-b"]);

        var result = await svc.CreateBatchesAsync(events, routes);

        result.Should().HaveCount(1);
        result[0].RowCount.Should().Be(3);
    }

    [Fact]
    public async Task CreateBatchesAsync_DifferentTargetNodes_CreatesSeparateBatches()
    {
        var svc = CreateService(out var db, out _);
        var e1 = MakeEvent(1, txId: 1);
        var e2 = MakeEvent(2, txId: 2);
        db.DataEvents.AddRange(e1, e2);
        await db.SaveChangesAsync();

        var routes = new Dictionary<long, IReadOnlyList<string>>
        {
            [1L] = ["node-a"],
            [2L] = ["node-b"]
        };

        var result = await svc.CreateBatchesAsync([e1, e2], routes);

        result.Should().HaveCount(2);
        result.Select(b => b.NodeId).Should().Contain("node-a").And.Contain("node-b");
    }
}
```

- [ ] **Step 8: Create `RetryProcessorTests.cs`**

```csharp
// tests/MSOSync.EngineTests/RetryProcessorTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MSOSync.Batch;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class RetryProcessorTests
{
    private static (RetryProcessor Processor, AppDbContext Db, FakeClock Clock) Create()
    {
        var db    = TestDbContext.Create();
        var clock = new FakeClock();
        var sm    = new BatchStateMachine(db);
        var proc  = new RetryProcessor(db, sm, clock, NullLogger<RetryProcessor>.Instance);
        return (proc, db, clock);
    }

    private static SyncOutgoingBatch MakeErrorBatch(AppDbContext db, int retryCount = 0, DateTime? nextRetry = null)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = "hub", ChannelId = "default",
            Status = (byte)BatchStatus.Error, RetryCount = retryCount,
            NextRetryTime = nextRetry
        };
        db.OutgoingBatches.Add(b);
        db.SaveChanges();
        return b;
    }

    [Fact]
    public async Task ProcessAsync_NoEligibleBatches_ReturnsZero()
    {
        var (proc, _, _) = Create();
        var count = await proc.ProcessAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_EligibleErrorBatch_TransitionsToRetry()
    {
        var (proc, db, clock) = Create();
        var batch = MakeErrorBatch(db);

        var count = await proc.ProcessAsync();

        count.Should().Be(1);
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Retry);
    }

    [Fact]
    public async Task ProcessAsync_FutureNextRetryTime_Skips()
    {
        var (proc, db, clock) = Create();
        MakeErrorBatch(db, nextRetry: clock.UtcNow.AddHours(1));

        var count = await proc.ProcessAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_FirstRetry_SetsDelay5Minutes()
    {
        var (proc, db, clock) = Create();
        var batch = MakeErrorBatch(db, retryCount: 0);

        await proc.ProcessAsync();

        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        var expectedDelay = TimeSpan.FromMinutes(5); // 2^0 * 5
        (updated!.NextRetryTime!.Value - clock.UtcNow).Should()
            .BeCloseTo(expectedDelay, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_SecondRetry_SetsDelay10Minutes()
    {
        var (proc, db, clock) = Create();
        var batch = MakeErrorBatch(db, retryCount: 1);

        await proc.ProcessAsync();

        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        var expectedDelay = TimeSpan.FromMinutes(10); // 2^1 * 5
        (updated!.NextRetryTime!.Value - clock.UtcNow).Should()
            .BeCloseTo(expectedDelay, TimeSpan.FromSeconds(5));
    }
}
```

- [ ] **Step 9: Create `RoutingServiceTests.cs`**

```csharp
// tests/MSOSync.EngineTests/RoutingServiceTests.cs
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Moq;
using MSOSync.Metadata.Events;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class RoutingServiceTests
{
    private static (RoutingService Svc, AppDbContext Db, IMemoryCache Cache, RouteCacheState State) Create()
    {
        var db    = TestDbContext.Create();
        var cache = new MemoryCache(new MemoryCacheOptions());
        var state = new RouteCacheState();
        var svc   = new RoutingService(db, cache, state);
        return (svc, db, cache, state);
    }

    private static void SeedRoute(AppDbContext db, string triggerId, string routerId, string targetGroupId, string nodeId)
    {
        if (!db.NodeGroups.Any(g => g.GroupId == targetGroupId))
            db.NodeGroups.Add(new SyncNodeGroup { GroupId = targetGroupId, GroupName = targetGroupId });
        if (!db.Nodes.Any(n => n.NodeId == nodeId))
            db.Nodes.Add(new SyncNode { NodeId = nodeId, GroupId = targetGroupId, SyncUrl = "http://x", Status = "ONLINE", SyncEnabled = true });
        if (!db.Routers.Any(r => r.RouterId == routerId))
            db.Routers.Add(new SyncRouter { RouterId = routerId, SourceNodeGroup = "src", TargetNodeGroup = targetGroupId, Enabled = true });
        if (!db.TriggerRouters.Any(tr => tr.TriggerId == triggerId))
            db.TriggerRouters.Add(new SyncTriggerRouter { TriggerId = triggerId, RouterId = routerId, Enabled = true });
        db.SaveChanges();
    }

    [Fact]
    public async Task ResolveAsync_SeededRoute_ReturnsTargetNodeId()
    {
        var (svc, db, _, _) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        var result = await svc.ResolveAsync("t1");

        result.Should().ContainSingle().Which.Should().Be("node-a");
    }

    [Fact]
    public async Task ResolveAsync_CacheHit_SecondCallReturnsSameResult()
    {
        var (svc, db, _, _) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        var first  = await svc.ResolveAsync("t1");
        var second = await svc.ResolveAsync("t1"); // from cache

        second.Should().BeEquivalentTo(first);
    }

    [Fact]
    public async Task ResolveAsync_TriggerMetadataChanged_EvictsSpecificKey()
    {
        var (svc, db, cache, _) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        await svc.ResolveAsync("t1");
        cache.TryGetValue("routing:trigger:t1", out _).Should().BeTrue();

        await svc.Handle(new TriggerMetadataChangedEvent("t1", "UPDATE"), CancellationToken.None);

        cache.TryGetValue("routing:trigger:t1", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ResolveAsync_RouterMetadataChanged_EvictsAllRoutes()
    {
        var (svc, db, cache, state) = Create();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");
        SeedRoute(db, "t2", "r1", "grp-a", "node-a");

        await svc.ResolveAsync("t1");
        await svc.ResolveAsync("t2");

        await svc.Handle(new RouterMetadataChangedEvent("r1", "UPDATE"), CancellationToken.None);

        // Both keys evicted — resolving again returns fresh DB result
        var result = await svc.ResolveAsync("t1");
        result.Should().ContainSingle().Which.Should().Be("node-a");
    }

    [Fact]
    public async Task ResolveAsync_TtlExpired_RefetchesDb()
    {
        var (svc, db, _, state) = Create();
        var clock = new FakeClock();
        SeedRoute(db, "t1", "r1", "grp-a", "node-a");

        await svc.ResolveAsync("t1");

        // Simulate TTL expiration by invalidating via state
        state.InvalidateAll();

        var result = await svc.ResolveAsync("t1");
        result.Should().ContainSingle().Which.Should().Be("node-a");
    }
}
```

- [ ] **Step 10: Create `SyncEngineTests.cs`**

```csharp
// tests/MSOSync.EngineTests/SyncEngineTests.cs
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Batch;
using MSOSync.Engine;
using MSOSync.Event;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Trigger;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class SyncEngineTests
{
    private static SyncEngine CreateEngine(
        IEventReader? reader = null,
        IRoutingService? routing = null,
        IBatchCreator? creator = null,
        ITransportService? transport = null,
        IMediator? mediator = null)
    {
        var driftMock     = new Mock<ITriggerDriftDetector>();
        var readerMock    = reader    ?? Mock.Of<IEventReader>(r => r.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<SyncDataEvent>>([]));
        var routingMock   = routing   ?? Mock.Of<IRoutingService>(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<string>>([]));
        var creatorMock   = creator   ?? Mock.Of<IBatchCreator>(c => c.CreateBatchesAsync(It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<SyncOutgoingBatch>>([]));
        var transportMock = transport ?? Mock.Of<ITransportService>();
        var mediatorMock  = mediator  ?? Mock.Of<IMediator>();
        var clock         = new FakeClock();

        return new SyncEngine(driftMock.Object, readerMock, routingMock, creatorMock,
            transportMock, mediatorMock, clock, NullLogger<SyncEngine>.Instance);
    }

    [Fact]
    public async Task RunAsync_NoEvents_NeverCallsRouteCreateTransport()
    {
        var routingMock   = new Mock<IRoutingService>();
        var creatorMock   = new Mock<IBatchCreator>();
        var transportMock = new Mock<ITransportService>();

        var engine = CreateEngine(routing: routingMock.Object, creator: creatorMock.Object, transport: transportMock.Object);
        await engine.RunAsync();

        routingMock.Verify(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        creatorMock.Verify(c => c.CreateBatchesAsync(It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(), It.IsAny<CancellationToken>()), Times.Never);
        transportMock.Verify(t => t.SendBatchAsync(It.IsAny<SyncOutgoingBatch>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WithOneEvent_CallsRouteAndCreateAndTransport()
    {
        var evt = new SyncDataEvent { EventId = 1, TriggerId = "t1", SourceNodeId = "hub", ChannelId = "default", EventType = 'I', TableName = "dbo.T", CreateTime = DateTime.UtcNow };
        var batch = new SyncOutgoingBatch { BatchId = 1, BatchSequence = 1, NodeId = "node-b", ChannelId = "default", Status = (byte)BatchStatus.New };

        var readerMock    = new Mock<IEventReader>();
        var routingMock   = new Mock<IRoutingService>();
        var creatorMock   = new Mock<IBatchCreator>();
        var transportMock = new Mock<ITransportService>();

        readerMock.Setup(r => r.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([evt]);
        routingMock.Setup(r => r.ResolveAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(["node-b"]);
        creatorMock.Setup(c => c.CreateBatchesAsync(It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([batch]);

        var engine = CreateEngine(readerMock.Object, routingMock.Object, creatorMock.Object, transportMock.Object);
        await engine.RunAsync();

        routingMock.Verify(r => r.ResolveAsync("t1", It.IsAny<CancellationToken>()), Times.Once);
        creatorMock.Verify(c => c.CreateBatchesAsync(It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(), It.IsAny<CancellationToken>()), Times.Once);
        transportMock.Verify(t => t.SendBatchAsync(batch, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithTwoBatches_TransportCalledTwice()
    {
        var evt = new SyncDataEvent { EventId = 1, TriggerId = "t1", SourceNodeId = "hub", ChannelId = "default", EventType = 'I', TableName = "dbo.T", CreateTime = DateTime.UtcNow };
        var b1  = new SyncOutgoingBatch { BatchId = 1, BatchSequence = 1, NodeId = "a", ChannelId = "default", Status = 0 };
        var b2  = new SyncOutgoingBatch { BatchId = 2, BatchSequence = 2, NodeId = "b", ChannelId = "default", Status = 0 };

        var readerMock    = new Mock<IEventReader>();
        var creatorMock   = new Mock<IBatchCreator>();
        var transportMock = new Mock<ITransportService>();

        readerMock.Setup(r => r.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([evt]);
        creatorMock.Setup(c => c.CreateBatchesAsync(It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([b1, b2]);

        var engine = CreateEngine(reader: readerMock.Object, creator: creatorMock.Object, transport: transportMock.Object);
        await engine.RunAsync();

        transportMock.Verify(t => t.SendBatchAsync(It.IsAny<SyncOutgoingBatch>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task RunAsync_PublishesSyncCycleCompletedEvent()
    {
        var mediatorMock = new Mock<IMediator>();
        var engine = CreateEngine(mediator: mediatorMock.Object);
        await engine.RunAsync();

        mediatorMock.Verify(m => m.Publish(It.IsAny<SyncCycleCompletedEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }
}
```

- [ ] **Step 11: Run all unit tests**

```pwsh
dotnet test tests/MSOSync.EngineTests -c Debug --warnaserror
```

Expected output: all tests pass. Example:
```
Passed! - Failed: 0, Passed: 26, Skipped: 0, Total: 26
```

- [ ] **Step 12: Commit**

```pwsh
git add tests/MSOSync.EngineTests/MSOSync.EngineTests.csproj `
        tests/MSOSync.EngineTests/TestDbContext.cs `
        tests/MSOSync.EngineTests/FakeClock.cs `
        tests/MSOSync.EngineTests/SqlServerTriggerBuilderTests.cs `
        tests/MSOSync.EngineTests/BatchStateMachineTests.cs `
        tests/MSOSync.EngineTests/BatchCreatorTests.cs `
        tests/MSOSync.EngineTests/RetryProcessorTests.cs `
        tests/MSOSync.EngineTests/RoutingServiceTests.cs `
        tests/MSOSync.EngineTests/SyncEngineTests.cs `
        MSOSync.sln
git commit -m "test(engine): add MSOSync.EngineTests — 26 unit tests for Trigger, Batch, Routing, Engine"
```
