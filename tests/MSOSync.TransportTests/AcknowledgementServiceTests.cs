using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Transport;
using MSOSync.Transport.Payloads;
using Xunit;

namespace MSOSync.TransportTests;

public sealed class AcknowledgementServiceTests
{
    private static (AcknowledgementService Svc, AppDbContext Db) Create()
    {
        var db  = TestDb.Create();
        var sm  = new BatchStateMachine(db, new FakeClock());
        var svc = new AcknowledgementService(sm, db, NullLogger<AcknowledgementService>.Instance);
        return (svc, db);
    }

    private static async Task<SyncOutgoingBatch> AddBatch(AppDbContext db, BatchStatus status)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = "hub", ChannelId = "default", Status = (byte)status
        };
        db.OutgoingBatches.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_Success_MovesToAcknowledged()
    {
        var (svc, db) = Create();
        var batch     = await AddBatch(db, BatchStatus.New);

        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(batch.BatchId, 1, "target", true, null, DateTimeOffset.UtcNow));

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_Failure_MovesToError_InsertsError()
    {
        var (svc, db) = Create();
        var batch     = await AddBatch(db, BatchStatus.New);

        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(batch.BatchId, 1, "target", false, "apply failed", DateTimeOffset.UtcNow));

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Error);
        db.BatchErrors.Should().ContainSingle(e => e.BatchId == batch.BatchId);
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_Duplicate_ReturnsTrue_NoStateChange()
    {
        var (svc, db) = Create();
        var batch     = await AddBatch(db, BatchStatus.Acknowledged);

        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(batch.BatchId, 1, "target", true, null, DateTimeOffset.UtcNow));

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var unchanged = await db.OutgoingBatches.FindAsync(batch.BatchId);
        unchanged!.Status.Should().Be((byte)BatchStatus.Acknowledged);
    }

    [Fact]
    public async Task AcknowledgeIncomingAsync_NotFound_ReturnsFalse()
    {
        var (svc, _) = Create();
        var result = await svc.AcknowledgeIncomingAsync(
            new AckPayload(99999, 1, "target", true, null, DateTimeOffset.UtcNow));
        result.Should().BeFalse();
    }
}
