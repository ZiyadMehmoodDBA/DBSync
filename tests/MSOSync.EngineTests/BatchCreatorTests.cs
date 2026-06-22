// tests/MSOSync.EngineTests/BatchCreatorTests.cs
using FluentAssertions;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class BatchCreatorTests
{
    private static BatchCreator CreateService(out AppDbContext db, out FakeClock clock)
    {
        db = TestDbContext.Create();
        clock = new FakeClock();
        db.Channels.Add(new SyncChannel
        {
            ChannelId = "default", Priority = 1,
            BatchSize = 1000, MaxBatchToSend = 100, MaxDataSize = 1048576L
        });
        db.SaveChanges();
        return new BatchCreator(db, clock);
    }

    private static SyncDataEvent MakeEvent(long id, string triggerId = "t1",
        string channelId = "default", long txId = 1, string? rowData = null) =>
        new()
        {
            EventId       = id,
            TriggerId     = triggerId,
            SourceNodeId  = "hub",
            ChannelId     = channelId,
            EventType     = 'I',
            TableName     = "dbo.T",
            TransactionId = txId,
            RowData       = rowData ?? "{}",
            CreateTime    = DateTime.UtcNow,
            IsProcessed   = false
        };

    [Fact]
    public async Task CreateBatchesAsync_EmptyEvents_ReturnsEmpty()
    {
        var svc = CreateService(out _, out _);
        var result = await svc.CreateBatchesAsync([], new Dictionary<long, IReadOnlyList<string>>());
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CreateBatchesAsync_SingleEvent_CreatesBatch()
    {
        var svc = CreateService(out var db, out _);
        var evt = MakeEvent(1);
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();

        var routes = new Dictionary<long, IReadOnlyList<string>> { [1L] = ["node-b"] };
        var result = await svc.CreateBatchesAsync([evt], routes);

        result.Should().HaveCount(1);
        result[0].NodeId.Should().Be("node-b");
        result[0].ChannelId.Should().Be("default");
        result[0].RowCount.Should().Be(1);
    }

    [Fact]
    public async Task CreateBatchesAsync_MarksEventsProcessed()
    {
        var svc = CreateService(out var db, out _);
        var evt = MakeEvent(1);
        db.DataEvents.Add(evt);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var routes = new Dictionary<long, IReadOnlyList<string>> { [1L] = ["node-b"] };
        await svc.CreateBatchesAsync([evt], routes);

        var refreshed = await db.DataEvents.FindAsync(1L);
        refreshed!.IsProcessed.Should().BeTrue();
    }

    [Fact]
    public async Task CreateBatchesAsync_TransactionBoundaryNeverSplit()
    {
        var svc = CreateService(out var db, out _);
        // Channel allows max 2 rows per batch, but tx has 3 rows — stays together
        db.Channels.First().MaxBatchToSend = 2;
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var events = Enumerable.Range(1, 3)
            .Select(i => MakeEvent(i, txId: 1))  // same transaction
            .ToList();
        db.DataEvents.AddRange(events);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var routes = events.ToDictionary(
            e => e.EventId,
            _ => (IReadOnlyList<string>)["node-b"]);

        var result = await svc.CreateBatchesAsync(events, routes);

        result.Should().HaveCount(1);
        result[0].RowCount.Should().Be(3);
    }

    [Fact]
    public async Task CreateBatchesAsync_DifferentTargetNodes_CreatesSeparateBatches()
    {
        var svc = CreateService(out var db, out _);
        var e1 = MakeEvent(1, txId: 1);
        var e2 = MakeEvent(2, txId: 2);
        db.DataEvents.AddRange(e1, e2);
        await db.SaveChangesAsync();

        var routes = new Dictionary<long, IReadOnlyList<string>>
        {
            [1L] = ["node-a"],
            [2L] = ["node-b"]
        };

        var result = await svc.CreateBatchesAsync([e1, e2], routes);

        result.Should().HaveCount(2);
        result.Select(b => b.NodeId).Should().Contain("node-a").And.Contain("node-b");
    }
}
