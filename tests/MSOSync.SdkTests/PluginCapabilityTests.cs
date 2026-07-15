using FluentAssertions;
using MSOSync.Sdk.Metadata;
using Xunit;

namespace MSOSync.SdkTests;

public sealed class PluginCapabilityTests
{
    [Fact]
    public void PluginCapability_None_IsZero()
    {
        ((int)PluginCapability.None).Should().Be(0);
    }

    [Fact]
    public void PluginCapability_BitwiseCombination_Works()
    {
        var combined = PluginCapability.Collector | PluginCapability.Transport;
        combined.HasFlag(PluginCapability.Collector).Should().BeTrue();
        combined.HasFlag(PluginCapability.Transport).Should().BeTrue();
        combined.HasFlag(PluginCapability.Operation).Should().BeFalse();
    }

    [Fact]
    public void PluginCapability_AllValuesDistinct_NoPowerOfTwoCollisions()
    {
        var values = Enum.GetValues<PluginCapability>()
            .Where(v => v != PluginCapability.None)
            .ToList();

        foreach (var v1 in values)
        foreach (var v2 in values)
        {
            if (v1 == v2) continue;
            (v1 & v2).Should().Be(PluginCapability.None,
                $"{v1} and {v2} should not share bits");
        }
    }
}
