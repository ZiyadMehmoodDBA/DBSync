using FluentAssertions;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using MSOSync.Metadata.Operations;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class OperationServiceTests : IDisposable
{
    private readonly AppDbContext               _db;
    private readonly Mock<IPublisher>           _publisherMock;
    private readonly Mock<IKeyedServiceProvider> _keyedMock;
    private readonly OperationService           _sut;

    public OperationServiceTests()
    {
        _db            = TestDbContext.Create();
        _publisherMock = new Mock<IPublisher>();
        _keyedMock     = new Mock<IKeyedServiceProvider>();
        _sut           = new OperationService(_db, _publisherMock.Object, _keyedMock.Object);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CreateAsync_PersistsRow_ReturnsNewGuid()
    {
        var id = await _sut.CreateAsync(
            OperationType.Export,
            referenceId:   Guid.NewGuid(),
            initiatedBy:   Guid.NewGuid(),
            source:        OperationSource.User,
            correlationId: "corr-001",
            canCancel:     true,
            canRetry:      false,
            summary:       "Export events to CSV",
            metadataJson:  null,
            ct:            default);

        id.Should().NotBeEmpty();

        var row = await _db.Operations.FindAsync(id);
        row.Should().NotBeNull();
        row!.Status.Should().Be("Pending");
        row.OperationType.Should().Be("Export");
        row.CanCancel.Should().BeTrue();
        row.CompletedAt.Should().BeNull();
    }

    [Fact]
    public async Task CreateAsync_PublishesOperationChangedEvent()
    {
        await _sut.CreateAsync(
            OperationType.Rollout, null, null,
            OperationSource.System, "corr-002",
            canCancel: false, canRetry: false,
            "Rollout started", null, default);

        _publisherMock.Verify(p =>
            p.Publish(It.IsAny<OperationChangedEvent>(), default),
            Times.Once);
    }

    [Fact]
    public async Task CompleteAsync_SetsStatusAndCompletedAt()
    {
        var id = await _sut.CreateAsync(
            OperationType.Export, null, null,
            OperationSource.Worker, "corr-003",
            canCancel: false, canRetry: false,
            "Export running", null, default);

        await _sut.CompleteAsync(id, OperationResult.Success, "Export done", default);

        var row = await _db.Operations.FindAsync(id);
        row!.Status.Should().Be("Completed");
        row.Result.Should().Be("Success");
        row.CompletedAt.Should().NotBeNull();
        row.ProgressPercent.Should().Be(100);
    }

    [Fact]
    public async Task CompleteAsync_WithFailure_SetsStatusFailed()
    {
        var id = await _sut.CreateAsync(
            OperationType.Decommission, null, null,
            OperationSource.System, "corr-004",
            canCancel: false, canRetry: true,
            "Decommission running", null, default);

        await _sut.CompleteAsync(id, OperationResult.Failure, "timeout", default);

        var row = await _db.Operations.FindAsync(id);
        row!.Status.Should().Be("Failed");
        row.Result.Should().Be("Failure");
    }

    [Fact]
    public async Task CancelAsync_PendingOperation_SetsStatusCancelled()
    {
        var refId = Guid.NewGuid();
        var id = await _sut.CreateAsync(
            OperationType.Rollout, refId, null,
            OperationSource.User, "corr-005",
            canCancel: true, canRetry: false,
            "Rollout", null, default);

        // No handler registered — GetKeyedService returns null — that is fine
        _keyedMock.Setup(k => k.GetKeyedService(typeof(IOperationHandler), OperationType.Rollout))
            .Returns((object?)null);

        await _sut.CancelAsync(id, Guid.NewGuid(), default);

        var row = await _db.Operations.FindAsync(id);
        row!.Status.Should().Be("Cancelled");
        row.Result.Should().Be("Cancelled");
        row.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task CancelAsync_CompletedOperation_ThrowsInvalidOperation()
    {
        var id = await _sut.CreateAsync(
            OperationType.Export, null, null,
            OperationSource.User, "corr-006",
            canCancel: true, canRetry: false,
            "Export", null, default);

        await _sut.CompleteAsync(id, OperationResult.Success, null, default);

        var act = () => _sut.CancelAsync(id, Guid.NewGuid(), default);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*cannot be cancelled*");
    }

    [Fact]
    public async Task CancelAsync_CanCancelFalse_ThrowsInvalidOperation()
    {
        var id = await _sut.CreateAsync(
            OperationType.Export, null, null,
            OperationSource.User, "corr-007",
            canCancel: false, canRetry: false,
            "Export", null, default);

        var act = () => _sut.CancelAsync(id, Guid.NewGuid(), default);
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*does not support cancellation*");
    }

    [Fact]
    public async Task CancelAsync_DelegatesHandlerCancelBeforeRowUpdate()
    {
        var refId   = Guid.NewGuid();
        var actorId = Guid.NewGuid();
        var handlerMock = new Mock<IOperationHandler>();
        handlerMock.Setup(h => h.CancelAsync(refId, actorId, default)).Returns(Task.CompletedTask);

        _keyedMock
            .Setup(k => k.GetKeyedService(typeof(IOperationHandler), OperationType.Rollout))
            .Returns(handlerMock.Object);

        var id = await _sut.CreateAsync(
            OperationType.Rollout, refId, null,
            OperationSource.User, "corr-008",
            canCancel: true, canRetry: false,
            "Rollout", null, default);

        await _sut.CancelAsync(id, actorId, default);

        handlerMock.Verify(h => h.CancelAsync(refId, actorId, default), Times.Once);
    }
}
