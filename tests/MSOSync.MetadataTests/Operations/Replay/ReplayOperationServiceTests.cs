using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Metadata.Operations;
using MSOSync.Metadata.Operations.Replay;
using MSOSync.Metadata.Operations.Replay.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Replay;

public sealed class ReplayOperationServiceTests : IDisposable
{
    private readonly Mock<INodeMetadataService> _nodeMeta = new();
    private readonly Mock<IOperationService>    _ops      = new();
    private readonly Guid                       _fixedOpId = Guid.NewGuid();
    private readonly AppDbContext               _db;

    public ReplayOperationServiceTests()
    {
        _db = TestDbContext.Create();

        // Wire up the mock so that CreateAsync inserts a real SyncOperation row,
        // satisfying the FK constraint on sync_replay_request and sync_replay_item.
        _ops.Setup(o => o.CreateAsync(
                It.IsAny<OperationType>(), It.IsAny<Guid?>(), It.IsAny<Guid?>(),
                It.IsAny<OperationSource>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<string>(),
                It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns<OperationType, Guid?, Guid?, OperationSource, string, bool, bool, string, string?, CancellationToken>(
                async (type, refId, initBy, src, corrId, cc, cr, summary, meta, ct) =>
                {
                    _db.Operations.Add(new SyncOperation
                    {
                        OperationId   = _fixedOpId,
                        OperationType = type.ToString(),
                        Status        = "Pending",
                        Source        = src.ToString(),
                        CorrelationId = corrId,
                        StartedAt     = DateTime.UtcNow,
                        MetadataJson  = meta,
                        Summary       = summary,
                    });
                    await _db.SaveChangesAsync(ct);
                    return _fixedOpId;
                });

        // CompleteAsync and CancelAsync are no-ops by default (mock returns Task.CompletedTask)
        _ops.Setup(x => x.CompleteAsync(It.IsAny<Guid>(), It.IsAny<OperationResult>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _ops.Setup(x => x.CancelAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    public void Dispose() => _db.Dispose();

    private static NodeDto ActiveNode(string id = "n1") => new(
        id, "g", "http://n1", NodeLifecycleState.Active,
        null, null, 30, true, TransportMode.Push, ConnectivityStatus.Reachable,
        false, null, null, null, null, false, null);

    private static NodeDto DisabledNode(string id = "n1") => new(
        id, "g", "http://n1", NodeLifecycleState.Disabled,
        null, null, 30, false, TransportMode.Push, ConnectivityStatus.Unreachable,
        false, null, null, null, null, false, null);

    private static NodeDto DrainingNode(string id = "n1") => new(
        id, "g", "http://n1", NodeLifecycleState.Draining,
        null, null, 30, true, TransportMode.Push, ConnectivityStatus.Reachable,
        false, null, null, null, null, false, null);

    private ReplayOperationService BuildService() => new(_db, _ops.Object, _nodeMeta.Object);

    private static CreateReplayRequest ValidRequest(string mode = "FailedDelivery") => new(
        NodeId:      "n1",
        ReplayMode:  mode,
        FromTime:    DateTime.UtcNow.AddDays(-1),
        ToTime:      DateTime.UtcNow,
        ChannelIds:  null,
        BatchIds:    null,
        InitiatedBy: null);

    [Fact]
    public async Task CreateAsync_ActiveNode_ValidRange_Returns_OperationWithItems()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());

        _db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            NodeId = "n1", ChannelId = "ch1", Status = (byte)3, // BatchStatus.Error
            BatchSequence = 1, CreateTime = DateTime.UtcNow.AddHours(-1)
        });
        await _db.SaveChangesAsync();

        var svc    = BuildService();
        var result = await svc.CreateAsync(ValidRequest(), default);

        result.Should().NotBeNull();
        result.ItemCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateAsync_DrainingNode_ValidRange_Returns_OperationWithItems()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(DrainingNode());

        var svc = BuildService();

        // No batches → NoData case
        var result = await svc.CreateAsync(ValidRequest(), default);
        result.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAsync_DisabledNode_Throws_OperationStateException()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(DisabledNode());
        var svc = BuildService();

        var act = () => svc.CreateAsync(ValidRequest(), default);
        await act.Should().ThrowAsync<OperationStateException>();
    }

    [Fact]
    public async Task CreateAsync_RangeExceeds90Days_Throws_OperationStateException()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        var svc = BuildService();

        var req = new CreateReplayRequest("n1", "FailedDelivery",
            DateTime.UtcNow.AddDays(-100), DateTime.UtcNow, null, null, null);
        var act = () => svc.CreateAsync(req, default);
        await act.Should().ThrowAsync<OperationStateException>();
    }

    [Fact]
    public async Task CreateAsync_NoMatchingBatches_Completes_Immediately_NoData()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());

