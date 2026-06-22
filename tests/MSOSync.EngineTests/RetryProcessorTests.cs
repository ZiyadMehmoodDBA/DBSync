// tests/MSOSync.EngineTests/RetryProcessorTests.cs
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class RetryProcessorTests
{
    private static (RetryProcessor Processor, AppDbContext Db, FakeClock Clock) Create()
    {
        var db    = TestDbContext.Create();
        var clock = new FakeClock();
        var proc  = new RetryProcessor(db, clock, NullLogger<RetryProcessor>.Instance);
        return (proc, db, clock);
    }

    private static SyncOutgoingBatch MakeErrorBatch(AppDbContext db, int retryCount = 0, DateTime? nextRetry = null)
    {
        var b = new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = "hub", ChannelId = "default",
            Status = (byte)BatchStatus.Error, RetryCount = retryCount,
            NextRetryTime = nextRetry
        };
        db.OutgoingBatches.Add(b);
        db.SaveChanges();
        return b;
    }

    [Fact]
    public async Task ProcessAsync_NoEligibleBatches_ReturnsZero()
    {
        var (proc, _, _) = Create();
        var count = await proc.ProcessAsync();
        count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_EligibleErrorBatch_TransitionsToRetry()
    {
        var (proc, db, _) = Create();
        var batch = MakeErrorBatch(db);

        var count = await proc.ProcessAsync();

        count.Should().Be(1);
        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        updated!.Status.Should().Be((byte)BatchStatus.Retry);
    }

    [Fact]
    public async Task ProcessAsync_FutureNextRetryTime_Skips()
    {
        var (proc, db, clock) = Create();
        MakeErrorBatch(db, nextRetry: clock.UtcNow.AddHours(1));

        var count = await proc.ProcessAsync();

        count.Should().Be(0);
    }

    [Fact]
    public async Task ProcessAsync_FirstRetry_SetsDelay5Minutes()
    {
        var (proc, db, clock) = Create();
        var batch = MakeErrorBatch(db, retryCount: 0);

        await proc.ProcessAsync();

        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        var expectedDelay = TimeSpan.FromMinutes(5); // 2^0 * 5
        (updated!.NextRetryTime!.Value - clock.UtcNow).Should()
            .BeCloseTo(expectedDelay, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ProcessAsync_SecondRetry_SetsDelay10Minutes()
    {
        var (proc, db, clock) = Create();
        var batch = MakeErrorBatch(db, retryCount: 1);

        await proc.ProcessAsync();

        db.ChangeTracker.Clear();
        var updated = await db.OutgoingBatches.FindAsync(batch.BatchId);
        var expectedDelay = TimeSpan.FromMinutes(10); // 2^1 * 5
        (updated!.NextRetryTime!.Value - clock.UtcNow).Should()
            .BeCloseTo(expectedDelay, TimeSpan.FromSeconds(5));
    }
}
