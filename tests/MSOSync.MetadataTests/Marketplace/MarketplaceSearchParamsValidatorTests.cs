using FluentAssertions;
using MSOSync.Api.Dtos.Marketplace;
using MSOSync.Api.Validators;
using Xunit;

namespace MSOSync.MetadataTests.Marketplace;

public sealed class MarketplaceSearchParamsValidatorTests
{
    private readonly MarketplaceSearchParamsValidator _sut = new();

    [Fact]
    public async Task Page_LessThan1_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 0, PageSize = 20 });
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.PropertyName == "Page");
    }

    [Fact]
    public async Task PageSize_Zero_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 0 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task PageSize_101_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 101 });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Query_ExceededMaxLength_IsInvalid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 20, Query = new string('x', 201) });
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidParams_NoQueryNoCategory_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 1, PageSize = 20 });
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidParams_WithQueryAndCategory_IsValid()
    {
        var result = await _sut.ValidateAsync(
            new MarketplaceSearchParams { Page = 2, PageSize = 50, Query = "sql", Category = "connector" });
        result.IsValid.Should().BeTrue();
    }
}
