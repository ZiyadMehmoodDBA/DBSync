using FluentAssertions;
using FluentValidation;
using MSOSync.Api.Dtos.Export;
using MSOSync.Api.Validators;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class CreateExportJobRequestValidatorTests
{
    private readonly IValidator<CreateExportJobRequest> _validator = new CreateExportJobRequestValidator();

    [Theory]
    [InlineData("events")]
    [InlineData("incoming-batches")]
    [InlineData("audit")]
    public void Valid_ResourceType_Empty_Filters_Passes(string resourceType)
        => _validator.Validate(new CreateExportJobRequest(resourceType, "csv", "")).IsValid.Should().BeTrue();

    [Fact]
    public void Unknown_ResourceType_Fails()
    {
        var result = _validator.Validate(new CreateExportJobRequest("bogus", "csv", ""));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Unknown resourceType: bogus");
    }

    [Fact]
    public void Valid_FiltersJson_Passes()
        => _validator.Validate(new CreateExportJobRequest("events", "csv", "{}")).IsValid.Should().BeTrue();

    [Fact]
    public void Malformed_FiltersJson_Fails()
    {
        var result = _validator.Validate(new CreateExportJobRequest("events", "csv", "{not-json"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Invalid filtersJson for events");
    }

    [Fact]
    public void Null_Literal_FiltersJson_Fails()
    {
        var result = _validator.Validate(new CreateExportJobRequest("audit", "csv", "null"));
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Unknown_ResourceType_With_Filters_Reports_ResourceType_Error_Only()
    {
        var result = _validator.Validate(new CreateExportJobRequest("bogus", "csv", "{}"));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorMessage == "Unknown resourceType: bogus");
    }
}
