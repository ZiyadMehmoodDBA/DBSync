using FluentAssertions;
using MSOSync.Metadata.Operations.Cluster.Recovery;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Cluster;

public sealed class RecoveryDashboardQueryServiceTests : IDisposable
{
    private readonly global::MSOSync.Persistence.AppDbContext _db = TestDbContext.Create();
    private readonly RecoveryDashboardQueryService _svc;

    public RecoveryDashboardQueryServiceTests()
    {
        _svc = new RecoveryDashboardQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetRecoveryDashboardAsync_NoRecoveryNodes_ReturnsEmptyActiveList()
    {
        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.ActiveRecoveries.Should().BeEmpty();
        result.Summary.ActiveCount.Should().Be(0);
        result.Summary.AvgRtoMinutes.Should().BeNull();
        result.Summary.MaxRtoMinutes.Should().BeNull();
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_NodeInRecovery_AppearsInActiveList()
    {
        var tenantId = Guid.Empty;
        var recoveryStart = DateTimeOffset.UtcNow.AddMinutes(-30);

        _db.Nodes.Add(new SyncNode
        {
            NodeId         = "rec-node-1",
            GroupId        = "grp",
            SyncUrl        = "http://rec.local",
            LifecycleState = NodeLifecycleState.Recovery,
            TenantId       = tenantId,
        });
        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId     = "rec-node-1",
            FromState  = NodeLifecycleState.Active,
            ToState    = NodeLifecycleState.Recovery,
            Trigger    = LifecycleTrigger.System,
            Actor      = "system",
            OccurredAt = recoveryStart,
            TenantId   = tenantId,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.ActiveRecoveries.Should().HaveCount(1);
        result.ActiveRecoveries[0].NodeId.Should().Be("rec-node-1");
        result.ActiveRecoveries[0].RecoveryStartedAt.Should().BeCloseTo(recoveryStart.UtcDateTime, TimeSpan.FromSeconds(1));
        result.ActiveRecoveries[0].ElapsedMinutes.Should().BeGreaterThan(25);
        result.Summary.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_CompletedRecovery_AppearsInCompleted()
    {
        var tenantId      = Guid.Empty;
        var recoveryStart = DateTimeOffset.UtcNow.AddDays(-2);
        var restored      = recoveryStart.AddMinutes(45);

        // A node now in Active, but had Recovery → Active transition recently
        _db.Nodes.Add(new SyncNode
        {
            NodeId         = "rec-node-2",
            GroupId        = "grp",
            SyncUrl        = "http://rec2.local",
            LifecycleState = NodeLifecycleState.Active,
            TenantId       = tenantId,
        });
        _db.NodeLifecycleHistories.AddRange(
            new SyncNodeLifecycleHistory { NodeId = "rec-node-2", FromState = NodeLifecycleState.Active,   ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, Actor = "system", OccurredAt = recoveryStart, TenantId = tenantId },
            new SyncNodeLifecycleHistory { NodeId = "rec-node-2", FromState = NodeLifecycleState.Recovery, ToState = NodeLifecycleState.Active,   Trigger = LifecycleTrigger.Manual, Actor = "admin",  OccurredAt = restored,      TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.RecentCompletedRecoveries.Should().HaveCount(1);
        result.RecentCompletedRecoveries[0].NodeId.Should().Be("rec-node-2");
        result.RecentCompletedRecoveries[0].RtoMinutes.Should().BeApproximately(45.0, 1.0);
        result.Summary.AvgRtoMinutes.Should().NotBeNull();
        result.Summary.MaxRtoMinutes.Should().BeApproximately(45.0, 1.0);
        result.Summary.CompletedLast30Days.Should().Be(1);
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_CompletedOlderThan30Days_NotInSummaryCount()
    {
        var tenantId = Guid.Empty;
        var recoveryStart = DateTimeOffset.UtcNow.AddDays(-35);
        var restored      = recoveryStart.AddMinutes(60);

        _db.Nodes.Add(new SyncNode { NodeId = "old-rec", GroupId = "grp", SyncUrl = "http://old.local", LifecycleState = NodeLifecycleState.Active, TenantId = tenantId });
        _db.NodeLifecycleHistories.AddRange(
            new SyncNodeLifecycleHistory { NodeId = "old-rec", FromState = NodeLifecycleState.Active,   ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, Actor = "system", OccurredAt = recoveryStart, TenantId = tenantId },
            new SyncNodeLifecycleHistory { NodeId = "old-rec", FromState = NodeLifecycleState.Recovery, ToState = NodeLifecycleState.Active,   Trigger = LifecycleTrigger.Manual, Actor = "admin",  OccurredAt = restored,      TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.Summary.CompletedLast30Days.Should().Be(0);
        result.RecentCompletedRecoveries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_NoCompletedRecoveries_NullAvgAndMax()
    {
        var result = await _svc.GetRecoveryDashboardAsync(default);
        result.Summary.AvgRtoMinutes.Should().BeNull();
        result.Summary.MaxRtoMinutes.Should().BeNull();
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_AssociatedReplayOps_LinkedToRecoveryNode()
    {
        var tenantId      = Guid.Empty;
        var recoveryStart = DateTimeOffset.UtcNow.AddHours(-2);

        _db.Nodes.Add(new SyncNode { NodeId = "rec-replay", GroupId = "grp", SyncUrl = "http://rr.local", LifecycleState = NodeLifecycleState.Recovery, TenantId = tenantId });
        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory { NodeId = "rec-replay", FromState = NodeLifecycleState.Active, ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, Actor = "system", OccurredAt = recoveryStart, TenantId = tenantId });

        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId   = opId,
            OperationType = "BatchReplay",
            Status        = "Running",
            Source        = "Worker",
            StartedAt     = recoveryStart.AddMinutes(5).UtcDateTime,
            TenantId      = tenantId,
        });
        _db.ReplayRequests.Add(new SyncReplayRequest
        {
            OperationId = opId,
            NodeId      = "rec-replay",
            ReplayMode  = "FailedDelivery",
            FromTime    = recoveryStart.UtcDateTime,
            ToTime      = recoveryStart.AddHours(1).UtcDateTime,
            TenantId    = tenantId,
        });
        _db.ReplayItems.Add(new SyncReplayItem { ItemId = Guid.NewGuid(), OperationId = opId, NodeId = "rec-replay", ChannelId = "ch1", Status = "Completed", TenantId = tenantId });
        _db.ReplayItems.Add(new SyncReplayItem { ItemId = Guid.NewGuid(), OperationId = opId, NodeId = "rec-replay", ChannelId = "ch1", Status = "Pending",   TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        var active = result.ActiveRecoveries.FirstOrDefault(r => r.NodeId == "rec-replay");
        active.Should().NotBeNull();
        active!.AssociatedReplayOps.Should().HaveCount(1);
        active.AssociatedReplayOps[0].OperationId.Should().Be(opId);
        active.AssociatedReplayOps[0].ItemsTotal.Should().Be(2);
        active.AssociatedReplayOps[0].ItemsDone.Should().Be(1);
    }

    /// <summary>
    /// Bug 1 regression: a StandardSync operation that has an associated SyncReplayRequest row
    /// must NOT be counted as a replay op — only BatchReplay operations qualify.
    /// </summary>
    [Fact]
    public async Task GetRecoveryDashboardAsync_NonBatchReplayOperation_ExcludedFromReplayOps()
    {
        var tenantId      = Guid.Empty;
        var recoveryStart = DateTimeOffset.UtcNow.AddHours(-2);

        _db.Nodes.Add(new SyncNode { NodeId = "replay-filter-node", GroupId = "grp", SyncUrl = "http://rf.local", LifecycleState = NodeLifecycleState.Recovery, TenantId = tenantId });
        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory { NodeId = "replay-filter-node", FromState = NodeLifecycleState.Active, ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, Actor = "system", OccurredAt = recoveryStart, TenantId = tenantId });

        // Valid BatchReplay operation — should appear in results
        var validOpId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId   = validOpId,
            OperationType = "BatchReplay",
            Status        = "Completed",
            Source        = "Worker",
            StartedAt     = recoveryStart.AddMinutes(5).UtcDateTime,
            TenantId      = tenantId,
        });
        _db.ReplayRequests.Add(new SyncReplayRequest
        {
            ReplayId    = Guid.NewGuid(),
            OperationId = validOpId,
            NodeId      = "replay-filter-node",
            ReplayMode  = "FailedDelivery",
            FromTime    = recoveryStart.UtcDateTime,
            ToTime      = recoveryStart.AddHours(1).UtcDateTime,
            TenantId    = tenantId,
        });

        // Non-BatchReplay operation that also has a SyncReplayRequest row — must be excluded
        var spuriousOpId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId   = spuriousOpId,
            OperationType = "StandardSync",
            Status        = "Completed",
            Source        = "Worker",
            StartedAt     = recoveryStart.AddMinutes(10).UtcDateTime,
            TenantId      = tenantId,
        });
        _db.ReplayRequests.Add(new SyncReplayRequest
        {
            ReplayId    = Guid.NewGuid(),
            OperationId = spuriousOpId,
            NodeId      = "replay-filter-node",
            ReplayMode  = "MissedData",
            FromTime    = recoveryStart.UtcDateTime,
            ToTime      = recoveryStart.AddHours(1).UtcDateTime,
            TenantId    = tenantId,
        });

        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        var active = result.ActiveRecoveries.FirstOrDefault(r => r.NodeId == "replay-filter-node");
        active.Should().NotBeNull();
        active!.AssociatedReplayOps.Should().HaveCount(1, "only BatchReplay ops should be included");
        active.AssociatedReplayOps[0].OperationId.Should().Be(validOpId);
    }

    /// <summary>
    /// Bug 2 regression: a Draining→Active transition must NOT be counted as a completed recovery.
    /// Only Recovery→Active transitions qualify.
    /// </summary>
    [Fact]
    public async Task GetRecoveryDashboardAsync_NonRecoveryToActiveTransition_NotCountedAsCompletedRecovery()
    {
        var tenantId   = Guid.Empty;
        var exitedAt   = DateTimeOffset.UtcNow.AddDays(-1);

        // Node that went Draining→Active — should NOT appear in completed recoveries
        _db.Nodes.Add(new SyncNode
        {
            NodeId         = "draining-node",
            GroupId        = "grp",
            SyncUrl        = "http://drain.local",
            LifecycleState = NodeLifecycleState.Active,
            TenantId       = tenantId,
        });
        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId     = "draining-node",
            FromState  = NodeLifecycleState.Draining,
            ToState    = NodeLifecycleState.Active,
            Trigger    = LifecycleTrigger.Manual,
            Actor      = "admin",
            OccurredAt = exitedAt,
            TenantId   = tenantId,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.RecentCompletedRecoveries.Should().NotContain(r => r.NodeId == "draining-node",
            "Draining→Active transitions must not be treated as completed recoveries");
        result.Summary.CompletedLast30Days.Should().Be(0);
    }
}
