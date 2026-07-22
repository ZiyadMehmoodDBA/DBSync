using FluentAssertions;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Cluster;

public sealed class ClusterSummaryQueryServiceTests : IDisposable
{
    private readonly global::MSOSync.Persistence.AppDbContext _db = TestDbContext.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetSummaryAsync_empty_db_returns_zero_counts()
    {
        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();
        result.NodeStates.Total.Should().Be(0);
        result.OperationCounts.Running.Should().Be(0);
        result.ActiveOperations.Should().BeEmpty();
        result.ActiveRollingOps.Should().BeEmpty();
        result.ActiveReplays.Should().BeEmpty();
        result.RecentNodeChanges.Should().BeEmpty();
    }

    private static SyncNode MakeNode(string nodeId, NodeLifecycleState state, bool maintenanceMode = false)
        => new SyncNode { NodeId = nodeId, NodeName = nodeId, GroupId = "g1", SyncUrl = "http://n",
            LifecycleState = state, MaintenanceMode = maintenanceMode, TenantId = Guid.Empty };

    [Fact]
    public async Task GetSummaryAsync_counts_active_nodes_correctly()
    {
        _db.Nodes.Add(MakeNode("n1", NodeLifecycleState.Active, maintenanceMode: false));
        _db.Nodes.Add(MakeNode("n2", NodeLifecycleState.Active, maintenanceMode: true));
        _db.Nodes.Add(MakeNode("n3", NodeLifecycleState.Draining, maintenanceMode: false));
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.NodeStates.Total.Should().Be(3);
        result.NodeStates.Active.Should().Be(1);
        result.NodeStates.Maintenance.Should().Be(1);
        result.NodeStates.Draining.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_returns_active_operations()
    {
        _db.Operations.Add(new SyncOperation
        {
            OperationId = Guid.NewGuid(), OperationType = "BatchReplay",
            Status = "Running", Source = "Worker",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CanCancel = true, CanRetry = false, TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.OperationCounts.Running.Should().Be(1);
        result.ActiveOperations.Should().HaveCount(1);
        result.ActiveOperations[0].Type.Should().Be("BatchReplay");
    }

    [Fact]
    public async Task GetSummaryAsync_rolling_ops_include_wave_progress()
    {
        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "RollingMaintenance",
            Status = "Running", Source = "User",
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CanCancel = true, CanRetry = false, TenantId = Guid.Empty,
        });
        _db.OperationSteps.Add(new SyncOperationStep
        {
            StepId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", WaveNumber = 1, Status = "Completed",
            TenantId = Guid.Empty,
        });
        _db.OperationSteps.Add(new SyncOperationStep
        {
            StepId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n2", WaveNumber = 2, Status = "Running",
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.ActiveRollingOps.Should().HaveCount(1);
        result.ActiveRollingOps[0].NodesDone.Should().Be(1);
        result.ActiveRollingOps[0].NodesTotal.Should().Be(2);
        result.ActiveRollingOps[0].TotalWaves.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_replay_ops_include_item_progress()
    {
        var opId = Guid.NewGuid();
        var replayId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "BatchReplay",
            Status = "Running", Source = "User",
            StartedAt = DateTime.UtcNow.AddMinutes(-3),
            CanCancel = true, CanRetry = false, TenantId = Guid.Empty,
        });
        _db.ReplayRequests.Add(new SyncReplayRequest
        {
            ReplayId = replayId, OperationId = opId,
            NodeId = "n1", ReplayMode = "FailedDelivery",
            FromTime = DateTime.UtcNow.AddDays(-1),
            ToTime = DateTime.UtcNow, TenantId = Guid.Empty,
        });
        _db.ReplayItems.Add(new SyncReplayItem
        {
            ItemId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ChannelId = "ch1", EventCount = 5,
            Status = "Completed", TenantId = Guid.Empty,
        });
        _db.ReplayItems.Add(new SyncReplayItem
        {
            ItemId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ChannelId = "ch2", EventCount = 3,
            Status = "Failed", TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.ActiveReplays.Should().HaveCount(1);
        result.ActiveReplays[0].ItemsDone.Should().Be(1);
        result.ActiveReplays[0].ItemsFailed.Should().Be(1);
        result.ActiveReplays[0].ItemsTotal.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_recent_node_changes_within_15_minutes_only()
    {
        // Seed nodes first to satisfy FK constraint
        _db.Nodes.Add(MakeNode("n1", NodeLifecycleState.Active));
        _db.Nodes.Add(MakeNode("n2", NodeLifecycleState.Draining));
        await _db.SaveChangesAsync();

        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            HistoryId = 1, NodeId = "n1",
            ToState = NodeLifecycleState.Active,
            Trigger = LifecycleTrigger.Manual,
            Actor = "admin",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            TenantId = Guid.Empty,
        });
        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            HistoryId = 2, NodeId = "n2",
            ToState = NodeLifecycleState.Draining,
            Trigger = LifecycleTrigger.Manual,
            Actor = "admin",
            OccurredAt = DateTimeOffset.UtcNow.AddHours(-2), // too old
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.RecentNodeChanges.Should().HaveCount(1);
        result.RecentNodeChanges[0].NodeId.Should().Be("n1");
    }

    [Fact]
    public async Task GetSummaryAsync_counts_operations_succeeded_today()
    {
        var todayMidnight = DateTime.UtcNow.Date;
        _db.Operations.Add(new SyncOperation
        {
            OperationId = Guid.NewGuid(), OperationType = "Export",
            Status = "Completed", Result = "Success", Source = "User",
            StartedAt = todayMidnight.AddHours(1),
            CompletedAt = todayMidnight.AddHours(2),
            CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
        });
        _db.Operations.Add(new SyncOperation
        {
            OperationId = Guid.NewGuid(), OperationType = "Export",
            Status = "Completed", Result = "Success", Source = "User",
            StartedAt = DateTime.UtcNow.AddDays(-2), // yesterday — excluded
            CompletedAt = DateTime.UtcNow.AddDays(-2).AddHours(1),
            CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.OperationCounts.SucceededToday.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_active_ops_capped_at_50()
    {
        for (var i = 0; i < 60; i++)
        {
            _db.Operations.Add(new SyncOperation
            {
                OperationId = Guid.NewGuid(), OperationType = "Export",
                Status = "Running", Source = "Worker",
                StartedAt = DateTime.UtcNow.AddMinutes(-i),
                CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
            });
        }
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.ActiveOperations.Count.Should().Be(50);
    }

    [Fact]
    public async Task GetSummaryAsync_offline_nodes_bucket_decommissioned_and_others()
    {
        _db.Nodes.Add(MakeNode("n1", NodeLifecycleState.Decommissioned));
        _db.Nodes.Add(MakeNode("n2", NodeLifecycleState.PendingApproval));
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.NodeStates.Offline.Should().Be(2);
    }
}
