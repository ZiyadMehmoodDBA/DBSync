// tests/MSOSync.EngineTests/BatchStateMachineTests.cs
using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchStateMachineTests
{
    private static (BatchStateMachine Sm, AppDbContext Db) Create()
    {
        var db = TestDbContext.Create();
        return (new BatchStateMachine(db), db);
    }

    private static SyncOutgoingBatch MakeBatch(BatchStatus status)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1,
            NodeId        = "hub",
            ChannelId     = "default",
            Status        = (byte)status
        };
        return b;
    }

    [Theory]
    [InlineData(BatchStatus.New,   BatchStatus.Sent)]
    [InlineData(BatchStatus.Sent,  BatchStatus.Ok)]
    [InlineData(BatchStatus.Sent,  BatchStatus.Error)]
    [InlineData(BatchStatus.Error, BatchStatus.Retry)]
    [InlineData(BatchStatus.Retry, BatchStatus.Sent)]
    [InlineData(BatchStatus.Retry, BatchStatus.Error)]
    public void CanTransition_ValidPairs_ReturnsTrue(BatchStatus from, BatchStatus to)
    {
        var (sm, _) = Create();
        sm.CanTransition(from, to).Should().BeTrue();
    }

    [Theory]
    [InlineData(BatchStatus.New,   BatchStatus.Ok)]
    [InlineData(BatchStatus.New,   BatchStatus.Error)]
    [InlineData(BatchStatus.Ok,    BatchStatus.Sent)]
    [InlineData(BatchStatus.Error, BatchStatus.Ok)]
    public void CanTransition_InvalidPairs_ReturnsFalse(BatchStatus from, BatchStatus to)
    {
        var (sm, _) = Create();
        sm.CanTransition(from, to).Should().BeFalse();
    }

    [Fact]
    public async Task TransitionAsync_ValidTransition_ReturnsTrue()
    {
        var (sm, db) = Create();
        var batch = MakeBatch(BatchStatus.New);
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var result = await sm.TransitionAsync(batch.BatchId, BatchStatus.New, BatchStatus.Sent);

        result.Should().BeTrue();
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Sent);
    }

    [Fact]
    public async Task TransitionAsync_WrongCurrentStatus_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch = MakeBatch(BatchStatus.Error);
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var result = await sm.TransitionAsync(batch.BatchId, BatchStatus.New, BatchStatus.Sent);

        result.Should().BeFalse();
        db.ChangeTracker.Clear();
        var unchanged = await db.OutgoingBatches.FindAsync(batch.BatchId);
        unchanged!.Status.Should().Be((byte)BatchStatus.Error);
    }

    [Fact]
    public async Task TransitionAsync_InvalidTransitionPair_ReturnsFalse()
    {
        var (sm, db) = Create();
        var batch = MakeBatch(BatchStatus.New);
        db.OutgoingBatches.Add(batch);
        await db.SaveChangesAsync();

        var result = await sm.TransitionAsync(batch.BatchId, BatchStatus.New, BatchStatus.Ok);

        result.Should().BeFalse();
    }
}
