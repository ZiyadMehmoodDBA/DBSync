// tests/MSOSync.IntegrationTests/PersistenceTests.cs
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Queries;
using Xunit;

namespace MSOSync.IntegrationTests;

public sealed class PersistenceTests(DatabaseFixture fixture) : IClassFixture<DatabaseFixture>
{
    [Fact]
    public async Task CanConnect()
    {
        var result = await fixture.Db.Database.CanConnectAsync();
        result.Should().BeTrue();
    }

    [Fact]
    public async Task SchemaCreated_All23TablesExist()
    {
        var count = await fixture.Db.Database
            .SqlQuery<int>($"SELECT COUNT(1) AS Value FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'msosync'")
            .SingleAsync();
        count.Should().Be(23);
    }

    [Fact]
    public async Task SeedData_RolesPresent()
    {
        var roles = await fixture.Db.Roles.AsNoTracking().ToListAsync();
        roles.Should().HaveCount(3);
        roles.Select(r => r.RoleName).Should().BeEquivalentTo(new[] { "ADMIN", "OPERATOR", "VIEWER" });
    }

    [Fact]
    public async Task SeedData_DefaultChannelPresent()
    {
        var channel = await fixture.Db.Channels.AsNoTracking()
            .FirstOrDefaultAsync(c => c.ChannelId == "config");
        channel.Should().NotBeNull();
        channel!.Priority.Should().Be(100);
    }

    [Fact]
    public async Task SeedData_ParametersPresent()
    {
        var count = await fixture.Db.Parameters.AsNoTracking().CountAsync();
        count.Should().Be(6);

        var interval = await fixture.Db.Parameters.AsNoTracking()
            .FirstOrDefaultAsync(p => p.ParameterName == "sync.interval.seconds");
        interval.Should().NotBeNull();
        interval!.ParameterValue.Should().Be("900");
    }

    [Fact]
    public async Task MigrationIdempotency_SecondMigrateAsyncNoException()
    {
        await fixture.Db.Database.MigrateAsync();
        var roleCount = await fixture.Db.Roles.AsNoTracking().CountAsync();
        roleCount.Should().Be(3);
    }

    [Fact]
    public async Task ForeignKeyIntegrity_BatchErrorRequiresValidBatch()
    {
        fixture.Db.BatchErrors.Add(new SyncBatchError
        {
            BatchId = 999_999_999L,
            RetryCount = 0
        });

        var act = async () => await fixture.Db.SaveChangesAsync();
        await act.Should().ThrowAsync<DbUpdateException>();

        fixture.Db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task QueryObjects_GetOfflineNodes_ReturnsStaleRegisteredNodes()
    {
        var nodeId = $"offline-test-{Guid.NewGuid():N}";
        var node = new SyncNode
        {
            NodeId = nodeId,
            GroupId = "test-group",
            SyncUrl = "http://test:8080",
            Status = "REGISTERED",
            LastHeartbeat = DateTime.UtcNow.AddMinutes(-120)
        };
        fixture.Db.Nodes.Add(node);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetOfflineNodesQuery(fixture.Db);
            var results = await query.ExecuteAsync(thresholdMinutes: 60);
            results.Should().Contain(n => n.NodeId == nodeId);
        }
        finally
        {
            fixture.Db.Nodes.Remove(node);
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task QueryObjects_GetPendingBatches_ReturnsNewAndRetryOnly()
    {
        var channelId = $"ch-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var nodeId = $"node-{Guid.NewGuid().ToString("N").Substring(0, 8)}";

        var newBatch = new SyncOutgoingBatch
        {
            BatchSequence = 1, NodeId = nodeId, ChannelId = channelId,
            Status = 0  // NEW
        };
        var ackedBatch = new SyncOutgoingBatch
        {
            BatchSequence = 2, NodeId = nodeId, ChannelId = channelId,
            Status = 2  // ACKED
        };
        fixture.Db.OutgoingBatches.AddRange(newBatch, ackedBatch);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetPendingBatchesQuery(fixture.Db);
            var results = await query.ExecuteAsync(nodeId, channelId);
            results.Should().ContainSingle(b => b.Status == 0);
            results.Should().NotContain(b => b.Status == 2);
        }
        finally
        {
            fixture.Db.OutgoingBatches.RemoveRange(newBatch, ackedBatch);
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task QueryObjects_GetRetryCandidates_ReturnsEligibleErrorBatches()
    {
        var nodeId = $"node-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var channelId = "config";
        var eligible = new SyncOutgoingBatch
        {
            BatchSequence = 10, NodeId = nodeId, ChannelId = channelId,
            Status = 3,  // ERROR
            RetryCount = 1,
            NextRetryTime = DateTime.UtcNow.AddMinutes(-5)
        };
        fixture.Db.OutgoingBatches.Add(eligible);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetRetryCandidatesQuery(fixture.Db);
            var results = await query.ExecuteAsync(maxRetries: 3);
            results.Should().Contain(b => b.BatchId == eligible.BatchId);
        }
        finally
        {
            fixture.Db.OutgoingBatches.Remove(eligible);
            await fixture.Db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task QueryObjects_GetEventQueueDepth_CountsUnprocessedPerChannel()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var channelA = $"ch-a-{suffix}";
        var channelB = $"ch-b-{suffix}";

        var events = new[]
        {
            MakeEvent(channelA), MakeEvent(channelA), MakeEvent(channelA),
            MakeEvent(channelB)
        };
        fixture.Db.DataEvents.AddRange(events);
        await fixture.Db.SaveChangesAsync();

        try
        {
            var query = new GetEventQueueDepthQuery(fixture.Db);
            var depth = await query.ExecuteAsync();
            depth[channelA].Should().Be(3);
            depth[channelB].Should().Be(1);
        }
        finally
        {
            fixture.Db.DataEvents.RemoveRange(events);
            await fixture.Db.SaveChangesAsync();
        }
    }

    private static SyncDataEvent MakeEvent(string channelId) => new()
    {
        TriggerId = "t1",
        SourceNodeId = "hub",
        ChannelId = channelId,
        EventType = 'I',
        TableName = "test_table",
        CreateTime = DateTime.UtcNow,
        IsProcessed = false
    };
}
