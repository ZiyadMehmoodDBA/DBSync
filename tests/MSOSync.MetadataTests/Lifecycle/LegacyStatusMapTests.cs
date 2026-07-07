using FluentAssertions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class LegacyStatusMapTests
{
    [Theory]
    [InlineData("PENDING",      NodeLifecycleState.PendingApproval)]
    [InlineData("APPROVED",     NodeLifecycleState.PendingRegistration)]
    [InlineData("PROVISIONED",  NodeLifecycleState.PendingRegistration)]
    [InlineData("REGISTERED",   NodeLifecycleState.Active)]
    [InlineData("OFFLINE",      NodeLifecycleState.Active)]
    [InlineData("DISABLED",     NodeLifecycleState.Disabled)]
    public void Map_KnownLegacyStatus_MapsToCorrectLifecycleState(string legacy, NodeLifecycleState expected)
    {
        LegacyStatusMap.Map.TryGetValue(legacy, out var result).Should().BeTrue();
        result.Should().Be(expected);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("Pending")]
    [InlineData("registered")]
    [InlineData("OFFLINE")]
    public void Map_IsCaseInsensitive(string input)
    {
        LegacyStatusMap.Map.ContainsKey(input).Should().BeTrue();
    }

    [Fact]
    public void Map_ContainsExactlySixEntries()
    {
        LegacyStatusMap.Map.Should().HaveCount(6);
    }

    [Theory]
    [InlineData("UNKNOWN")]
    [InlineData("ACTIVE")]
    [InlineData("")]
    [InlineData("DECOMMISSIONED")]
    public void Map_UnknownStatus_ReturnsFalse(string unknown)
    {
        LegacyStatusMap.Map.ContainsKey(unknown).Should().BeFalse();
    }
}
