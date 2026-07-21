using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Common.Workers;
using MSOSync.Engine;
using MSOSync.Metadata.Dtos;
using MSOSync.Metadata.Interfaces;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Scheduler;
using MSOSync.Topology;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class PullJobTests
{
    private const string LocalNodeId = "node-1";

    private readonly Mock<INodeMetadataService>         _nodeMeta   = new();
    private readonly Mock<IChannelMetadataService>      _channels   = new();
    private readonly Mock<ITopologyService>             _topology   = new();
    private readonly Mock<IBatchTransportQueryService>  _batchQuery = new();
    private readonly Mock<IApplyService>                _apply      = new();
    private readonly Mock<INodeHttpClient>              _nodeHttp   = new();
    private readonly Mock<IWorkerStatusRegistry>        _registry   = new();
    private readonly Mock<IClock>                       _clock      = new();

    private PullJob BuildJob()
    {
        var props = Options.Create(new NodeProperties
        {
            NodeId = LocalNodeId, GroupId = "g1", SyncUrl = "http://local", NodeToken = "tok"
        });

        var services = new ServiceCollection();
        services.AddScoped(_ => _nodeMeta.Object);
        services.AddScoped(_ => _channels.Object);
        services.AddScoped(_ => _topology.Object);
        services.AddScoped(_ => _batchQuery.Object);
        services.AddScoped(_ => _apply.Object);
        services.AddScoped(_ => _clock.Object);
        services.AddScoped(_ => new PullClient(_nodeHttp.Object, props));

        var scopeFactory = services.BuildServiceProvider()
            .GetRequiredService<IServiceScopeFactory>();

        return new PullJob(
            scopeFactory, props, Options.Create(new SyncOptions()),
            _registry.Object, NullLogger<PullJob>.Instance);
    }

    private static NodeDto Node(TransportMode mode) => new(
        LocalNodeId, "g1", "http://local", NodeLifecycleState.Active,
        null, null, 30, true, mode, ConnectivityStatus.Reachable,
        false, null, null, null, null, false);

    private static BatchPayload Batch(long seq) => new(
        BatchId: 42, BatchSequence: seq, ChannelId: "ch1",
        SourceNodeId: "src-1", TargetNodeId: LocalNodeId,
        RowCount: 5, Events: Array.Empty<EventPayload>());

    private void SetupTopology(params SourceNodeInfo[] sources)
    {
        _channels
            .Setup(x => x.GetChannelsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new ChannelDto("ch1", 1, 100, 10, 1000, Enabled: true) });
        _topology
            .Setup(x => x.GetSourceNodesAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(sources);
    }

    [Fact]
    public async Task IsPullEnabled_returns_false_when_node_in_push_mode()
    {
        _nodeMeta
            .Setup(x => x.GetNodeAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Node(TransportMode.Push));

        var enabled = await BuildJob().IsPullEnabledAsync(CancellationToken.None);

        enabled.Should().BeFalse();
    }

    [Fact]
    public async Task IsPullEnabled_returns_true_when_node_in_pull_mode()
    {
        _nodeMeta
            .Setup(x => x.GetNodeAsync(LocalNodeId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Node(TransportMode.Pull));

        var enabled = await BuildJob().IsPullEnabledAsync(CancellationToken.None);

        enabled.Should().BeTrue();
    }

    [Fact]
    public async Task RunTick_makes_no_http_calls_when_no_source_nodes()
    {
        SetupTopology();

        await BuildJob().RunTickAsync(LocalNodeId, CancellationToken.None);

        _nodeHttp.Verify(
            x => x.PostNullableAsync<PullRequest, PullResponse>(
                It.IsAny<string>(), It.IsAny<PullRequest>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _registry.Verify(x => x.RecordTickComplete(nameof(PullJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_applies_pulled_batch_and_sends_success_ack()
    {
        SetupTopology(new SourceNodeInfo("src-1", "http://src"));
        _batchQuery
            .Setup(x => x.GetLastSequenceAsync("src-1", "ch1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _batchQuery
            .Setup(x => x.IncomingBatchExistsAsync("src-1", 1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _nodeHttp
            .Setup(x => x.PostNullableAsync<PullRequest, PullResponse>(
                It.IsAny<string>(), It.IsAny<PullRequest>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullResponse(new[] { Batch(seq: 1) }, MoreAvailable: false));
        _apply
            .Setup(x => x.ApplyAsync(
                It.IsAny<SyncIncomingBatch>(), It.IsAny<BatchPayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ApplyResult(Success: true, AppliedRows: 5, ErrorRows: 0, ErrorMessage: null));

        await BuildJob().RunTickAsync(LocalNodeId, CancellationToken.None);

        _batchQuery.Verify(
            x => x.InsertIncomingBatchAsync(
                It.Is<SyncIncomingBatch>(b => b.BatchSequence == 1 && b.SourceNodeId == "src-1"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _apply.Verify(
            x => x.ApplyAsync(
                It.IsAny<SyncIncomingBatch>(), It.IsAny<BatchPayload>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _nodeHttp.Verify(
            x => x.PostVoidAsync(
                It.IsAny<string>(),
                It.Is<AckPayload>(a => a.Success && a.ErrorCode == null && a.BatchSequence == 1),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _registry.Verify(x => x.RecordTickComplete(nameof(PullJob)), Times.Once);
    }

    [Fact]
    public async Task RunTick_acks_sequence_gap_without_applying()
    {
        SetupTopology(new SourceNodeInfo("src-1", "http://src"));
        _batchQuery
            .Setup(x => x.GetLastSequenceAsync("src-1", "ch1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        _nodeHttp
            .Setup(x => x.PostNullableAsync<PullRequest, PullResponse>(
                It.IsAny<string>(), It.IsAny<PullRequest>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PullResponse(new[] { Batch(seq: 5) }, MoreAvailable: false));

        await BuildJob().RunTickAsync(LocalNodeId, CancellationToken.None);

        _apply.Verify(
            x => x.ApplyAsync(
                It.IsAny<SyncIncomingBatch>(), It.IsAny<BatchPayload>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _nodeHttp.Verify(
            x => x.PostVoidAsync(
                It.IsAny<string>(),
                It.Is<AckPayload>(a => !a.Success && a.ErrorCode == "SEQUENCE_GAP"),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
