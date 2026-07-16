using FluentAssertions;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Models;
using Xunit;

namespace MSOSync.ConfigurationTests.Services;

public sealed class CanonicalJsonSerializerTests
{
    private static ConfigurationSettings Base() => new()
    {
        HeartbeatIntervalSeconds = 30,
        TransportMode            = "Push",
        MaxRetryAttempts         = 3,
        RetryBackoffSeconds      = 60,
        BatchSizeLimit           = 1000,
        FeatureFlags             = new() { ["enableBulkApply"] = true },
        ChannelIds               = [Guid.Parse("11111111-0000-0000-0000-000000000000")],
        RouterIds                = [Guid.Parse("22222222-0000-0000-0000-000000000000")],
        TriggerIds               = [Guid.Parse("33333333-0000-0000-0000-000000000000")],
    };

    [Fact]
    public void SameInput_ProducesSameHash()
    {
        var h1 = CanonicalJsonSerializer.ComputeHash(Base());
        var h2 = CanonicalJsonSerializer.ComputeHash(Base());
        h1.Should().Be(h2);
    }

    [Fact]
    public void ChannelIds_OrderDoesNotAffectHash()
    {
        var g1 = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000000");
        var g2 = Guid.Parse("bbbbbbbb-0000-0000-0000-000000000000");
        var s1 = Base() with { ChannelIds = [g1, g2] };
        var s2 = Base() with { ChannelIds = [g2, g1] };
        CanonicalJsonSerializer.ComputeHash(s1).Should().Be(CanonicalJsonSerializer.ComputeHash(s2));
    }

    [Fact]
    public void RouterIds_OrderDoesNotAffectHash()
    {
        var g1 = Guid.Parse("cccccccc-0000-0000-0000-000000000000");
        var g2 = Guid.Parse("dddddddd-0000-0000-0000-000000000000");
        var s1 = Base() with { RouterIds = [g1, g2] };
        var s2 = Base() with { RouterIds = [g2, g1] };
        CanonicalJsonSerializer.ComputeHash(s1).Should().Be(CanonicalJsonSerializer.ComputeHash(s2));
    }

    [Fact]
    public void FeatureFlags_KeyOrderDoesNotAffectHash()
    {
        var s1 = Base() with { FeatureFlags = new() { ["enableBulkApply"] = true, ["enableCompression"] = false } };
        var s2 = Base() with { FeatureFlags = new() { ["enableCompression"] = false, ["enableBulkApply"] = true } };
        CanonicalJsonSerializer.ComputeHash(s1).Should().Be(CanonicalJsonSerializer.ComputeHash(s2));
    }

    [Fact]
    public void DifferentSettings_ProduceDifferentHash()
    {
        var h1 = CanonicalJsonSerializer.ComputeHash(Base());
        var h2 = CanonicalJsonSerializer.ComputeHash(Base() with { HeartbeatIntervalSeconds = 60 });
        h1.Should().NotBe(h2);
    }

    [Fact]
    public void Hash_IsHex64Chars()
    {
        var hash = CanonicalJsonSerializer.ComputeHash(Base());
        hash.Should().HaveLength(64).And.MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void NullMinimumAgentVersion_SameAsExplicitNull()
    {
        var h1 = CanonicalJsonSerializer.ComputeHash(Base() with { MinimumAgentVersion = null });
        var h2 = CanonicalJsonSerializer.ComputeHash(Base());
        h1.Should().Be(h2);
    }
}
