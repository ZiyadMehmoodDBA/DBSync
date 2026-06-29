using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using MSOSync.Metadata.Metrics;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Metrics;

public sealed class MetricsQueryServiceTests : IDisposable
{
    private readonly AppDbContext        _db;
    private readonly IMemoryCache        _cache;
    private readonly MetricsQueryService _sut;

    public MetricsQueryServiceTests()
    {
        _db    = TestDbContext.Create();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _sut   = new MetricsQueryService(_db, _cache);
    }

    public void Dispose() { _db.Dispose(); _cache.Dispose(); }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static SyncNode Node(string id, ConnectivityStatus cs) => new()
    {
        NodeId  = id,
        GroupId = "g1",
        SyncUrl = $"http://{id}",
        Status  = "REGISTERED",
        ConnectivityStatus = cs
    };

    private static SyncOutgoingBatch OutBatch(long id, string nodeId, string channelId, byte status) => new()
    {
        BatchId       = id,
        BatchSequence = id,
        NodeId        = nodeId,
        ChannelId     = channelId,
        Status        = status
    };

    private static SyncIncomingBatch InBatch(long id, string nodeId, string channelId,
        IncomingBatchStatus status, DateTime? appliedTime = null, long? applyTimeMs = null) => new()
    {
        BatchId       = id,
        BatchSequence = id,
        NodeId        = nodeId,
        SourceNodeId  = nodeId,
        ChannelId     = channelId,
        Status        = status,
        ReceivedTime  = DateTime.UtcNow.AddHours(-2),
        AppliedTime   = appliedTime,
        ApplyTimeMs   = applyTimeMs
    };

    // ── GetSummaryAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetSummary_ReachableNodesCount()
    {
        _db.Nodes.AddRange(
            Node("n1", ConnectivityStatus.Reachable),
            Node("n2", ConnectivityStatus.Reachable),
            Node("n3", ConnectivityStatus.Unreachable));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.TotalNodes.Should().Be(3);
        result.ReachableNodes.Should().Be(2);
        result.UnreachableNodes.Should().Be(1);
        result.DegradedNodes.Should().Be(0);
        result.UnknownNodes.Should().Be(0);
    }

    [Fact]
    public async Task GetSummary_IncomingQueueDepth()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        _db.IncomingBatches.AddRange(
            InBatch(1L, "n1", "ch-1", IncomingBatchStatus.New),
            InBatch(2L, "n1", "ch-1", IncomingBatchStatus.Applying),
            InBatch(3L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-1)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.IncomingQueueDepth.Should().Be(2);
    }

    [Fact]
    public async Task GetSummary_Errors24h_ExcludesOlderErrors()
    {
        _db.OutgoingBatches.Add(OutBatch(1L, "n1", "ch-1", 2));
        await _db.SaveChangesAsync();

        _db.BatchErrors.AddRange(
            new SyncBatchError { BatchId = 1L, ErrorMessage = "old",    CreateTime = DateTime.UtcNow.AddHours(-25) },
            new SyncBatchError { BatchId = 1L, ErrorMessage = "recent", CreateTime = DateTime.UtcNow.AddHours(-1) });
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.Errors24h.Should().Be(1);
    }

    [Fact]
    public async Task GetSummary_ErrorRatePercent_ZeroIfNoActivity()
    {
        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.ErrorRatePercent.Should().Be(0.0);
        result.ThroughputPerMinute.Should().Be(0.0);
    }

    [Fact]
    public async Task GetSummary_ThroughputPerMinute()
    {
        // 144 batches applied → Math.Round(144 / 1440.0, 2) = 0.1
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        var batches = Enumerable.Range(1, 144).Select(i =>
            InBatch(100 + i, "n1", "ch-1", IncomingBatchStatus.Applied,
                    appliedTime: DateTime.UtcNow.AddHours(-1))).ToList();
        _db.IncomingBatches.AddRange(batches);
        await _db.SaveChangesAsync();

        var result = await _sut.GetSummaryAsync(CancellationToken.None);

        result.BatchesProcessed24h.Should().Be(144);
        result.ThroughputPerMinute.Should().Be(0.1);
    }

    // ── GetNodeMetricsAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetNodeMetrics_ReturnsAllNodes()
    {
        _db.Nodes.AddRange(
            Node("n1", ConnectivityStatus.Reachable),
            Node("n2", ConnectivityStatus.Degraded));
        await _db.SaveChangesAsync();

        var result = await _sut.GetNodeMetricsAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result.Select(x => x.NodeId).Should().BeEquivalentTo(new[] { "n1", "n2" });
    }

    [Fact]
    public async Task GetNodeMetrics_ProcessedBatches24h()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        _db.IncomingBatches.AddRange(
            InBatch(1L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-1)),
            InBatch(2L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-2)),
            InBatch(3L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-25)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetNodeMetricsAsync(CancellationToken.None);

        result.Single(x => x.NodeId == "n1").ProcessedBatches24h.Should().Be(2);
    }

    [Fact]
    public async Task GetNodeMetrics_AvgApplyTimeMs_Nullable()
    {
        _db.Nodes.Add(Node("n1", ConnectivityStatus.Reachable));
        _db.IncomingBatches.Add(InBatch(1L, "n1", "ch-1", IncomingBatchStatus.New));
        await _db.SaveChangesAsync();

        var result = await _sut.GetNodeMetricsAsync(CancellationToken.None);

        result.Single().AvgApplyTimeMs.Should().BeNull();
    }

    // ── GetChannelMetricsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetChannelMetrics_PendingEvents()
    {
        _db.DataEvents.AddRange(
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch-1", EventType = 'I', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch-1", EventType = 'U', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = false },
            new SyncDataEvent { TriggerId = "t1", SourceNodeId = "n1", ChannelId = "ch-1", EventType = 'D', TableName = "T", CreateTime = DateTime.UtcNow, IsProcessed = true });
        await _db.SaveChangesAsync();

        var result = await _sut.GetChannelMetricsAsync(CancellationToken.None);

        result.Single(x => x.ChannelId == "ch-1").PendingEvents.Should().Be(2);
    }

    [Fact]
    public async Task GetChannelMetrics_ActiveNodes()
    {
        // 3 distinct nodes with batches applied within 24h; 1 outside 24h window
        _db.Nodes.AddRange(
            Node("n1", ConnectivityStatus.Reachable),
            Node("n2", ConnectivityStatus.Reachable),
            Node("n3", ConnectivityStatus.Reachable),
            Node("n4", ConnectivityStatus.Reachable));
        await _db.SaveChangesAsync();
        _db.IncomingBatches.AddRange(
            InBatch(1L, "n1", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-1)),
            InBatch(2L, "n2", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-2)),
            InBatch(3L, "n3", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-3)),
            InBatch(4L, "n4", "ch-1", IncomingBatchStatus.Applied, appliedTime: DateTime.UtcNow.AddHours(-25)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetChannelMetricsAsync(CancellationToken.None);

        result.Single(x => x.ChannelId == "ch-1").ActiveNodes.Should().Be(3);
    }

    // ── GetRuntimeMetricsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetRuntimeMetrics_ReturnsAllRows_OrderedDescending()
    {
        var older = DateTime.UtcNow.AddHours(-2);
        var newer = DateTime.UtcNow.AddHours(-1);
        _db.RuntimeStats.AddRange(
            new SyncRuntimeStats { HeapUsed = 100L, CreateTime = older },
            new SyncRuntimeStats { HeapUsed = 200L, CreateTime = newer });
        await _db.SaveChangesAsync();

        var result = await _sut.GetRuntimeMetricsAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].CreateTime.Should().BeCloseTo(newer, TimeSpan.FromSeconds(5));
        result[1].CreateTime.Should().BeCloseTo(older, TimeSpan.FromSeconds(5));
    }

    // ── GetMonitorMetricsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task GetMonitorMetrics_FiltersOnMetricName()
    {
        _db.Monitors.AddRange(
            new SyncMonitor { NodeId = "n1", MetricName = "cpu",    MetricValue = "50", CreateTime = DateTime.UtcNow },
            new SyncMonitor { NodeId = "n1", MetricName = "cpu",    MetricValue = "60", CreateTime = DateTime.UtcNow },
            new SyncMonitor { NodeId = "n1", MetricName = "memory", MetricValue = "70", CreateTime = DateTime.UtcNow });
        await _db.SaveChangesAsync();

        var result = await _sut.GetMonitorMetricsAsync(null, "cpu", CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().AllSatisfy(x => x.MetricName.Should().Be("cpu"));
    }
}
