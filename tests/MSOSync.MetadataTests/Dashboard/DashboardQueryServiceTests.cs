using FluentAssertions;
using MSOSync.Metadata.Dashboard;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Dashboard;

public sealed class DashboardQueryServiceTests : IDisposable
{
    private readonly AppDbContext          _db;
    private readonly DashboardQueryService _sut;

    public DashboardQueryServiceTests()
    {
        _db  = TestDbContext.Create();
        _sut = new DashboardQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    private static SyncNode Node(string id, ConnectivityStatus cs) => new()
    {
        NodeId = id, GroupId = "g1", SyncUrl = $"http://{id}", Status = "REGISTERED",
        ConnectivityStatus = cs
    };

    private static SyncOutgoingBatch OutBatch(long id, string nodeId, byte status) => new()
    {
        BatchId = id, BatchSequence = id, NodeId = nodeId, ChannelId = "ch1", Status = status
    };

    // ── GetSummaryAsync ───────────────────────────────────────

    [Fact]
    public async Task GetSummary_CountsNodesByStatus()
    {
        _db.Nodes.AddRange(
            Node("n1", ConnectivityStatus.Reachable),
            Node("n2", ConnectivityStatus.Reachable),
            Node("n3", ConnectivityStatus.Degraded),
            Node("n4", ConnectivityStatus.Unreachable),
            Node("n5", ConnectivityStatus.Unknown));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(default);

        result.TotalNodes.Should().Be(5);
        result.ReachableNodes.Should().Be(2);
        result.DegradedNodes.Should().Be(1);
        result.UnreachableNodes.Should().Be(1);
        result.UnknownNodes.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_QueueDepth_ExcludesAcknowledged()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        _db.OutgoingBatches.AddRange(
            OutBatch(1, "n1", 0),  // New = pending
            OutBatch(2, "n1", 1),  // Sending = pending
            OutBatch(3, "n1", 2)); // Acknowledged = excluded
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(default);

        result.QueueDepth.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_PendingEvents_CountsUnprocessed()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        _db.DataEvents.AddRange(
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch1", EventType = 'I', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch1", EventType = 'U', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = true  });
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(default);

        result.PendingEvents.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_TransportErrors24h_CountsRecent()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        var ob = OutBatch(1, "n1", 0);
        _db.OutgoingBatches.Add(ob);
        await _db.SaveChangesAsync();
        _db.BatchErrors.AddRange(
            new SyncBatchError { BatchId = ob.BatchId, CreateTime = DateTime.UtcNow.AddHours(-12) },
            new SyncBatchError { BatchId = ob.BatchId, CreateTime = DateTime.UtcNow.AddHours(-30) }); // outside 24h
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(default);

        result.TransportErrors24h.Should().Be(1);
    }

    // ── GetActivityAsync ──────────────────────────────────────

    [Fact]
    public async Task GetActivity_ReturnsBothTypes_SortedByTime()
    {
        _db.Audits.AddRange(
            new SyncAudit { AuditId = 1, ActionName = "UPDATE", ObjectName = "SyncNode", CreateTime = DateTime.UtcNow.AddMinutes(-10) },
            new SyncAudit { AuditId = 2, ActionName = "DELETE", ObjectName = "SyncTrigger", CreateTime = DateTime.UtcNow.AddMinutes(-5) });
        await _db.SaveChangesAsync();

        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        var ob = OutBatch(10, "n1", 0);
        _db.OutgoingBatches.Add(ob);
        await _db.SaveChangesAsync();
        _db.BatchErrors.Add(new SyncBatchError { BatchId = ob.BatchId, ErrorMessage = "conflict", CreateTime = DateTime.UtcNow.AddMinutes(-3) });
        await _db.SaveChangesAsync();

        var result = await _sut.GetActivityAsync(new ActivityFilter(), default);

        result.Should().HaveCount(3);
        result[0].Type.Should().Be("batch_error"); // newest = -3 min
        result[1].Type.Should().Be("audit");        // -5 min
        result[2].Type.Should().Be("audit");        // -10 min
    }

    [Fact]
    public async Task GetActivity_FilterByType_ReturnsOnlyAudit()
    {
        _db.Audits.Add(new SyncAudit { AuditId = 1, ActionName = "CREATE", ObjectName = "Node", CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        var ob = OutBatch(10, "n1", 0);
        _db.OutgoingBatches.Add(ob);
        await _db.SaveChangesAsync();
        _db.BatchErrors.Add(new SyncBatchError { BatchId = ob.BatchId, CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetActivityAsync(new ActivityFilter { Type = "audit" }, default);

        result.Should().HaveCount(1);
        result[0].Type.Should().Be("audit");
    }

    [Fact]
    public async Task GetActivity_Limit_TruncatesResults()
    {
        for (int i = 1; i <= 10; i++)
            _db.Audits.Add(new SyncAudit { AuditId = i, ActionName = "OP", ObjectName = "T", CreateTime = DateTime.UtcNow.AddMinutes(-i) });
        await _db.SaveChangesAsync();

        var result = await _sut.GetActivityAsync(new ActivityFilter { Limit = 3 }, default);

        result.Should().HaveCount(3);
    }

    [Fact]
    public async Task GetActivity_ErrorMessage_TruncatedAt200Chars()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        var ob = OutBatch(10, "n1", 0);
        _db.OutgoingBatches.Add(ob);
        await _db.SaveChangesAsync();
        var longMessage = new string('X', 300);
        _db.BatchErrors.Add(new SyncBatchError { BatchId = ob.BatchId, ErrorMessage = longMessage, CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetActivityAsync(new ActivityFilter { Type = "batch_error" }, default);

        result.Should().HaveCount(1);
        result[0].Detail!.Length.Should().Be(200);
    }
}
