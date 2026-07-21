using FluentAssertions;
using FluentValidation;
using MSOSync.Api.Validators;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class UpsertPreferenceRequestValidatorTests
{
    private readonly IValidator<string> _validator = new UpsertPreferenceRequestValidator();

    [Fact]
    public void Valid_Key_Passes()
    {
        var result = _validator.Validate("theme");
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Empty_Key_Fails()
    {
        var result = _validator.Validate("");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Whitespace_Key_Fails()
    {
        var result = _validator.Validate("   ");
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public void Key_At_100_Chars_Passes()
    {
        var result = _validator.Validate(new string('a', 100));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Key_At_101_Chars_Fails()
    {
        var result = _validator.Validate(new string('a', 101));
        result.IsValid.Should().BeFalse();
    }
}
