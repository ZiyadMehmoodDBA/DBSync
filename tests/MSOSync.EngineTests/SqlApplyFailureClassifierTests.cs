// tests/MSOSync.EngineTests/SqlApplyFailureClassifierTests.cs
using FluentAssertions;
using MSOSync.Engine;
using Xunit;

namespace MSOSync.EngineTests;

public sealed class SqlApplyFailureClassifierTests
{
    private readonly SqlApplyFailureClassifier _classifier = new();

    [Theory]
    [InlineData(2627, ApplyFailureCategory.DuplicateKey)]
    [InlineData(2601, ApplyFailureCategory.DuplicateKey)]
    [InlineData(547,  ApplyFailureCategory.FKViolation)]
    [InlineData(1205, ApplyFailureCategory.Deadlock)]
    [InlineData(-2,   ApplyFailureCategory.Timeout)]
    [InlineData(102,  ApplyFailureCategory.SyntaxError)]
    [InlineData(208,  ApplyFailureCategory.SyntaxError)]
    [InlineData(207,  ApplyFailureCategory.SyntaxError)]
    [InlineData(4121, ApplyFailureCategory.SyntaxError)]
    [InlineData(99999, ApplyFailureCategory.Unknown)]
    [InlineData(0,    ApplyFailureCategory.Unknown)]
    public void Classify_ErrorNumber_ReturnsExpectedCategory(int errorNumber, ApplyFailureCategory expected)
    {
        _classifier.Classify(errorNumber).Should().Be(expected);
    }
}
