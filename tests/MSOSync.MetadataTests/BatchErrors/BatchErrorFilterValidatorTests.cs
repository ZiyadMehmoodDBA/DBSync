using FluentAssertions;
using MSOSync.Metadata.BatchErrors;
using Xunit;

namespace MSOSync.MetadataTests.BatchErrors;

public sealed class BatchErrorFilterValidatorTests
{
    private static BatchErrorFilterValidator Sut() => new();

    [Fact]
    public void Page_Zero_Fails()
    {
        Sut().Validate(new BatchErrorFilter { Page = 0 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Over100_Fails()
    {
        Sut().Validate(new BatchErrorFilter { PageSize = 101 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void From_After_To_Fails()
    {
        var now = DateTime.UtcNow;
        Sut().Validate(new BatchErrorFilter { From = now.AddHours(1), To = now })
             .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_WithSeverity_Passes()
    {
        Sut().Validate(new BatchErrorFilter { Severity = ErrorSeverity.Warning })
             .IsValid.Should().BeTrue();
    }
}
