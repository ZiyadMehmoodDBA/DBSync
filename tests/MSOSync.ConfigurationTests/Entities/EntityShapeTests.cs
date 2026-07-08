using FluentAssertions;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ConfigurationTests.Entities;

public sealed class EntityShapeTests
{
    [Fact]
    public void ConfigurationState_HasExpectedValues()
    {
        var values = Enum.GetNames<ConfigurationState>();
        values.Should().Contain(["None", "Current", "UpdateAvailable", "Applying", "Drifted", "Failed", "Unknown"]);
        values.Should().HaveCount(7);
    }

    [Fact]
    public void ConfigurationApplyStatus_HasExpectedValues()
    {
        var values = Enum.GetNames<ConfigurationApplyStatus>();
        values.Should().Contain(["None", "Applying", "Applied", "Failed"]);
        values.Should().HaveCount(4);
    }

    [Fact]
    public void FeatureFlagCatalog_ContainsExpectedKeys()
    {
        FeatureFlagCatalog.IsSupportedKey("enableBulkApply").Should().BeTrue();
        FeatureFlagCatalog.IsSupportedKey("enableCompression").Should().BeTrue();
        FeatureFlagCatalog.IsSupportedKey("enableParallelSync").Should().BeTrue();
        FeatureFlagCatalog.IsSupportedKey("nonExistentFlag").Should().BeFalse();
    }

    [Fact]
    public void ConfigurationSettings_DefaultIsValid()
    {
        var s = new ConfigurationSettings
        {
            HeartbeatIntervalSeconds = 30,
            TransportMode = "Push",
            MaxRetryAttempts = 3,
            RetryBackoffSeconds = 60,
            BatchSizeLimit = 1000,
            FeatureFlags = new Dictionary<string, bool>(),
            ChannelIds = [],
            RouterIds = [],
            TriggerIds = [],
        };
        s.HeartbeatIntervalSeconds.Should().Be(30);
        s.MinimumAgentVersion.Should().BeNull();
    }

    [Fact]
    public void SyncNode_HasConfigurationColumns()
    {
        var node = new SyncNode
        {
            NodeId = "n1", GroupId = "g", SyncUrl = "http://x",
        };
        node.AssignedTemplateId.Should().BeNull();
        node.AssignedTemplateVersion.Should().BeNull();
        node.AppliedTemplateVersion.Should().BeNull();
        node.ExpectedEffectiveHash.Should().BeNull();
        node.AppliedEffectiveHash.Should().BeNull();
        node.ConfigurationState.Should().BeNull();
        node.ConfigurationStatusReportedAt.Should().BeNull();
        node.LastAppliedAt.Should().BeNull();
    }
}
