using FluentAssertions;
using FluentValidation;
using MSOSync.Api.Validators;
using MSOSync.Metadata.Export;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class OutgoingBatchExportFilterValidatorTests
{
    private readonly IValidator<OutgoingBatchExportFilter> _validator = new OutgoingBatchExportFilterValidator();

    [Fact]
    public void Null_Status_Passes()
        => _validator.Validate(new OutgoingBatchExportFilter()).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("New")]
    [InlineData("Sending")]
    [InlineData("Acknowledged")]
    [InlineData("Error")]
    [InlineData("Retry")]
    public void Valid_Status_Passes(string status)
        => _validator.Validate(new OutgoingBatchExportFilter { Status = status }).IsValid.Should().BeTrue();

    [Fact]
    public void Invalid_Status_Fails()
    {
        var result = _validator.Validate(new OutgoingBatchExportFilter { Status = "Bogus" });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.ErrorMessage == "Unknown status 'Bogus'. Valid values: New, Sending, Acknowledged, Error, Retry.");
    }
}
