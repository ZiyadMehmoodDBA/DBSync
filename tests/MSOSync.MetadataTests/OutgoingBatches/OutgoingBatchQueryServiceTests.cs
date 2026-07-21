using FluentAssertions;
using MSOSync.Metadata.OutgoingBatches;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.OutgoingBatches;

public sealed class OutgoingBatchQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OutgoingBatchQueryService _sut;

    public OutgoingBatchQueryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new OutgoingBatchQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task SeedBatch(long batchId, string nodeId, string channelId,
        byte status = 0, DateTime? createTime = null)
    {
        _db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchId = batchId, BatchSequence = batchId,
            NodeId = nodeId, ChannelId = channelId, Status = status,
            RowCount = 5, CreateTime = createTime ?? DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task GetBatches_filters_by_node_and_status_and_pages()
    {
        await SeedBatch(1, "n1", "ch1", status: 3); // Error
        await SeedBatch(2, "n1", "ch1", status: 3); // Error
        await SeedBatch(3, "n1", "ch1", status: 3); // Error
        await SeedBatch(4, "n2", "ch1", status: 3); // different node
        await SeedBatch(5, "n1", "ch1", status: 0); // different status

        var filter = new OutgoingBatchQueryFilter("n1", null, 3, "batchId", "asc", 1, 2);
        var result = await _sut.GetBatchesAsync(filter);

        result.Total.Should().Be(3);
        result.Items.Should().HaveCount(2);
        result.Items[0].BatchId.Should().Be(1);
        result.Items[1].BatchId.Should().Be(2);
    }

    [Fact]
    public async Task GetBatches_sorts_by_batchId_asc()
    {
        await SeedBatch(10, "n1", "ch1");
        await SeedBatch(5,  "n1", "ch1");
        await SeedBatch(15, "n1", "ch1");

        var filter = new OutgoingBatchQueryFilter("n1", null, null, "batchId", "asc", 1, 10);
        var result = await _sut.GetBatchesAsync(filter);

        result.Items.Select(b => b.BatchId).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetBatchById_returns_row_with_latest_error()
    {
        await SeedBatch(100, "n1", "ch1");
        _db.BatchErrors.AddRange(
            new SyncBatchError { ErrorId = 1, BatchId = 100, ErrorMessage = "old error", CreateTime = DateTime.UtcNow },
            new SyncBatchError { ErrorId = 2, BatchId = 100, ErrorMessage = "latest error", CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetBatchByIdAsync(100);

        result.Should().NotBeNull();
        result!.LatestError.Should().Be("latest error");
    }

    [Fact]
    public async Task GetBatchById_unknown_returns_null()
    {
        var result = await _sut.GetBatchByIdAsync(99999);
        result.Should().BeNull();
    }
}
