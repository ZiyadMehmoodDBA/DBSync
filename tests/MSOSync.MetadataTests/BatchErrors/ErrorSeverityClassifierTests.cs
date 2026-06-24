using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using Xunit;

namespace MSOSync.MetadataTests.BatchErrors;

public sealed class ErrorSeverityClassifierTests
{
    private static IErrorSeverityClassifier Sut() => new ErrorSeverityClassifier();

    [Fact]
    public void Classify_Null_ReturnsCritical()
    {
        Sut().Classify(null).Should().Be(ErrorSeverity.Critical);
    }

    [Fact]
    public void Classify_Unknown_ReturnsCritical()
    {
        Sut().Classify("SomeUnknownType").Should().Be(ErrorSeverity.Critical);
    }

    [Fact]
    public void Classify_DuplicateKey_ReturnsInfo()
    {
        Sut().Classify("DuplicateKey").Should().Be(ErrorSeverity.Info);
    }

    [Theory]
    [InlineData("Timeout")]
    [InlineData("Deadlock")]
    [InlineData("SequenceGap")]
    public void Classify_RetriableTypes_ReturnWarning(string conflictType)
    {
        Sut().Classify(conflictType).Should().Be(ErrorSeverity.Warning);
    }

    [Fact]
    public void Classify_MetadataMissing_ReturnsCritical()
    {
        Sut().Classify("MetadataMissing").Should().Be(ErrorSeverity.Critical);
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Critical)]
    public void GetConflictTypes_ReturnsNonEmpty(ErrorSeverity severity)
    {
        Sut().GetConflictTypes(severity).Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Critical)]
    public void GetConflictTypes_ReturnsNoNulls(ErrorSeverity severity)
    {
        Sut().GetConflictTypes(severity).Should().NotContainNulls();
    }

    [Theory]
    [InlineData(ErrorSeverity.Info)]
    [InlineData(ErrorSeverity.Warning)]
    [InlineData(ErrorSeverity.Critical)]
    public void RoundTrip_AllTypesClassifyBackCorrectly(ErrorSeverity severity)
    {
        var sut   = Sut();
        var types = sut.GetConflictTypes(severity);
        foreach (var t in types)
            sut.Classify(t).Should().Be(severity, because: $"'{t}' should classify as {severity}");
    }

    [Fact]
    public void GetConflictTypes_SetsAreDisjoint()
    {
        var sut  = Sut();
        var info = sut.GetConflictTypes(ErrorSeverity.Info).ToHashSet();
        var warn = sut.GetConflictTypes(ErrorSeverity.Warning).ToHashSet();
        var crit = sut.GetConflictTypes(ErrorSeverity.Critical).ToHashSet();

        info.Intersect(warn).Should().BeEmpty();
        info.Intersect(crit).Should().BeEmpty();
        warn.Intersect(crit).Should().BeEmpty();
    }
}
