using FluentAssertions;
using MSOSync.Security;
using Xunit;

namespace MSOSync.SecurityTests;

public sealed class BCryptPasswordHasherTests
{
    private readonly BCryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_ReturnsNonEmptyString()
    {
        var hash = _hasher.Hash("Password1!");
        hash.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void Hash_TwoCallsSameInput_ReturnDifferentHashes()
    {
        var hash1 = _hasher.Hash("Password1!");
        var hash2 = _hasher.Hash("Password1!");
        hash1.Should().NotBe(hash2);
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("Password1!");
        _hasher.Verify("Password1!", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("Password1!");
        _hasher.Verify("WrongPassword!", hash).Should().BeFalse();
    }
}
