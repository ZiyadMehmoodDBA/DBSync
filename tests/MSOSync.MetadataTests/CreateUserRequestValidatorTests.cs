using FluentAssertions;
using FluentValidation;
using MSOSync.Api.Validators;
using MSOSync.Metadata.Users;
using Xunit;

namespace MSOSync.MetadataTests;

public sealed class CreateUserRequestValidatorTests
{
    private readonly IValidator<CreateUserRequest> _validator = new CreateUserRequestValidator();

    [Fact]
    public void Valid_Request_Passes()
    {
        var result = _validator.Validate(new CreateUserRequest("alice", "P@ss1234!", true));
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Password_TooShort_Fails()
    {
        var result = _validator.Validate(new CreateUserRequest("alice", "short", true));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Password");
    }

    [Fact]
    public void Username_Empty_Fails()
    {
        var result = _validator.Validate(new CreateUserRequest("", "P@ss1234!", true));
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Username");
    }
}
