using FluentAssertions;
using Microsoft.AspNetCore.SignalR;
using Moq;
using MSOSync.App.Hubs;
using MSOSync.App.SignalR;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.AppTests.SignalR;

public sealed class SyncOperationsPublisherTests
{
    private readonly Mock<IHubContext<OperationsHub>>    _hubCtx  = new();
    private readonly Mock<IHubClients>                   _clients = new();
    private readonly Mock<IClientProxy>                  _group   = new();
    private readonly List<(string Method, object? Arg)> _sent    = [];
    private readonly SyncOperationsPublisher             _publisher;

    public SyncOperationsPublisherTests()
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

        _publisher = new SyncOperationsPublisher(_hubCtx.Object);
    }

    private OperationsEvent? LastSent => _sent.LastOrDefault().Arg as OperationsEvent;

    [Fact]
    public async Task Handle_SyncCycleCompleted_SendsSyncCycleCompletedEvent()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(100, 5, TimeSpan.FromSeconds(2)),
            CancellationToken.None);

        _sent.Should().HaveCount(1);
        _sent[0].Method.Should().Be("OperationsEvent");
        LastSent!.Type.Should().Be(OperationsEventType.SyncCycleCompleted);
    }

    [Fact]
    public async Task Handle_SyncCycleCompleted_UsesSystemNodeId()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(0, 0, TimeSpan.Zero),
            CancellationToken.None);

        LastSent!.NodeId.Should().Be("system");
    }

    [Fact]
    public async Task Handle_SyncCycleCompleted_UsesGlobalGroupId()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(0, 0, TimeSpan.Zero),
            CancellationToken.None);

        LastSent!.GroupId.Should().Be("global");
    }

    [Fact]
    public async Task Handle_SyncCycleCompleted_UsesOperatorsGroup()
    {
        await _publisher.Handle(
            new SyncCycleCompletedEvent(50, 3, TimeSpan.FromSeconds(1)),
            CancellationToken.None);

        _clients.Verify(c => c.Group("operators"), Times.Once);
    }
}
