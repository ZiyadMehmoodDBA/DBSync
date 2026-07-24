using FluentAssertions;
using MSOSync.Plugin.Marketplace;
using Xunit;

namespace MSOSync.MetadataTests.Marketplace;

public sealed class MarketplaceOptionsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsConfigured_NullOrWhitespace_ReturnsFalse(string? url)
    {
        var opts = new MarketplaceOptions { RegistryUrl = url };
        opts.IsConfigured.Should().BeFalse();
    }

    [Fact]
    public void IsConfigured_WithUrl_ReturnsTrue()
    {
        var opts = new MarketplaceOptions { RegistryUrl = "https://marketplace.msosync.io/api/v1" };
        opts.IsConfigured.Should().BeTrue();
    }

    [Fact]
    public void Defaults_AreSet()
    {
        var opts = new MarketplaceOptions();
        opts.CacheMinutes.Should().Be(60);
        opts.MemoryCacheMinutes.Should().Be(5);
        opts.HttpTimeoutSeconds.Should().Be(30);
        opts.RetryCount.Should().Be(3);
    }

    [Fact]
    public void SectionName_IsMarketplace()
    {
        MarketplaceOptions.SectionName.Should().Be("Marketplace");
    }
}
