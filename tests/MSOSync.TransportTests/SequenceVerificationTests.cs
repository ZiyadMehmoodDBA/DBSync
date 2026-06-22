using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class SequenceVerificationTests
{
    private static (BatchTransportQueryService Svc, AppDbContext Db) Create()
    {
        var db = TestDb.Create();
        return (new BatchTransportQueryService(db), db);
    }

    /// <summary>
    /// SyncIncomingBatch has a FK to SyncNode via SourceNodeId.
    /// Insert a minimal SyncNode so the FK is satisfied.
    /// </summary>
    private static async Task EnsureNode(AppDbContext db, string nodeId)
    {
        if (await db.Nodes.FindAsync(nodeId) != null) return;
        db.Nodes.Add(new SyncNode
        {
            NodeId   = nodeId,
            GroupId  = "g",
            SyncUrl  = "http://local",
            Status   = "APPROVED"
        });
        await db.SaveChangesAsync();
    }

    private static async Task<SyncIncomingBatch> InsertIncoming(
        AppDbContext db, string sourceNodeId, string channelId, long seq)
    {
        await EnsureNode(db, sourceNodeId);
        var b = new SyncIncomingBatch
        {
            BatchId       = seq,
            NodeId        = "local",
            ChannelId     = channelId,
            SourceNodeId  = sourceNodeId,
            BatchSequence = seq,
            ReceivedTime  = DateTime.UtcNow,
            RowCount      = 1
        };
        db.IncomingBatches.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task FirstBatch_Seq1_NoGap()
    {
        var (svc, _) = Create();
        var lastSeq = await svc.GetLastSequenceAsync("source1", "default");

        // First batch: lastSeq=0, batchSequence=1 → lastSeq + 1 == batchSequence → OK
        (lastSeq + 1 == 1).Should().BeTrue();
    }

    [Fact]
    public async Task SequentialBatches_NoGap()
    {
        var (svc, db) = Create();
        await InsertIncoming(db, "source1", "default", 1);
        await InsertIncoming(db, "source1", "default", 2);

        var lastSeq = await svc.GetLastSequenceAsync("source1", "default");
        lastSeq.Should().Be(2);
        (lastSeq + 1 == 3).Should().BeTrue();
    }

    [Fact]
    public async Task Gap_1_2_4_Detected()
    {
        var (svc, db) = Create();
        await InsertIncoming(db, "source1", "default", 1);
        await InsertIncoming(db, "source1", "default", 2);
        // Sequence 3 missing — incoming arrives with seq=4

        var lastSeq       = await svc.GetLastSequenceAsync("source1", "default");
        var incomingSeq   = 4L;
        var isGap         = lastSeq + 1 != incomingSeq;

        isGap.Should().BeTrue($"expected gap: lastSeq={lastSeq} incomingSeq={incomingSeq}");
    }

    [Fact]
    public async Task DuplicateBatch_Detected()
    {
        var (svc, db) = Create();
        await InsertIncoming(db, "source1", "default", 1);

        var exists = await svc.IncomingBatchExistsAsync("source1", 1L);
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task NonExistentBatch_NotDuplicate()
    {
        var (svc, _) = Create();
        var exists = await svc.IncomingBatchExistsAsync("source1", 99L);
        exists.Should().BeFalse();
    }
}
