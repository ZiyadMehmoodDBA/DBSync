using FluentAssertions;
using MSOSync.Security;
using Xunit;

namespace MSOSync.SecurityTests;

public sealed class PasswordPolicyTests
{
    private readonly PasswordPolicy _policy = new();

    [Fact]
    public void Validate_ValidPassword_ReturnsTrue()
    {
        var (valid, error) = _policy.Validate("Valid1!x");
        valid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void Validate_TooShort_ReturnsFalse()
    {
        var (valid, error) = _policy.Validate("Ab1!");
        valid.Should().BeFalse();
        error.Should().Contain("8 characters");
    }

    [Fact]
    public void Validate_NoUppercase_ReturnsFalse()
    {
        var (valid, error) = _policy.Validate("password1!");
        valid.Should().BeFalse();
        error.Should().Contain("uppercase");
    }

    [Fact]
    public void Validate_NoDigit_ReturnsFalse()
    {
        var (valid, error) = _policy.Validate("Password!");
        valid.Should().BeFalse();
        error.Should().Contain("digit");
    }

    [Fact]
    public void Validate_NoSpecial_ReturnsFalse()
    {
        var (valid, error) = _policy.Validate("Password1");
        valid.Should().BeFalse();
        error.Should().Contain("special");
    }
}
