using FluentAssertions;
using MSOSync.Metadata.Configuration;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.ConfigurationTests.Services;

public sealed class DriftDetectorTests
{
    private static readonly DateTime Now = new(2026, 7, 8, 12, 0, 0, DateTimeKind.Utc);
    private readonly IDriftDetector _detector = new DriftDetector();

    private static SyncNode Node(
        Guid? assignedId = null, int? assignedVer = null, int? appliedVer = null,
        string? expectedHash = null, string? appliedHash = null,
        ConfigurationState? state = null, DateTime? reportedAt = null) => new()
    {
        NodeId                        = "n1", GroupId = "g", SyncUrl = "http://x",
        AssignedTemplateId            = assignedId,
        AssignedTemplateVersion       = assignedVer,
        AppliedTemplateVersion        = appliedVer,
        ExpectedEffectiveHash         = expectedHash,
        AppliedEffectiveHash          = appliedHash,
        ConfigurationState            = state,
        ConfigurationStatusReportedAt = reportedAt,
    };

    [Fact]
    public void NoTemplate_ReturnsNone()
    {
        var result = _detector.Compute(Node(), Now, 30, 3);
        result.Should().Be(ConfigurationState.None);
    }

    [Fact]
    public void StaleReport_ReturnsUnknown()
    {
        // stale = 30 * 3 * 2 = 180s ago
        var node = Node(Guid.NewGuid(), 1, 1, "abc", "abc",
            reportedAt: Now.AddSeconds(-181));
        var result = _detector.Compute(node, Now, 30, 3);
        result.Should().Be(ConfigurationState.Unknown);
    }

    [Fact]
    public void VersionMatch_HashMatch_ReturnsCurrent()
    {
        var node = Node(Guid.NewGuid(), 2, 2, "hash1", "hash1",
            reportedAt: Now.AddSeconds(-10));
        var result = _detector.Compute(node, Now, 30, 3);
        result.Should().Be(ConfigurationState.Current);
    }

    [Fact]
    public void VersionMismatch_ReturnsUpdateAvailable()
    {
        var node = Node(Guid.NewGuid(), 3, 2, "hash1", "old-hash",
            reportedAt: Now.AddSeconds(-10));
        var result = _detector.Compute(node, Now, 30, 3);
        result.Should().Be(ConfigurationState.UpdateAvailable);
    }

    [Fact]
    public void SameVersion_HashMismatch_ReturnsDrifted()
    {
        var node = Node(Guid.NewGuid(), 2, 2, "expected-hash", "applied-hash",
            reportedAt: Now.AddSeconds(-10));
        var result = _detector.Compute(node, Now, 30, 3);
        result.Should().Be(ConfigurationState.Drifted);
    }

    [Fact]
    public void ThresholdBoundary_ExactlyAtThreshold_ReturnsUnknown()
    {
        // threshold = 30 * 3 * 2 = 180s
        var node = Node(Guid.NewGuid(), 1, 1, "h", "h",
            reportedAt: Now.AddSeconds(-180));
        var result = _detector.Compute(node, Now, 30, 3);
        result.Should().Be(ConfigurationState.Unknown);
    }

    [Fact]
    public void ThresholdBoundary_OneSecondBelow_ReturnsCurrent()
    {
        var node = Node(Guid.NewGuid(), 1, 1, "h", "h",
            reportedAt: Now.AddSeconds(-179));
        var result = _detector.Compute(node, Now, 30, 3);
        result.Should().Be(ConfigurationState.Current);
    }
}
