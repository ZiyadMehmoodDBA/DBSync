using FluentAssertions;
using FluentValidation;
using MSOSync.Api.Validators;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class OperationsPageSizeValidatorTests
{
    private readonly IValidator<int> _validator = new OperationsPageSizeValidator();

    [Theory]
    [InlineData(1)]
    [InlineData(25)]
    [InlineData(100)]
    public void In_Range_Passes(int pageSize)
        => _validator.Validate(pageSize).IsValid.Should().BeTrue();

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void Out_Of_Range_Fails(int pageSize)
        => _validator.Validate(pageSize).IsValid.Should().BeFalse();
}
