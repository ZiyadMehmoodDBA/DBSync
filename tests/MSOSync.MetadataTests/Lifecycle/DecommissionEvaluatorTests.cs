using FluentAssertions;
using MSOSync.Metadata.Lifecycle;
using Xunit;

namespace MSOSync.MetadataTests.Lifecycle;

public sealed class DecommissionEvaluatorTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 06, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoOpenBatches_Finalize_DrainCompleted()
        => DecommissionEvaluator.Decide(openBatches: 0, graceUntil: Now.AddMinutes(30), now: Now)
            .Should().Be(new DecommissionDecision(true, DecommissionDecisionReason.DrainCompleted));

    [Fact]
    public void OpenBatches_GraceExpired_Finalize_GraceExpired()
        => DecommissionEvaluator.Decide(openBatches: 5, graceUntil: Now.AddMinutes(-1), now: Now)
            .Should().Be(new DecommissionDecision(true, DecommissionDecisionReason.GraceExpired));

    [Fact]
    public void OpenBatches_WithinGrace_DoNotFinalize()
        => DecommissionEvaluator.Decide(openBatches: 5, graceUntil: Now.AddMinutes(30), now: Now)
            .Should().Be(new DecommissionDecision(false, DecommissionDecisionReason.OpenBatches));

    [Fact]
    public void NoGraceSet_TreatedAsExpired()   // defensive: Decommissioning row without grace finalizes
        => DecommissionEvaluator.Decide(openBatches: 5, graceUntil: null, now: Now)
            .Should().Be(new DecommissionDecision(true, DecommissionDecisionReason.GraceExpired));

    [Fact]
    public void NoOpenBatches_EvenIfGraceRemains_FinalizesImmediately()
        => DecommissionEvaluator.Decide(openBatches: 0, graceUntil: Now.AddHours(1), now: Now)
            .Finalize.Should().BeTrue();
}