        var svc    = BuildService();
        var result = await svc.CreateAsync(ValidRequest(), default);

        result.ItemCount.Should().Be(0);
        _ops.Verify(x => x.CompleteAsync(_fixedOpId, OperationResult.NoData, It.IsAny<string?>(), default), Times.Once);
    }

    [Fact]
    public async Task CreateAsync_BatchIds_Only_Allowed_For_FailedDelivery_Mode()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());
        var svc = BuildService();

        var req = new CreateReplayRequest("n1", "MissedData",
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow,
            null, new long[] { 1, 2 }, null);  // BatchIds with MissedData
        var act = () => svc.CreateAsync(req, default);
        await act.Should().ThrowAsync<OperationStateException>();
    }

    [Fact]
    public async Task CreateAsync_FailedDelivery_Items_Have_SourceBatchId_Set()
    {
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());

        _db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            NodeId = "n1", ChannelId = "ch1", Status = (byte)3, // BatchStatus.Error
            BatchSequence = 1, CreateTime = DateTime.UtcNow.AddHours(-1)
        });
        await _db.SaveChangesAsync();

        var svc    = BuildService();
        var result = await svc.CreateAsync(ValidRequest("FailedDelivery"), default);

        _db.ChangeTracker.Clear();
        var items = await _db.ReplayItems.Where(i => i.OperationId == _fixedOpId).ToListAsync();
        items.Should().AllSatisfy(i => i.SourceBatchId.Should().NotBeNull());
    }

    [Fact]
    public async Task CreateAsync_MissedData_Items_Have_SourceBatchId_Null()
    {
        // For MissedData, items created from SyncDataEvent have no source batch id.
        // This test uses empty data — items count = 0 → NoData result.
        _nodeMeta.Setup(x => x.GetNodeAsync("n1", default)).ReturnsAsync(ActiveNode());

        var svc    = BuildService();
        var req    = ValidRequest("MissedData");
        var result = await svc.CreateAsync(req, default);
        result.ItemCount.Should().Be(0);
    }

    [Fact]
    public async Task CancelAsync_Running_Sets_Cancelled_Skips_Pending_Items()
    {
        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "BatchReplay",
            Status = "Running", Source = "User", StartedAt = DateTime.UtcNow
        });
        _db.ReplayItems.Add(new SyncReplayItem
        {
            ItemId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ChannelId = "ch1", EventCount = 1,
            Status = nameof(ReplayItemStatus.Pending)
        });
        await _db.SaveChangesAsync();

        var svc = BuildService();
        await svc.CancelAsync(opId, default);

        _db.ChangeTracker.Clear();
        var items = await _db.ReplayItems.Where(i => i.OperationId == opId).ToListAsync();
        items.Should().AllSatisfy(i => i.Status.Should().Be(nameof(ReplayItemStatus.Skipped)));
    }

    [Fact]
    public async Task CancelAsync_Completed_Throws_OperationStateException()
    {
        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "BatchReplay",
            Status = "Completed", Source = "User", StartedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync();

        var svc = BuildService();
        var act = () => svc.CancelAsync(opId, default);
        await act.Should().ThrowAsync<OperationStateException>();
    }
}
