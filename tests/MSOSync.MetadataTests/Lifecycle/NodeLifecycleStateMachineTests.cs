using FluentAssertions;
using MSOSync.Common.Exceptions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class NodeLifecycleStateMachineTests
{
    private readonly NodeLifecycleStateMachine _sm = new();

    // Spec §2.2 — the exhaustive allowed set. Every pair not listed here is denied.
    public static readonly HashSet<(NodeLifecycleState, NodeLifecycleState)> Allowed =
    [
        (NodeLifecycleState.PendingApproval,     NodeLifecycleState.PendingRegistration),
        (NodeLifecycleState.PendingApproval,     NodeLifecycleState.Rejected),
        (NodeLifecycleState.PendingRegistration, NodeLifecycleState.Active),
        (NodeLifecycleState.Active,              NodeLifecycleState.Disabled),
        (NodeLifecycleState.Disabled,            NodeLifecycleState.Active),
        (NodeLifecycleState.Active,              NodeLifecycleState.Recovery),
        (NodeLifecycleState.Disabled,            NodeLifecycleState.Recovery),
        (NodeLifecycleState.Recovery,            NodeLifecycleState.Active),
        (NodeLifecycleState.Recovery,            NodeLifecycleState.Disabled),  // reject → PreviousLifecycleState
        (NodeLifecycleState.PendingApproval,     NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.PendingRegistration, NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Active,              NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Recovery,            NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Disabled,            NodeLifecycleState.Decommissioning),
        (NodeLifecycleState.Decommissioning,     NodeLifecycleState.Decommissioned),
    ];

    public static TheoryData<NodeLifecycleState, NodeLifecycleState> AllPairs()
    {
        var data = new TheoryData<NodeLifecycleState, NodeLifecycleState>();
        foreach (var from in Enum.GetValues<NodeLifecycleState>())
            foreach (var to in Enum.GetValues<NodeLifecycleState>())
                if (from != to) data.Add(from, to);
        return data;
    }

    [Theory]
    [MemberData(nameof(AllPairs))]
    public void CanTransition_MatchesCanonicalTable(NodeLifecycleState from, NodeLifecycleState to)
        => _sm.CanTransition(from, to).Should().Be(Allowed.Contains((from, to)));

    [Theory]
    [InlineData(NodeLifecycleState.Rejected)]
    [InlineData(NodeLifecycleState.Decommissioned)]
    public void Invariant1_TerminalStates_HaveNoExits(NodeLifecycleState terminal)
        => _sm.AllowedTargets(terminal).Should().BeEmpty();

    [Fact]
    public void SelfTransition_IsDenied()
    {
        foreach (var s in Enum.GetValues<NodeLifecycleState>())
            _sm.CanTransition(s, s).Should().BeFalse();
    }

    [Fact]
    public void Validate_InvalidTransition_ThrowsWithAllowedTargets()
    {
        // Exception carries strings: MSOSync.Common must not know the Persistence enum.
        var act = () => _sm.Validate(NodeLifecycleState.Disabled, NodeLifecycleState.Rejected);
        act.Should().Throw<InvalidLifecycleTransitionException>()
            .Which.AllowedTargets.Should().BeEquivalentTo(["Active", "Recovery", "Decommissioning"]);
    }

    [Fact]
    public void Invariant5_OnboardingIntoActive_OnlyFromPendingRegistrationOrRecoveryOrDisabled()
    {
        // Only three sources may enter Active: activation (PendingRegistration, Recovery)
        // and administrative Enable (Disabled).
        var sources = Enum.GetValues<NodeLifecycleState>()
            .Where(s => _sm.CanTransition(s, NodeLifecycleState.Active));
        sources.Should().BeEquivalentTo(
        [
            NodeLifecycleState.PendingRegistration,
            NodeLifecycleState.Recovery,
            NodeLifecycleState.Disabled,
        ]);
    }
}
