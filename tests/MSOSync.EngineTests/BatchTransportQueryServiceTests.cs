using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchTransportQueryServiceTests
{
    private static (BatchTransportQueryService Svc, MSOSync.Persistence.AppDbContext Db) Create()
    {
        var db = TestDbContext.Create();
        return (new BatchTransportQueryService(db), db);
    }

    [Fact]
    public async Task GetNextPullBatchAsync_NoBatches_ReturnsNull()
    {
        var (svc, _) = Create();
        var (batch, more) = await svc.GetNextPullBatchAsync("hub", "default", 0);
        batch.Should().BeNull();
        more.Should().BeFalse();
    }

    [Fact]
    public async Task GetNextPullBatchAsync_OneBatch_ReturnsBatchNoMore()
    {
        var (svc, db) = Create();
        db.OutgoingBatches.Add(new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = "hub", ChannelId = "default",
            Status = (byte)BatchStatus.New, RowCount = 5
        });
        await db.SaveChangesAsync();

        var (batch, more) = await svc.GetNextPullBatchAsync("hub", "default", 0);
        batch.Should().NotBeNull();
        more.Should().BeFalse();
    }

    [Fact]
    public async Task GetNextPullBatchAsync_TwoBatches_MoreAvailableTrue()
    {
        var (svc, db) = Create();
        for (var i = 1; i <= 2; i++)
            db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                BatchSequence = i, NodeId = "hub", ChannelId = "default",
                Status = (byte)BatchStatus.New, RowCount = 1
            });
        await db.SaveChangesAsync();

        var (batch, more) = await svc.GetNextPullBatchAsync("hub", "default", 0);
        batch.Should().NotBeNull();
        more.Should().BeTrue();
    }

    [Fact]
    public async Task GetLastSequenceAsync_NoIncoming_ReturnsZero()
    {
        var (svc, _) = Create();
        var seq = await svc.GetLastSequenceAsync("source1", "default");
        seq.Should().Be(0L);
    }

    [Fact]
    public async Task IncomingBatchExistsAsync_NonExistent_ReturnsFalse()
    {
        var (svc, _) = Create();
        var exists = await svc.IncomingBatchExistsAsync("source1", 99L);
        exists.Should().BeFalse();
    }
}
