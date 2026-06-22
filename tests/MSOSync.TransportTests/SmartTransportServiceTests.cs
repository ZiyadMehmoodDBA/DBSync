using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Batch;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class SmartTransportServiceTests
{
    private static SmartTransportService CreateService(
        INodeMetadataService nodeMetadata,
        PushClient?          pushClient   = null,
        IBatchStateMachine?  stateMachine = null)
    {
        var db           = TestDb.Create();
        var compression  = new GzipCompressionService();
        var clock        = new FakeClock();
        var classifier   = new TransportFailureClassifier();
        var sm           = stateMachine ?? new BatchStateMachine(db, clock);
        var ack          = new AcknowledgementService(sm, db, NullLogger<AcknowledgementService>.Instance);
        var nodeProps    = Microsoft.Extensions.Options.Options.Create(
            new MSOSync.Common.NodeProperties { NodeId = "local", GroupId = "g", SyncUrl = "http://local", NodeToken = "tok" });
        var pc           = pushClient ?? new PushClient(Mock.Of<INodeHttpClient>(), nodeProps);

        return new SmartTransportService(nodeMetadata, pc, sm, ack, classifier,
            NullLogger<SmartTransportService>.Instance);
    }

    private static NodeDto ActivePushNode() =>
        new("target", "g", "http://target", "APPROVED", null, null, 60, true, TransportMode.Push);

    private static NodeDto ActivePullNode() =>
        new("target", "g", "http://target", "APPROVED", null, null, 60, true, TransportMode.Pull);

    [Fact]
    public async Task SendBatchAsync_UnknownNode_Skips()
    {
        var meta = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync((NodeDto?)null);

        var svc   = CreateService(meta.Object);
        var batch = new SyncOutgoingBatch { BatchId = 1, NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };

        await svc.SendBatchAsync(batch, [], default);
        // No exception = skip successful
    }

    [Fact]
    public async Task SendBatchAsync_DisabledNode_Skips()
    {
        var disabledNode = new NodeDto("target", "g", "http://t", "APPROVED", null, null, 60, false, TransportMode.Push);
        var meta = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync(disabledNode);

        var svc   = CreateService(meta.Object);
        var batch = new SyncOutgoingBatch { BatchId = 1, NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };

        await svc.SendBatchAsync(batch, [], default);
        // No exception = skip successful
    }

    [Fact]
    public async Task SendBatchAsync_PullNode_DoesNotCallPushClient()
    {
        var meta       = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync(ActivePullNode());
        var httpClient = new Mock<INodeHttpClient>();

        var svc   = CreateService(meta.Object);
        var batch = new SyncOutgoingBatch { BatchId = 1, NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };

        await svc.SendBatchAsync(batch, [], default);

        httpClient.Verify(
            h => h.PostAsync<It.IsAnyType, It.IsAnyType>(It.IsAny<string>(), It.IsAny<It.IsAnyType>(), It.IsAny<string>(), It.IsAny<string>(), default),
            Times.Never);
    }

    [Fact]
    public async Task SendBatchAsync_PushNode_CallsPushClient()
    {
        var db          = TestDb.Create();
        var clock       = new FakeClock();
        var sm          = new BatchStateMachine(db, clock);
        var meta        = new Mock<INodeMetadataService>();
        meta.Setup(m => m.GetNodeAsync("target", default)).ReturnsAsync(ActivePushNode());

        var httpClient  = new Mock<INodeHttpClient>();
        var pushResponse = new MSOSync.Transport.Payloads.PushResponse(1L, true, 5, 0, null);
        httpClient
            .Setup(h => h.PostAsync<MSOSync.Transport.Payloads.BatchPayload, MSOSync.Transport.Payloads.PushResponse>(
                It.IsAny<string>(), It.IsAny<MSOSync.Transport.Payloads.BatchPayload>(), It.IsAny<string>(), It.IsAny<string>(), default))
            .ReturnsAsync(pushResponse);

        var nodeProps = Microsoft.Extensions.Options.Options.Create(
            new MSOSync.Common.NodeProperties { NodeId = "local", GroupId = "g", SyncUrl = "http://local", NodeToken = "tok" });
        var pushClient = new PushClient(httpClient.Object, nodeProps);

        var batch = new SyncOutgoingBatch { NodeId = "target", ChannelId = "ch", Status = (byte)BatchStatus.New, BatchSequence = 1 };
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var svc = CreateService(meta.Object, pushClient, sm);
        await svc.SendBatchAsync(batch, [], default);

        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }
}
