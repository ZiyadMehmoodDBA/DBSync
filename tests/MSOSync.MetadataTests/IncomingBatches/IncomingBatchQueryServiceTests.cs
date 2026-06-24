using FluentAssertions;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.IncomingBatches;

public sealed class IncomingBatchQueryServiceTests
{
    private static (IncomingBatchQueryService Svc, AppDbContext Db) Make()
    {
        var db  = TestDbContext.Create();
        var svc = new IncomingBatchQueryService(db);
        return (svc, db);
    }

    private static SyncNode MakeNode(string nodeId) => new()
    {
        NodeId   = nodeId,
        GroupId  = "default",
        SyncUrl  = "http://localhost",
        Status   = "REGISTERED"
    };

    private static SyncIncomingBatch MakeBatch(string sourceNodeId, IncomingBatchStatus status,
        long batchId) => new()
    {
        BatchId       = batchId,
        NodeId        = sourceNodeId,
        SourceNodeId  = sourceNodeId,
        ChannelId     = "ch-1",
        Status        = status,
        BatchSequence = batchId,
        ReceivedTime  = DateTime.UtcNow
    };

    [Fact]
    public async Task GetIncomingBatchesAsync_NoFilter_ReturnsAll()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        db.IncomingBatches.AddRange(
            MakeBatch("node-1", IncomingBatchStatus.Applied, 1L),
            MakeBatch("node-1", IncomingBatchStatus.Error,   2L));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(new IncomingBatchFilter(), default);

        result.TotalCount.Should().Be(2);
        result.Items.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetIncomingBatchesAsync_FilterByStatus_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        db.IncomingBatches.AddRange(
            MakeBatch("node-1", IncomingBatchStatus.Applied, 1L),
            MakeBatch("node-1", IncomingBatchStatus.Error,   2L),
            MakeBatch("node-1", IncomingBatchStatus.New,     3L));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(
            new IncomingBatchFilter { Status = IncomingBatchStatus.Error }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().Status.Should().Be(IncomingBatchStatus.Error);
    }

    [Fact]
    public async Task GetIncomingBatchesAsync_FilterBySourceNode_ReturnsMatching()
    {
        var (svc, db) = Make();
        db.Nodes.AddRange(MakeNode("node-1"), MakeNode("node-2"));
        await db.SaveChangesAsync();
        db.IncomingBatches.AddRange(
            MakeBatch("node-1", IncomingBatchStatus.Applied, 1L),
            MakeBatch("node-2", IncomingBatchStatus.Applied, 2L));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(
            new IncomingBatchFilter { SourceNodeId = "node-1" }, default);

        result.TotalCount.Should().Be(1);
        result.Items.Single().SourceNodeId.Should().Be("node-1");
    }

    [Fact]
    public async Task GetIncomingBatchesAsync_Pagination_HonorsPageSize()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        for (long i = 1; i <= 8; i++)
            db.IncomingBatches.Add(MakeBatch("node-1", IncomingBatchStatus.Applied, i));
        await db.SaveChangesAsync();

        var result = await svc.GetIncomingBatchesAsync(
            new IncomingBatchFilter { Page = 2, PageSize = 3 }, default);

        result.TotalCount.Should().Be(8);
        result.Items.Should().HaveCount(3);
        result.Page.Should().Be(2);
    }

    [Fact]
    public async Task GetIncomingBatchByIdAsync_Exists_ReturnsDetailDto()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        var batch = MakeBatch("node-1", IncomingBatchStatus.Applied, 42L);
        batch.AppliedTime = batch.ReceivedTime.AddMilliseconds(250);
        db.IncomingBatches.Add(batch);
        await db.SaveChangesAsync();

        var dto = await svc.GetIncomingBatchByIdAsync(42L, default);

        dto.Should().NotBeNull();
        dto!.BatchId.Should().Be(42L);
        dto.Status.Should().Be(IncomingBatchStatus.Applied);
        dto.ApplyTimeMs.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetIncomingBatchByIdAsync_Missing_ReturnsNull()
    {
        var (svc, db) = Make();
        var dto = await svc.GetIncomingBatchByIdAsync(99999L, default);
        dto.Should().BeNull();
    }

    [Fact]
    public async Task GetIncomingBatchByIdAsync_NoAppliedTime_ApplyTimeMsNull()
    {
        var (svc, db) = Make();
        db.Nodes.Add(MakeNode("node-1"));
        await db.SaveChangesAsync();
        db.IncomingBatches.Add(MakeBatch("node-1", IncomingBatchStatus.New, 10L));
        await db.SaveChangesAsync();

        var dto = await svc.GetIncomingBatchByIdAsync(10L, default);

        dto!.ApplyTimeMs.Should().BeNull();
    }
}
