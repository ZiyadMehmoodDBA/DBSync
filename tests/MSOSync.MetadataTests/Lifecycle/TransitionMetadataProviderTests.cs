using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class TransitionMetadataProviderTests
{
    private readonly TransitionMetadataProvider _provider = new(new NodeLifecycleStateMachine());

    private static SyncNode Node(NodeLifecycleState s, bool maintenance = false) => new()
    {
        NodeId = "n1", GroupId = "g", SyncUrl = "http://x",
        LifecycleState = s, MaintenanceMode = maintenance,
    };

    [Fact]
    public void Active_NoMaintenance_OffersDisableStartMaintenanceDecommissionStartDrain()
        => _provider.GetTransitions(Node(NodeLifecycleState.Active)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["Disable", "StartMaintenance", "Decommission", "StartDrain"]);

    [Fact]
    public void Active_InMaintenance_OffersEndMaintenance_NotStart()
    {
        var actions = _provider.GetTransitions(Node(NodeLifecycleState.Active, maintenance: true))
            .AllowedTransitions.Select(t => t.Action).ToList();
        actions.Should().Contain("EndMaintenance");
        actions.Should().NotContain("StartMaintenance");
    }

    [Fact]
    public void Disabled_OffersEnableAndDecommission_Only()
        => _provider.GetTransitions(Node(NodeLifecycleState.Disabled)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["Enable", "Decommission"]);

    [Fact]
    public void Decommissioning_OffersForceCompleteOnly()
        => _provider.GetTransitions(Node(NodeLifecycleState.Decommissioning)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["ForceCompleteDecommission"]);

    [Theory]
    [InlineData(NodeLifecycleState.Decommissioned)]
    [InlineData(NodeLifecycleState.Rejected)]
    public void Terminal_OffersNothing(NodeLifecycleState s)
        => _provider.GetTransitions(Node(s)).AllowedTransitions.Should().BeEmpty();

    [Fact]
    public void Decommission_IsCritical_RequiresReasonAndConfirmation()
    {
        var t = _provider.GetTransitions(Node(NodeLifecycleState.Active))
            .AllowedTransitions.Single(x => x.Action == "Decommission");
        t.Should().Be(new TransitionActionDto("Decommission", true, true, "Critical"));
    }

    [Fact]
    public void StartMaintenance_RequiresReason_NoConfirmation_Normal()
    {
        var t = _provider.GetTransitions(Node(NodeLifecycleState.Active))
            .AllowedTransitions.Single(x => x.Action == "StartMaintenance");
        t.Should().Be(new TransitionActionDto("StartMaintenance", true, false, "Normal"));
    }

    [Fact]
    public void PendingRegistration_OffersDecommissionOnly()   // Activate is node-driven, never an operator action
        => _provider.GetTransitions(Node(NodeLifecycleState.PendingRegistration)).AllowedTransitions
            .Select(t => t.Action).Should().BeEquivalentTo(["Decommission"]);
}
