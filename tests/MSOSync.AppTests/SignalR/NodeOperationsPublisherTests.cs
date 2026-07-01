using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using MSOSync.App.Hubs;
using MSOSync.App.SignalR;
using MSOSync.Metadata.Events;
using MSOSync.Persistence;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.AppTests.SignalR;

public sealed class NodeOperationsPublisherTests
{
    private readonly Mock<IHubContext<OperationsHub>>      _hubCtx    = new();
    private readonly Mock<IHubClients>                     _clients   = new();
    private readonly Mock<IClientProxy>                    _group     = new();
    private readonly List<(string Method, object? Arg)>   _sent      = [];
    private readonly NodeOperationsPublisher               _publisher;

    public NodeOperationsPublisherTests()
    {
        _hubCtx.Setup(h => h.Clients).Returns(_clients.Object);
        _clients.Setup(c => c.Group("operators")).Returns(_group.Object);
        _group
            .Setup(p => p.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, object?[], CancellationToken>((method, args, _) =>
                _sent.Add((method, args.Length > 0 ? args[0] : null)))
            .Returns(Task.CompletedTask);

        _publisher = new NodeOperationsPublisher(_hubCtx.Object);
    }

    private OperationsEvent? LastSent => _sent.LastOrDefault().Arg as OperationsEvent;

    [Fact]
    public async Task Handle_Approved_SendsNodeApprovedEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-1", "APPROVED"), CancellationToken.None);

        _sent.Should().HaveCount(1);
        _sent[0].Method.Should().Be("OperationsEvent");
        LastSent!.Type.Should().Be(OperationsEventType.NodeApproved);
        LastSent.NodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task Handle_Rejected_SendsNodeRejectedEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-2", "REJECTED"), CancellationToken.None);

        LastSent!.Type.Should().Be(OperationsEventType.NodeRejected);
        LastSent.NodeId.Should().Be("node-2");
    }

    [Fact]
    public async Task Handle_Disabled_SendsNodeDisabledEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-3", "DISABLED"), CancellationToken.None);

        LastSent!.Type.Should().Be(OperationsEventType.NodeDisabled);
        LastSent.NodeId.Should().Be("node-3");
    }

    [Fact]
    public async Task Handle_Enabled_SendsNodeEnabledEvent()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-4", "ENABLED"), CancellationToken.None);

        LastSent!.Type.Should().Be(OperationsEventType.NodeEnabled);
        LastSent.NodeId.Should().Be("node-4");
    }

    [Fact]
    public async Task Handle_Updated_SendsNothing()
    {
        await _publisher.Handle(
            new NodeMetadataChangedEvent("node-5", "UPDATED"), CancellationToken.None);

        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ConnectivityChanged_SendsNodeHealthChangedEvent()
    {
        await _publisher.Handle(
            new NodeConnectivityChangedEvent(
                "node-6",
                ConnectivityStatus.Reachable,
                ConnectivityStatus.Degraded),
            CancellationToken.None);

        _sent.Should().HaveCount(1);
        _sent[0].Method.Should().Be("OperationsEvent");
        LastSent!.Type.Should().Be(OperationsEventType.NodeHealthChanged);
        LastSent.NodeId.Should().Be("node-6");
        LastSent.PreviousStatus.Should().Be("Reachable");
        LastSent.CurrentStatus.Should().Be("Degraded");
    }

    [Fact]
    public async Task Handle_ConnectivityChanged_UsesOperatorsGroup()
    {
        await _publisher.Handle(
            new NodeConnectivityChangedEvent(
                "node-7",
                ConnectivityStatus.Unknown,
                ConnectivityStatus.Reachable),
            CancellationToken.None);

        _clients.Verify(c => c.Group("operators"), Times.Once);
    }
}
