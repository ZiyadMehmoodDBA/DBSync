# Task 3 — ReplayWorker

**Files:**
- Create: `src/MSOSync.Scheduler/Workers/ReplayWorker.cs`
- Modify: `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs` (add `AddHostedService<ReplayWorker>()`)
- Modify: `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj` (no change needed — already references MSOSync.Batch, MSOSync.Routing)
- Test: `tests/MSOSync.SchedulerTests/ReplayWorkerTests.cs`

**Interfaces:**
- Consumes from Task 1: `SyncReplayRequest`, `SyncReplayItem`, `ReplayItemStatus`, `OperationType.BatchReplay`
- Consumes from Task 2: `ReplayOptions`, `IReplayOperationService` (for cancel side-effects only)
- Consumes: `IWorkerStatusRegistry`, `IBatchCreator`, `IRoutingService`, `INodeMetadataService`, `IOperationService`, `AppDbContext`, `IClock`
- Produces: `ReplayWorker` registered as `IHostedService`

---

- [ ] **Step 1: Write failing tests for `ReplayWorker`**

```csharp
// tests/MSOSync.SchedulerTests/ReplayWorkerTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;
using MSOSync.Scheduler.Workers;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class ReplayWorkerTests
{
    private readonly Mock<INodeMetadataService> _nodeMeta = new();
    private readonly Mock<IOperationService>    _ops      = new();
    private readonly Mock<IBatchCreator>        _batchCreator = new();
    private readonly Mock<IRoutingService>      _routing  = new();
    private readonly Mock<IWorkerStatusRegistry> _registry = new();
    private readonly DbContextOptions<AppDbContext> _dbOpts =
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    private AppDbContext NewDb() => new(_dbOpts);

    private static NodeDto ActiveNode(string id = "n1") => new(
        id, "g", "http://n1", NodeLifecycleState.Active,
        null, null, 30, true, TransportMode.Push, ConnectivityStatus.Reachable,
        false, null, null, null, null, false, null);

    private ReplayWorker BuildWorker()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => NewDb());
        services.AddScoped(_ => _nodeMeta.Object);
        services.AddScoped(_ => _ops.Object);
        services.AddScoped(_ => _batchCreator.Object);
        services.AddScoped(_ => _routing.Object);
        services.AddScoped<IClock>(_ => new FakeClock());

        var sp   = services.BuildServiceProvider();
        var opts = Options.Create(new ReplayOptions { WorkerIntervalSeconds = 10, ItemPageSize = 50, MaxConcurrentOperations = 5 });

        return new ReplayWorker(
            sp.GetRequiredService<IServiceScopeFactory>(),
            opts, _registry.Object,
            NullLogger<ReplayWorker>.Instance);
    }

    private static SyncOperation MakeOperation(Guid id, string status = "Pending")
        => new()
        {
            OperationId = id, OperationType = "BatchReplay",
            Status = status, Source = "User", StartedAt = DateTime.UtcNow
        };

    private static SyncReplayRequest MakeRequest(Guid opId, string mode = "FailedDelivery")
        => new()
        {
            ReplayId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ReplayMode = mode,
            FromTime = DateTime.UtcNow.AddDays(-1),
            ToTime   = DateTime.UtcNow,
        };

    private static SyncReplayItem MakeItem(Guid opId, long? sourceBatchId = 42, string status = "Pending")
        => new()
        {
            ItemId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ChannelId = "ch1",
            EventCount = 5, Status = status,
            SourceBatchId = sourceBatchId,
        };

    [Fact]
    public async Task ReplayWorker_Registers_With_IWorkerStatusRegistry()
    {
        var worker = BuildWorker();
        var ct     = CancellationToken.None;

        await worker.StartAsync(ct);
        await worker.StopAsync(ct);

        _registry.Verify(r => r.Register(nameof(ReplayWorker), It.IsAny<TimeSpan>()), Times.Once);
    }

    [Fact]
    public async Task Advance_Transitions_Pending_Operation_To_Running()
    {
        var opId = Guid.NewGuid();
        using var db = NewDb();
        db.Operations.Add(MakeOperation(opId, "Pending"));
        db.ReplayRequests.Add(MakeRequest(opId));
        await db.SaveChangesAsync();

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        var op = await NewDb().Operations.FindAsync(opId);
        op!.Status.Should().Be("Running");
    }

    [Fact]
    public async Task Advance_FailedDelivery_Item_Resets_Batch_To_Retry()
    {
        var opId = Guid.NewGuid();
        var db   = NewDb();

        var batch = new SyncOutgoingBatch
        {
            NodeId = "n1", ChannelId = "ch1",
            Status = (byte)BatchStatus.Error, BatchSequence = 1,
            CreateTime = DateTime.UtcNow.AddHours(-1)
        };
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        db.Operations.Add(MakeOperation(opId, "Running"));
        db.ReplayRequests.Add(MakeRequest(opId, "FailedDelivery"));
        db.ReplayItems.Add(MakeItem(opId, sourceBatchId: batch.BatchId));
        await db.SaveChangesAsync();

        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        var updated = await NewDb().OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Retry);

        var item = await NewDb().ReplayItems.FirstAsync(i => i.OperationId == opId);
        item.Status.Should().Be("Completed");
    }

    [Fact]
    public async Task Advance_NodeNoLongerActive_Skips_Item()
    {
        var opId = Guid.NewGuid();
        var db   = NewDb();

        var batch = new SyncOutgoingBatch
        {
            NodeId = "n1", ChannelId = "ch1",
            Status = (byte)BatchStatus.Error, BatchSequence = 1
        };
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        db.Operations.Add(MakeOperation(opId, "Running"));
        db.ReplayRequests.Add(MakeRequest(opId, "FailedDelivery"));
        db.ReplayItems.Add(MakeItem(opId, batch.BatchId));
        await db.SaveChangesAsync();

        var disabled = new NodeDto("n1", "g", "http://n1", NodeLifecycleState.Disabled,
            null, null, 30, false, TransportMode.Push, ConnectivityStatus.Unreachable,
            false, null, null, null, null, false, null);
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(disabled);

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        var item = await NewDb().ReplayItems.FirstAsync(i => i.OperationId == opId);
        item.Status.Should().Be("Skipped");
    }

    [Fact]
    public async Task Advance_AllItemsComplete_Sets_Operation_Completed_Success()
    {
        var opId = Guid.NewGuid();
        var db   = NewDb();
        db.Operations.Add(MakeOperation(opId, "Running"));
        db.ReplayRequests.Add(MakeRequest(opId));
        db.ReplayItems.Add(MakeItem(opId, status: "Completed"));
        await db.SaveChangesAsync();

        _ops.Setup(x => x.CompleteAsync(opId, OperationResult.Success, It.IsAny<string?>(), default))
            .Returns(Task.CompletedTask);

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        _ops.Verify(x => x.CompleteAsync(opId, OperationResult.Success, It.IsAny<string?>(), default), Times.Once);
    }

    [Fact]
    public async Task Advance_SomeItemsFailed_Sets_PartialSuccess()
    {
        var opId = Guid.NewGuid();
        var db   = NewDb();
        db.Operations.Add(MakeOperation(opId, "Running"));
        db.ReplayRequests.Add(MakeRequest(opId));
        db.ReplayItems.Add(MakeItem(opId, status: "Completed"));
        db.ReplayItems.Add(MakeItem(opId, status: "Failed"));
        await db.SaveChangesAsync();

        _ops.Setup(x => x.CompleteAsync(opId, OperationResult.PartialSuccess, It.IsAny<string?>(), default))
            .Returns(Task.CompletedTask);

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        _ops.Verify(x => x.CompleteAsync(opId, OperationResult.PartialSuccess, It.IsAny<string?>(), default), Times.Once);
    }

    [Fact]
    public async Task Advance_Resumable_After_Interruption_Skips_Completed_Items()
    {
        var opId = Guid.NewGuid();
        var db   = NewDb();
        var batch = new SyncOutgoingBatch
        {
            NodeId = "n1", ChannelId = "ch1", Status = (byte)BatchStatus.Error, BatchSequence = 2
        };
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        db.Operations.Add(MakeOperation(opId, "Running"));
        db.ReplayRequests.Add(MakeRequest(opId));
        db.ReplayItems.Add(MakeItem(opId, status: "Completed"));
        db.ReplayItems.Add(MakeItem(opId, sourceBatchId: batch.BatchId));
        await db.SaveChangesAsync();

        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        var items = await NewDb().ReplayItems.Where(i => i.OperationId == opId).ToListAsync();
        items.Count(i => i.Status == "Completed").Should().Be(2);
    }

    [Fact]
    public async Task Advance_MissedData_Item_Calls_BatchCreator()
    {
        var opId = Guid.NewGuid();
        var db   = NewDb();

        db.DataEvents.Add(new SyncDataEvent
        {
            EventId = 1, ChannelId = "ch1", TriggerId = "trg1",
            SourceNodeId = "src", EventType = 'I',
            TableName = "t", TransactionId = 1,
            CreateTime = DateTime.UtcNow.AddHours(-1)
        });
        await db.SaveChangesAsync();

        db.Operations.Add(MakeOperation(opId, "Running"));
        db.ReplayRequests.Add(MakeRequest(opId, "MissedData"));
        db.ReplayItems.Add(MakeItem(opId, sourceBatchId: null));
        await db.SaveChangesAsync();

        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        _routing.Setup(x => x.ResolveAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new[] { "n1" });
        _batchCreator.Setup(x => x.CreateBatchesAsync(
            It.IsAny<IReadOnlyList<SyncDataEvent>>(),
            It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(),
            default))
            .ReturnsAsync(new List<SyncOutgoingBatch>
            {
                new() { BatchId = 99, NodeId = "n1", ChannelId = "ch1", BatchSequence = 10 }
            });

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        _batchCreator.Verify(x => x.CreateBatchesAsync(
            It.IsAny<IReadOnlyList<SyncDataEvent>>(),
            It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(),
            default), Times.Once);
    }

    [Fact]
    public async Task Advance_MaxConcurrentOperations_Respected()
    {
        var db = NewDb();
        for (var i = 0; i < 6; i++)
        {
            var id = Guid.NewGuid();
            db.Operations.Add(MakeOperation(id, "Pending"));
            db.ReplayRequests.Add(MakeRequest(id));
        }
        await db.SaveChangesAsync();

        var worker = BuildWorker();
        await worker.RunTickAsync(CancellationToken.None);

        var running = await NewDb().Operations
            .CountAsync(o => o.Status == "Running" && o.OperationType == "BatchReplay");
        running.Should().Be(5);
    }
}
```

