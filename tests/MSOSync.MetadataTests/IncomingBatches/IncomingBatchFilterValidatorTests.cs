using FluentAssertions;
using MSOSync.Metadata.IncomingBatches;
using MSOSync.Persistence;
using Xunit;

namespace MSOSync.MetadataTests.IncomingBatches;

public sealed class IncomingBatchFilterValidatorTests
{
    private static IncomingBatchFilterValidator Sut() => new();

    [Fact]
    public void Page_Zero_Fails()
    {
        Sut().Validate(new IncomingBatchFilter { Page = 0 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void PageSize_Over100_Fails()
    {
        Sut().Validate(new IncomingBatchFilter { PageSize = 101 }).IsValid.Should().BeFalse();
    }

    [Fact]
    public void From_After_To_Fails()
    {
        var now = DateTime.UtcNow;
        Sut().Validate(new IncomingBatchFilter { From = now.AddHours(1), To = now })
             .IsValid.Should().BeFalse();
    }

    [Fact]
    public void Valid_WithStatus_Passes()
    {
        Sut().Validate(new IncomingBatchFilter { Status = IncomingBatchStatus.Error })
             .IsValid.Should().BeTrue();
    }
}
