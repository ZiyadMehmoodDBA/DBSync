using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class ConnectivityPolicyTests
{
    private readonly ConnectivityPolicy _sut = new();

    private static readonly TimeSpan HbInterval    = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromMinutes(2);
    private static readonly DateTime Now           = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    private static ConnectivityTelemetry Snapshot(
        NodeLifecycleState lifecycle        = NodeLifecycleState.Active,
        DateTime?          lastHeartbeat    = null,
        DateTime?          lastProbe        = null,
        bool               lastProbeFailed  = false,
        int                consecutiveFails = 0) =>
        new(lifecycle, lastHeartbeat, lastProbe, lastProbeFailed, consecutiveFails,
            Now, HbInterval, ProbeInterval);

    // ── Rule 1: excluded lifecycles ────────────────────────────────────────

    [Theory]
    [InlineData(NodeLifecycleState.PendingApproval)]
    [InlineData(NodeLifecycleState.PendingRegistration)]
    [InlineData(NodeLifecycleState.Rejected)]
    [InlineData(NodeLifecycleState.Decommissioned)]
    public void Rule1_ExcludedLifecycle_ReturnsUnknownNotEvaluated(NodeLifecycleState state)
    {
        var result = _sut.Evaluate(Snapshot(lifecycle: state, lastHeartbeat: Now));

        result.Status.Should().Be(ConnectivityStatus.Unknown);
        result.Reason.Should().Be(ConnectivityReason.NotEvaluated);
    }

    // ── Rule 2: no heartbeat ever ─────────────────────────────────────────

    [Fact]
    public void Rule2_NoHeartbeat_ReturnsUnknownNoHeartbeat()
    {
        var result = _sut.Evaluate(Snapshot(lastHeartbeat: null));

        result.Status.Should().Be(ConnectivityStatus.Unknown);
        result.Reason.Should().Be(ConnectivityReason.NoHeartbeat);
    }

    // ── Rule 3: heartbeat expired (>3x interval) ──────────────────────────

    [Fact]
    public void Rule3_HeartbeatExpired_ReturnsUnreachableExpired()
    {
        var stale = Now - 4 * HbInterval;
        var result = _sut.Evaluate(Snapshot(lastHeartbeat: stale));

        result.Status.Should().Be(ConnectivityStatus.Unreachable);
        result.Reason.Should().Be(ConnectivityReason.HeartbeatExpired);
    }

    // ── Rule 4: heartbeat stale (>1x interval) ────────────────────────────

    [Fact]
    public void Rule4_HeartbeatStale_ReturnsDegradedStale()
    {
        var stale = Now - 2 * HbInterval;
        var result = _sut.Evaluate(Snapshot(lastHeartbeat: stale));

        result.Status.Should().Be(ConnectivityStatus.Degraded);
        result.Reason.Should().Be(ConnectivityReason.HeartbeatStale);
    }

    // ── Rule 6: 3+ consecutive fresh probe failures ───────────────────────

    [Fact]
    public void Rule6_ThreeConsecutiveFreshProbeFails_ReturnsUnreachableProbeFailures()
    {
        var freshProbe = Now - ProbeInterval; // within 2x interval
        var result = _sut.Evaluate(Snapshot(
            lastHeartbeat: Now - TimeSpan.FromSeconds(10),
            lastProbe: freshProbe,
            lastProbeFailed: true,
            consecutiveFails: 3));

        result.Status.Should().Be(ConnectivityStatus.Unreachable);
        result.Reason.Should().Be(ConnectivityReason.ProbeFailures);
    }

    // ── Rule 5: single fresh probe failure ────────────────────────────────

    [Fact]
    public void Rule5_SingleFreshProbeFail_ReturnsDegradedProbeFailed()
    {
        var freshProbe = Now - ProbeInterval;
        var result = _sut.Evaluate(Snapshot(
            lastHeartbeat: Now - TimeSpan.FromSeconds(10),
            lastProbe: freshProbe,
            lastProbeFailed: true,
            consecutiveFails: 1));

        result.Status.Should().Be(ConnectivityStatus.Degraded);
        result.Reason.Should().Be(ConnectivityReason.ProbeFailed);
    }

    // ── Rule 7: healthy ────────────────────────────────────────────────────

    [Fact]
    public void Rule7_FreshHeartbeatNoProbeFailure_ReturnsReachableHealthy()
    {
        var freshProbe = Now - ProbeInterval;
        var result = _sut.Evaluate(Snapshot(
            lastHeartbeat: Now - TimeSpan.FromSeconds(10),
            lastProbe: freshProbe,
            lastProbeFailed: false));

        result.Status.Should().Be(ConnectivityStatus.Reachable);
        result.Reason.Should().Be(ConnectivityReason.Healthy);
    }

    [Fact]
    public void Rule7_FreshHeartbeatNoProbe_ReturnsReachableHealthy()
    {
        var result = _sut.Evaluate(Snapshot(
            lastHeartbeat: Now - TimeSpan.FromSeconds(10),
            lastProbe: null));

        result.Status.Should().Be(ConnectivityStatus.Reachable);
        result.Reason.Should().Be(ConnectivityReason.Healthy);
    }

    // ── Stale probe ignored ────────────────────────────────────────────────

    [Fact]
    public void StaleProbeIgnored_FreshHeartbeatStaleProbeFail_ReturnsHealthy()
    {
        // Probe failure is older than 2x probe interval — stale, ignored
        var staleProbe = Now - 3 * ProbeInterval;
        var result = _sut.Evaluate(Snapshot(
            lastHeartbeat: Now - TimeSpan.FromSeconds(10),
            lastProbe: staleProbe,
            lastProbeFailed: true,
            consecutiveFails: 5));

        result.Status.Should().Be(ConnectivityStatus.Reachable);
        result.Reason.Should().Be(ConnectivityReason.Healthy);
    }

    // ── Decommissioning is NOT excluded (lifecycle check) ─────────────────

    [Fact]
    public void DecommissioningLifecycle_IsNotExcludedByRule1_EvaluatesNormally()
    {
        // Decommissioning is NOT in excluded list; it evaluates based on heartbeat
        var result = _sut.Evaluate(Snapshot(
            lifecycle: NodeLifecycleState.Decommissioning,
            lastHeartbeat: Now - TimeSpan.FromSeconds(5)));

        // Fresh heartbeat, no probe failures → Reachable
        result.Status.Should().Be(ConnectivityStatus.Reachable);
    }
}