- [ ] **Step 2: Run tests — expect build failure**

```
dotnet test tests/MSOSync.SchedulerTests --filter "FullyQualifiedName~ReplayWorkerTests" -v normal
```

Expected: build errors (ReplayWorker not yet implemented).

- [ ] **Step 3: Implement `ReplayWorker`**

```csharp
// src/MSOSync.Scheduler/Workers/ReplayWorker.cs
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Replay;
using MSOSync.Metadata.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Routing;

namespace MSOSync.Scheduler.Workers;

public sealed class ReplayWorker(
    IServiceScopeFactory       scopeFactory,
    IOptions<ReplayOptions>    opts,
    IWorkerStatusRegistry      registry,
    ILogger<ReplayWorker>      logger) : BackgroundService
{
    private int _running;

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromSeconds(
            opts.Value.WorkerIntervalSeconds > 0 ? opts.Value.WorkerIntervalSeconds : 10);
        registry.Register(nameof(ReplayWorker), interval);
        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(opts.Value.WorkerIntervalSeconds);
        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(ct))
        {
            if (Interlocked.Exchange(ref _running, 1) == 1)
            {
                logger.LogWarning("ReplayWorker tick skipped — previous tick still running");
                continue;
            }
            registry.RecordTickStart(nameof(ReplayWorker));
            try
            {
                await RunTickAsync(ct);
                registry.RecordTickComplete(nameof(ReplayWorker));
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                registry.RecordTickFailed(nameof(ReplayWorker), ex);
                logger.LogError(ex, "ReplayWorker tick failed");
            }
            finally { Interlocked.Exchange(ref _running, 0); }
        }
    }

    internal async Task RunTickAsync(CancellationToken ct)
    {
        await using var scope      = scopeFactory.CreateAsyncScope();
        var db                     = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var operations             = scope.ServiceProvider.GetRequiredService<IOperationService>();
        var nodeMeta               = scope.ServiceProvider.GetRequiredService<INodeMetadataService>();
        var batchCreator           = scope.ServiceProvider.GetRequiredService<IBatchCreator>();
        var routing                = scope.ServiceProvider.GetRequiredService<IRoutingService>();
        var clock                  = scope.ServiceProvider.GetRequiredService<IClock>();
        var maxConcurrent          = opts.Value.MaxConcurrentOperations;
        var pageSize               = opts.Value.ItemPageSize;

        // 1. Transition Pending → Running (up to maxConcurrent)
        var pendingOps = await db.Operations
            .Where(o => o.Status == "Pending" && o.OperationType == "BatchReplay")
            .Take(maxConcurrent)
            .ToListAsync(ct);

        var runningCount = await db.Operations
            .CountAsync(o => o.Status == "Running" && o.OperationType == "BatchReplay", ct);

        var slotsAvailable = maxConcurrent - runningCount;
        foreach (var op in pendingOps.Take(Math.Max(0, slotsAvailable)))
        {
            op.Status    = "Running";
            op.StartedAt = clock.UtcNow;
        }
        await db.SaveChangesAsync(ct);

        // 2. Advance all Running operations
        var runningOps = await db.Operations
            .Where(o => o.Status == "Running" && o.OperationType == "BatchReplay")
            .ToListAsync(ct);

        foreach (var op in runningOps)
        {
            var req = await db.ReplayRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.OperationId == op.OperationId, ct);
            if (req is null) continue;

            var mode = Enum.Parse<ReplayMode>(req.ReplayMode);

            // Check if any Pending items remain
            var pendingItems = await db.ReplayItems
                .Where(i => i.OperationId == op.OperationId && i.Status == "Pending")
                .Take(pageSize)
                .ToListAsync(ct);

            if (pendingItems.Count == 0)
            {
                // All items processed — complete the operation
                await CompleteOperationAsync(op, db, operations, ct);
                continue;
            }

            // Check node is still active
            var node = await nodeMeta.GetNodeAsync(req.NodeId, ct);
            var nodeActive = node is not null
                && node.LifecycleState is NodeLifecycleState.Active;

            foreach (var item in pendingItems)
            {
                if (!nodeActive)
                {
                    item.Status = "Skipped";
                    continue;
                }

                item.Status = "Processing";
                await db.SaveChangesAsync(ct);

                try
                {
                    if (item.SourceBatchId.HasValue)
                    {
                        // FailedDelivery: reset batch to Retry
                        await db.OutgoingBatches
                            .Where(b => b.BatchId == item.SourceBatchId.Value)
                            .ExecuteUpdateAsync(s =>
                                s.SetProperty(b => b.Status, (byte)BatchStatus.Retry), ct);
                        item.ReplayBatchId = item.SourceBatchId;
                    }
                    else
                    {
                        // MissedData: query events, resolve routing, create batches
                        var channelIds = req.ChannelIdsJson is null ? null
                            : System.Text.Json.JsonSerializer.Deserialize<string[]>(req.ChannelIdsJson);

                        var events = await db.DataEvents.AsNoTracking()
                            .Where(e => e.ChannelId == item.ChannelId
                                     && e.CreateTime >= req.FromTime
                                     && e.CreateTime <= req.ToTime)
                            .ToListAsync(ct);

                        if (events.Count > 0)
                        {
                            // Build routes: only route to the target node
                            var routes = new Dictionary<long, IReadOnlyList<string>>();
                            foreach (var ev in events)
                            {
                                var targets = await routing.ResolveAsync(ev.TriggerId ?? string.Empty, ct);
                                if (targets.Contains(req.NodeId))
                                    routes[ev.EventId] = new[] { req.NodeId };
                            }

                            if (routes.Count > 0)
                            {
                                var batches = await batchCreator.CreateBatchesAsync(events, routes, ct);
                                item.ReplayBatchId = batches.FirstOrDefault()?.BatchId;
                            }
                        }
                    }

                    item.Status = "Completed";
                }
                catch (Exception ex)
                {
                    item.Status       = "Failed";
                    item.ErrorMessage = ex.Message.Length > 1000
                        ? ex.Message[..1000] : ex.Message;
                    logger.LogError(ex, "ReplayWorker failed to process item {ItemId}", item.ItemId);
                }
            }

            // Update progress
            var total     = await db.ReplayItems.CountAsync(i => i.OperationId == op.OperationId, ct);
            var completed = await db.ReplayItems.CountAsync(
                i => i.OperationId == op.OperationId
                  && (i.Status == "Completed" || i.Status == "Failed" || i.Status == "Skipped"), ct);
            op.ProgressPercent = total > 0 ? completed * 100 / total : 0;

            await db.SaveChangesAsync(ct);

            // Check if now complete
            var remainingPending = await db.ReplayItems
                .AnyAsync(i => i.OperationId == op.OperationId && i.Status == "Pending", ct);
            if (!remainingPending)
                await CompleteOperationAsync(op, db, operations, ct);
        }
    }

    private static async Task CompleteOperationAsync(
        SyncOperation op, AppDbContext db, IOperationService operations, CancellationToken ct)
    {
        var hasFailed = await db.ReplayItems
            .AnyAsync(i => i.OperationId == op.OperationId && i.Status == "Failed", ct);
        var result = hasFailed ? OperationResult.PartialSuccess : OperationResult.Success;
        await operations.CompleteAsync(op.OperationId, result, null, ct);
        op.Status = "Completed";
    }
}
```

