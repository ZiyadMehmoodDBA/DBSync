using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Api.Health;
using MSOSync.Batch;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ApiTests.Health;

public sealed class HealthScoringServiceTests : IDisposable
{
    private readonly AppDbContext _db;

    public HealthScoringServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetScoresAsync_ReturnsScore_ForEachNode()
    {
        // Seed a reachable node with recent heartbeat
        _db.Nodes.Add(new SyncNode
        {
            NodeId              = "node-1",
            NodeName            = "Node A",
            GroupId             = "g1",
            SyncUrl             = "https://node-a/sync",
            ConnectivityStatus  = ConnectivityStatus.Reachable,
            LastHeartbeat       = DateTime.UtcNow.AddMinutes(-2),
        });
        await _db.SaveChangesAsync();

        var svc    = new HealthScoringService(_db);
        var scores = await svc.GetScoresAsync();

        scores.Should().ContainSingle(s => s.NodeId == "node-1");
        scores[0].ConnectivityScore.Should().Be(40); // Reachable = full score
        scores[0].HeartbeatScore.Should().Be(10);    // heartbeat < 5 min = full score
    }

    [Fact]
    public async Task GetScoresAsync_Score0Connectivity_WhenNodeUnreachable()
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId              = "node-2",
            NodeName            = "Node B",
            GroupId             = "g1",
            SyncUrl             = "https://node-b/sync",
            ConnectivityStatus  = ConnectivityStatus.Unreachable,
            LastHeartbeat       = DateTime.UtcNow.AddHours(-2),
        });
        await _db.SaveChangesAsync();

        var svc    = new HealthScoringService(_db);
        var scores = await svc.GetScoresAsync();

        scores.Single(s => s.NodeId == "node-2").ConnectivityScore.Should().Be(0);
    }

    [Fact]
    public async Task GetScoresAsync_Score20Connectivity_WhenNodeDegraded()
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId              = "node-3",
            NodeName            = "Node C",
            GroupId             = "g1",
            SyncUrl             = "https://node-c/sync",
            ConnectivityStatus  = ConnectivityStatus.Degraded,
            LastHeartbeat       = null,
        });
        await _db.SaveChangesAsync();

        var svc    = new HealthScoringService(_db);
        var scores = await svc.GetScoresAsync();

        scores.Single(s => s.NodeId == "node-3").ConnectivityScore.Should().Be(20);
    }

    [Fact]
    public async Task GetScoresAsync_ErrorRateScore0_WhenHighErrorRate()
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId              = "node-4",
            NodeName            = "Node D",
            GroupId             = "g1",
            SyncUrl             = "https://node-d/sync",
            ConnectivityStatus  = ConnectivityStatus.Reachable,
            LastHeartbeat       = DateTime.UtcNow.AddMinutes(-1),
        });
        // Seed batches: 8 errors out of 10 = 80 % error rate → score 0
        for (var i = 1; i <= 10; i++)
        {
            _db.OutgoingBatches.Add(new SyncOutgoingBatch
            {
                BatchId       = i,
                BatchSequence = i,
                NodeId        = "node-4",
                ChannelId     = "ch1",
                Status        = (byte)(i <= 8 ? BatchStatus.Error : BatchStatus.Acknowledged),
                CreateTime    = DateTime.UtcNow.AddMinutes(-30),
                AckTime       = i > 8 ? DateTime.UtcNow.AddMinutes(-5) : null,
            });
        }
        await _db.SaveChangesAsync();

        var svc    = new HealthScoringService(_db);
        var scores = await svc.GetScoresAsync();

        scores.Single(s => s.NodeId == "node-4").ErrorRateScore.Should().Be(0);
    }

    [Fact]
    public async Task GetScoresAsync_ErrorRateScore20_WhenNoBatches()
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId              = "node-5",
            NodeName            = "Node E",
            GroupId             = "g1",
            SyncUrl             = "https://node-e/sync",
            ConnectivityStatus  = ConnectivityStatus.Reachable,
            LastHeartbeat       = DateTime.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();

        var svc    = new HealthScoringService(_db);
        var scores = await svc.GetScoresAsync();

        scores.Single(s => s.NodeId == "node-5").ErrorRateScore.Should().Be(20);
    }

    [Fact]
    public async Task GetScoreAsync_ReturnsNull_WhenNodeNotFound()
    {
        var svc    = new HealthScoringService(_db);
        var result = await svc.GetScoreAsync("nonexistent-node");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetScoreAsync_ReturnsScore_ForKnownNode()
    {
        _db.Nodes.Add(new SyncNode
        {
            NodeId              = "node-6",
            NodeName            = "Node F",
            GroupId             = "g1",
            SyncUrl             = "https://node-f/sync",
            ConnectivityStatus  = ConnectivityStatus.Reachable,
            LastHeartbeat       = DateTime.UtcNow.AddMinutes(-1),
        });
        await _db.SaveChangesAsync();

        var svc    = new HealthScoringService(_db);
        var result = await svc.GetScoreAsync("node-6");

        result.Should().NotBeNull();
        result!.NodeId.Should().Be("node-6");
    }

    [Fact]
    public void ComputeGrade_ReturnsCorrectGrade()
    {
        NodeHealthScore.ComputeGrade(95).Should().Be("A");
        NodeHealthScore.ComputeGrade(80).Should().Be("B");
        NodeHealthScore.ComputeGrade(60).Should().Be("C");
        NodeHealthScore.ComputeGrade(30).Should().Be("D");
        NodeHealthScore.ComputeGrade(10).Should().Be("F");
    }
}
