using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Common;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchStateMachineTests
{
    private static (BatchStateMachine Sm, AppDbContext Db) Create()
    {
        var db = TestDbContext.Create();
        return (new BatchStateMachine(db, new FakeClock()), db);
    }

    private static async Task<SyncOutgoingBatch> AddBatch(AppDbContext db, BatchStatus status)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1,
            NodeId        = "hub",
            ChannelId     = "default",
            Status        = (byte)status
        };
        db.OutgoingBatches.Add(b);
        await db.SaveChangesAsync();
        return b;
    }

    [Fact]
    public async Task MoveToSendingAsync_FromNew_TransitionsAndSetsSentTime()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.New);

        var result = await sm.MoveToSendingAsync(batch.BatchId);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Sending);
        updated.SentTime.Should().NotBeNull();
    }

    [Fact]
    public async Task MoveToSendingAsync_FromError_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Error);

        var result = await sm.MoveToSendingAsync(batch.BatchId);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task MoveToAcknowledgedAsync_FromSending_SetsAckTime()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Sending);
        var ackTime  = DateTimeOffset.UtcNow;

        var result = await sm.MoveToAcknowledgedAsync(batch.BatchId, ackTime);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Acknowledged);
        updated.AckTime.Should().NotBeNull();
    }

    [Fact]
    public async Task MoveToAcknowledgedAsync_FromNew_ReturnsTrueForPullMode()
    {
        // PULL mode: batch stays New until ACK, so New→Acknowledged must be valid
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.New);

        var result = await sm.MoveToAcknowledgedAsync(batch.BatchId, DateTimeOffset.UtcNow);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MoveToErrorAsync_FromSending_Transitions()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Sending);

        var result = await sm.MoveToErrorAsync(batch.BatchId);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Error);
    }

    [Fact]
    public async Task MoveToErrorAsync_FromNew_ReturnsTrueForPullMode()
    {
        // PULL mode: negative ACK → New→Error
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.New);

        var result = await sm.MoveToErrorAsync(batch.BatchId);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task MoveToRetryAsync_FromError_Transitions()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Error);

        var result = await sm.MoveToRetryAsync(batch.BatchId);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Retry);
    }

    [Fact]
    public async Task MoveToRetryAsync_FromAcknowledged_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch    = await AddBatch(db, BatchStatus.Acknowledged);

        var result = await sm.MoveToRetryAsync(batch.BatchId);

        result.Should().BeFalse();
    }
}
