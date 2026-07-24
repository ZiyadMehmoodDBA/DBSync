using FluentAssertions;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Api.Validators;
using Xunit;

namespace MSOSync.MetadataTests.Marketplace;

public sealed class MarketplaceInstallRequestValidatorTests
{
    private readonly MarketplaceInstallRequestValidator _sut = new();

    [Theory]
    [InlineData("abc")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("v1.2.3")]
    public async Task NonSemverVersion_IsInvalid(string version)
    {
        var result = await _sut.ValidateAsync(new MarketplaceInstallRequest { Version = version });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task NullVersion_IsValid()
    {
        var result = await _sut.ValidateAsync(new MarketplaceInstallRequest { Version = null });
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.10.300")]
    [InlineData("0.0.1")]
    public async Task ValidSemver_IsValid(string version)
    {
        var result = await _sut.ValidateAsync(new MarketplaceInstallRequest { Version = version });
        result.IsValid.Should().BeTrue();
    }
}
