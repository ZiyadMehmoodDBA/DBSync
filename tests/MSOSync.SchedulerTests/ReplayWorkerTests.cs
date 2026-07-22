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
    private readonly Mock<INodeMetadataService>  _nodeMeta     = new();
    private readonly Mock<IOperationService>     _ops          = new();
    private readonly Mock<IBatchCreator>         _batchCreator = new();
    private readonly Mock<IRoutingService>       _routing      = new();
    private readonly Mock<IWorkerStatusRegistry> _registry     = new();
    private readonly Mock<IClock>                _clock        = new();
    private readonly DbContextOptions<AppDbContext> _dbOpts =
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;

    public ReplayWorkerTests()
    {
        _clock.Setup(c => c.UtcNow).Returns(DateTime.UtcNow);
    }

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
        services.AddScoped(_ => _clock.Object);

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
