using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class NodeSyncPolicyTests
{
    private readonly NodeSyncPolicy _sut = new();

    private static SyncNode Node(
        NodeLifecycleState state = NodeLifecycleState.Active,
        bool maintenance = false) =>
        new()
        {
            NodeId         = "n1",
            GroupId        = "g1",
            SyncUrl        = "http://n1",
            LifecycleState = state,
            MaintenanceMode = maintenance
        };

    // ── CanSynchronize ────────────────────────────────────────────────────

    [Fact]
    public void CanSynchronize_ActiveNotInMaintenance_ReturnsTrue()
    {
        _sut.CanSynchronize(Node(NodeLifecycleState.Active, false)).Should().BeTrue();
    }

    [Fact]
    public void CanSynchronize_ActiveInMaintenance_ReturnsFalse()
    {
        _sut.CanSynchronize(Node(NodeLifecycleState.Active, true)).Should().BeFalse();
    }

    [Theory]
    [InlineData(NodeLifecycleState.Disabled)]
    [InlineData(NodeLifecycleState.PendingApproval)]
    [InlineData(NodeLifecycleState.PendingRegistration)]
    [InlineData(NodeLifecycleState.Recovery)]
    [InlineData(NodeLifecycleState.Decommissioning)]
    [InlineData(NodeLifecycleState.Decommissioned)]
    [InlineData(NodeLifecycleState.Rejected)]
    public void CanSynchronize_NonActiveState_ReturnsFalse(NodeLifecycleState state)
    {
        _sut.CanSynchronize(Node(state, false)).Should().BeFalse();
    }

    // ── Evaluate ──────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_ActiveNotInMaintenance_ReturnsAllowed()
    {
        _sut.Evaluate(Node(NodeLifecycleState.Active, false))
            .Should().Be(SyncEligibility.Allowed);
    }

    [Fact]
    public void Evaluate_ActiveInMaintenance_ReturnsBlockedByMaintenance()
    {
        _sut.Evaluate(Node(NodeLifecycleState.Active, true))
            .Should().Be(SyncEligibility.BlockedByMaintenance);
    }

    [Theory]
    [InlineData(NodeLifecycleState.Decommissioning)]
    [InlineData(NodeLifecycleState.Decommissioned)]
    public void Evaluate_DecommissionState_ReturnsBlockedByDecommission(NodeLifecycleState state)
    {
        _sut.Evaluate(Node(state, false))
            .Should().Be(SyncEligibility.BlockedByDecommission);
    }

    [Theory]
    [InlineData(NodeLifecycleState.Disabled)]
    [InlineData(NodeLifecycleState.PendingApproval)]
    [InlineData(NodeLifecycleState.PendingRegistration)]
    [InlineData(NodeLifecycleState.Recovery)]
    [InlineData(NodeLifecycleState.Rejected)]
    public void Evaluate_NonActiveNonDecommission_ReturnsBlockedByLifecycle(NodeLifecycleState state)
    {
        _sut.Evaluate(Node(state, false))
            .Should().Be(SyncEligibility.BlockedByLifecycle);
    }

    // ── EligibleExpression ────────────────────────────────────────────────

    [Fact]
    public void EligibleExpression_CompiledMatchesCanSynchronize()
    {
        var compiled = NodeSyncPolicy.EligibleExpression.Compile();
        var active   = Node(NodeLifecycleState.Active, false);
        var disabled = Node(NodeLifecycleState.Disabled, false);
        var maint    = Node(NodeLifecycleState.Active, true);

        compiled(active).Should().BeTrue();
        compiled(disabled).Should().BeFalse();
        compiled(maint).Should().BeFalse();
    }
}