Note: `SyncDataEvent` has `TriggerId` for routing. If `SyncDataEvent` doesn't have `TriggerId`, use `EventId.ToString()` or check the actual field name.

- [ ] **Step 4: Register `ReplayWorker` in `SyncSchedulerExtensions`**

In `src/MSOSync.Scheduler/SyncSchedulerExtensions.cs`, after `AddHostedService<RollingOperationWorker>()`:

```csharp
        services.AddHostedService<ReplayWorker>();
```

Also add `ReplayOptions` binding in the same method. Since `ReplayOptions` is in `MSOSync.Metadata`, the extension already has access via the `MSOSync.Metadata` project reference. Add:

```csharp
        services.Configure<MSOSync.Metadata.Options.ReplayOptions>(
            config.GetSection(MSOSync.Metadata.Options.ReplayOptions.Section));
```

- [ ] **Step 5: Check `SyncDataEvent` field names**

Run:
```
grep -r "TriggerId\|TriggerName" src/MSOSync.Persistence/Entities/SyncDataEvent.cs
```

If `SyncDataEvent` has no `TriggerId`, check what field is used for routing. Look at `SyncEngine` or `IBatchCreator` usage. Adjust the `routing.ResolveAsync` call in `RunTickAsync` accordingly. The `IRoutingService.ResolveAsync(string triggerId)` needs a trigger identifier.

- [ ] **Step 6: Run tests**

```
dotnet test tests/MSOSync.SchedulerTests --filter "FullyQualifiedName~ReplayWorkerTests" -v normal
```

Expected: all 9 tests pass.

- [ ] **Step 7: Build full solution**

```
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: 0 errors.

- [ ] **Step 8: Commit**

```
git add src/MSOSync.Scheduler/Workers/ReplayWorker.cs
git add src/MSOSync.Scheduler/SyncSchedulerExtensions.cs
git add tests/MSOSync.SchedulerTests/ReplayWorkerTests.cs
git commit -m "feat(2B.2-T3): ReplayWorker (FailedDelivery + MissedData advance)"
```
