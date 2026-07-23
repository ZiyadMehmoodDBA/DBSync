using FluentAssertions;
using MSOSync.Plugin.Packaging;
using Xunit;

namespace MSOSync.PluginTests.Packaging;

public sealed class SdkVersionConstraintParserTests
{
    [Fact]
    public void Satisfies_GreaterThanOrEqual_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0", new Version(1, 2, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_GreaterThanOrEqual_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies(">=2.0.0", new Version(1, 9, 9)).Should().BeFalse();

    [Fact]
    public void Satisfies_StrictLessThan_Satisfied()
        => SdkVersionConstraintParser.Satisfies("<2.0.0", new Version(1, 9, 9)).Should().BeTrue();

    [Fact]
    public void Satisfies_StrictLessThan_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies("<2.0.0", new Version(2, 0, 0)).Should().BeFalse();

    [Fact]
    public void Satisfies_Range_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0 <2.0.0", new Version(1, 5, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_Range_ExactLowerBound_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0 <2.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_Range_UpperBoundExclusive()
        => SdkVersionConstraintParser.Satisfies(">=1.0.0 <2.0.0", new Version(2, 0, 0)).Should().BeFalse();

    [Fact]
    public void Satisfies_ExactMatch_WithEquals_Satisfied()
        => SdkVersionConstraintParser.Satisfies("=1.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_ExactMatch_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies("=1.0.0", new Version(1, 0, 1)).Should().BeFalse();

    [Fact]
    public void Satisfies_BareVersion_ExactMatch_Satisfied()
        => SdkVersionConstraintParser.Satisfies("1.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_StrictGreaterThan_Satisfied()
        => SdkVersionConstraintParser.Satisfies(">1.0.0", new Version(1, 0, 1)).Should().BeTrue();

    [Fact]
    public void Satisfies_StrictGreaterThan_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies(">1.0.0", new Version(1, 0, 0)).Should().BeFalse();

    [Fact]
    public void Satisfies_LessThanOrEqual_Satisfied()
        => SdkVersionConstraintParser.Satisfies("<=1.0.0", new Version(1, 0, 0)).Should().BeTrue();

    [Fact]
    public void Satisfies_LessThanOrEqual_NotSatisfied()
        => SdkVersionConstraintParser.Satisfies("<=1.0.0", new Version(1, 0, 1)).Should().BeFalse();

    [Fact]
    public void Satisfies_InvalidConstraint_ReturnsFalse()
        => SdkVersionConstraintParser.Satisfies("banana", new Version(1, 0, 0)).Should().BeFalse();

    [Fact]
    public void Parse_InvalidConstraint_ReturnsNull()
        => SdkVersionConstraintParser.Parse("banana").Should().BeNull();
}
