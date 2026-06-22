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
        var readerMock    = reader    ?? Mock.Of<IEventReader>(r => r.ReadAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<SyncDataEvent>>(Array.Empty<SyncDataEvent>()));
        var routingMock   = routing   ?? Mock.Of<IRoutingService>(r => r.ResolveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>()));
        var creatorMock   = creator   ?? Mock.Of<IBatchCreator>(c => c.CreateBatchesAsync(It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<IReadOnlyDictionary<long, IReadOnlyList<string>>>(), It.IsAny<CancellationToken>()) == Task.FromResult<IReadOnlyList<SyncOutgoingBatch>>(Array.Empty<SyncOutgoingBatch>()));
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
        transportMock.Verify(t => t.SendBatchAsync(It.IsAny<SyncOutgoingBatch>(), It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WithOneEvent_CallsRouteAndCreateAndTransport()
    {
        var evt   = new SyncDataEvent { EventId = 1, TriggerId = "t1", SourceNodeId = "hub", ChannelId = "default", EventType = 'I', TableName = "dbo.T", CreateTime = DateTime.UtcNow };
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
        transportMock.Verify(t => t.SendBatchAsync(batch, It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<CancellationToken>()), Times.Once);
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

        transportMock.Verify(t => t.SendBatchAsync(It.IsAny<SyncOutgoingBatch>(), It.IsAny<IReadOnlyList<SyncDataEvent>>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
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
