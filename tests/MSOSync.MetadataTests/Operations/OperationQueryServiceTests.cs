using FluentAssertions;
using MSOSync.Metadata.Operations;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class OperationQueryServiceTests : IDisposable
{
    private readonly AppDbContext          _db;
    private readonly OperationQueryService _sut;

    public OperationQueryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new OperationQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    private SyncOperation MakeOp(string type = "Export", string status = "Completed") => new()
    {
        OperationId   = Guid.NewGuid(),
        OperationType = type,
        Status        = status,
        Source        = "User",
        CanCancel     = false,
        CanRetry      = false,
        StartedAt     = DateTime.UtcNow,
    };

    [Fact]
    public async Task GetPageAsync_NoFilter_ReturnsAllOrderedByStartedAtDesc()
    {
        var early = MakeOp(); early.StartedAt = DateTime.UtcNow.AddMinutes(-5);
        var late  = MakeOp(); late.StartedAt  = DateTime.UtcNow;
        _db.Operations.AddRange(early, late);
        await _db.SaveChangesAsync();

        var result = await _sut.GetPageAsync(new OperationFilter(), default);

        result.Items.Should().HaveCount(2);
        result.Items[0].OperationId.Should().Be(late.OperationId);
        result.Items[1].OperationId.Should().Be(early.OperationId);
    }

    [Fact]
    public async Task GetPageAsync_TypeFilter_ReturnsOnlyMatchingType()
    {
        _db.Operations.AddRange(MakeOp("Export"), MakeOp("Rollout"), MakeOp("Export"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetPageAsync(new OperationFilter(Types: new[] { "Export" }), default);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(o => o.OperationType == "Export");
    }

    [Fact]
    public async Task GetPageAsync_CursorPagination_ReturnsNextPage()
    {
        for (int i = 0; i < 5; i++)
        {
            var op = MakeOp();
            op.StartedAt = DateTime.UtcNow.AddMinutes(-i);
            _db.Operations.Add(op);
        }
        await _db.SaveChangesAsync();

        var page1 = await _sut.GetPageAsync(new OperationFilter(PageSize: 3), default);
        page1.Items.Should().HaveCount(3);
        page1.NextCursor.Should().NotBeNull();

        var page2 = await _sut.GetPageAsync(
            new OperationFilter(PageSize: 3, Cursor: page1.NextCursor), default);
        page2.Items.Should().HaveCount(2);
        page2.NextCursor.Should().BeNull();
    }

    [Fact]
    public async Task GetDetailAsync_ExistingId_ReturnsDetail()
    {
        var op = MakeOp();
        _db.Operations.Add(op);
        await _db.SaveChangesAsync();

        var detail = await _sut.GetDetailAsync(op.OperationId, default);

        detail.Should().NotBeNull();
        detail!.OperationId.Should().Be(op.OperationId);
    }

    [Fact]
    public async Task GetDetailAsync_MissingId_ReturnsNull()
    {
        var result = await _sut.GetDetailAsync(Guid.NewGuid(), default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPageAsync_PendingItems_HaveQueuePosition()
    {
        _db.Operations.AddRange(
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export",
                Status = "Pending", Source = "User", CanCancel = false, CanRetry = false,
                StartedAt = DateTime.UtcNow.AddMinutes(-2) },
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Rollout",
                Status = "Pending", Source = "User", CanCancel = false, CanRetry = false,
                StartedAt = DateTime.UtcNow.AddMinutes(-1) });
        await _db.SaveChangesAsync();

        var result = await _sut.GetPageAsync(
            new OperationFilter(Statuses: new[] { "Pending" }), default);

        result.Items.Should().HaveCount(2);
        result.Items.Should().OnlyContain(o => o.QueuePosition.HasValue);
    }
}
