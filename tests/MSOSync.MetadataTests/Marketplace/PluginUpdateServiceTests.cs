using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Metadata.Marketplace;
using MSOSync.Plugin.Abstractions;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using MSOSync.Plugin.Models;
using Xunit;

namespace MSOSync.MetadataTests.Marketplace;

public sealed class PluginUpdateServiceTests
{
    private readonly Mock<IMarketplaceService> _marketplaceService;
    private readonly Mock<IPluginStore>        _pluginStore;
    private readonly PluginUpdateService       _sut;

    public PluginUpdateServiceTests()
    {
        _marketplaceService = new Mock<IMarketplaceService>();
        _pluginStore        = new Mock<IPluginStore>();
        _sut = new PluginUpdateService(
            _marketplaceService.Object,
            _pluginStore.Object,
            NullLogger<PluginUpdateService>.Instance);
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private static PluginRecord MakeRecord(string id, string version) =>
        new() { PluginId = id, PluginName = id, PluginVersion = version, Status = "Active" };

    private static RegistryVersionEntry MakeVersion(string version) =>
        new()
        {
            Version        = version,
            MinHostVersion = "1.0.0",
            MaxHostVersion = "99.0.0",
            PublishedAt    = DateTime.UtcNow.AddDays(-3),
            DownloadUrl    = $"https://cdn.example.com/{version}.msopkg",
            Sha256         = "deadbeef",
            ReleaseNotes   = "Bug fixes",
        };

    // ── CheckAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAsync_NewerVersionAvailable_ReturnsManifest()
    {
        var latestVersion = MakeVersion("2.0.0");
        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync("plugin-a", "1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(latestVersion);

        var result = await _sut.CheckAsync("plugin-a", "1.0.0", default);

        result.Should().NotBeNull();
        result!.PluginId.Should().Be("plugin-a");
        result.InstalledVersion.Should().Be("1.0.0");
        result.AvailableVersion.Should().Be("2.0.0");
        result.DownloadUrl.Should().Be(latestVersion.DownloadUrl);
        result.Sha256.Should().Be("deadbeef");
        result.ReleaseNotes.Should().Be("Bug fixes");
    }

    [Fact]
    public async Task CheckAsync_AlreadyAtLatest_ReturnsNull()
    {
        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync("plugin-b", "3.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryVersionEntry?)null);

        var result = await _sut.CheckAsync("plugin-b", "3.0.0", default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_PluginNotInRegistry_ReturnsNull()
    {
        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryVersionEntry?)null);

        var result = await _sut.CheckAsync("unknown-plugin", "1.0.0", default);

        result.Should().BeNull();
    }

    // ── CheckAllAsync ─────────────────────────────────────────────────────────

    [Fact]
    public async Task CheckAllAsync_NoInstalledPlugins_ReturnsEmpty()
    {
        _pluginStore
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<PluginRecord>)[]);

        var result = await _sut.CheckAllAsync(default);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CheckAllAsync_SomeHaveUpdates_ReturnsOnlyUpdatable()
    {
        var installed = new[]
        {
            MakeRecord("plugin-a", "1.0.0"),  // has update
            MakeRecord("plugin-b", "2.0.0"),  // no update
            MakeRecord("plugin-c", "3.0.0"),  // not in registry
        };
        _pluginStore
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(installed);

        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync("plugin-a", "1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeVersion("2.0.0"));

        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync("plugin-b", "2.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryVersionEntry?)null);

        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync("plugin-c", "3.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryVersionEntry?)null);

        var result = await _sut.CheckAllAsync(default);

        result.Should().HaveCount(1);
        result[0].PluginId.Should().Be("plugin-a");
        result[0].AvailableVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task CheckAllAsync_AllHaveUpdates_ReturnsAllManifests()
    {
        var installed = new[]
        {
            MakeRecord("p1", "1.0.0"),
            MakeRecord("p2", "1.0.0"),
        };
        _pluginStore
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(installed);

        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync("p1", "1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeVersion("2.0.0"));

        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync("p2", "1.0.0", It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeVersion("3.0.0"));

        var result = await _sut.CheckAllAsync(default);

        result.Should().HaveCount(2);
        result.Select(m => m.PluginId).Should().Contain(["p1", "p2"]);
    }

    [Fact]
    public async Task CheckAllAsync_CallsMarketplaceSequentially()
    {
        // Verifies that each plugin is checked — the order of calls is sequential
        var callOrder = new List<string>();
        var installed = new[]
        {
            MakeRecord("first",  "1.0.0"),
            MakeRecord("second", "1.0.0"),
            MakeRecord("third",  "1.0.0"),
        };
        _pluginStore
            .Setup(s => s.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(installed);

        _marketplaceService
            .Setup(s => s.GetLatestUpdateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, string _, CancellationToken _) =>
            {
                callOrder.Add(id);
                return (RegistryVersionEntry?)null;
            });

        await _sut.CheckAllAsync(default);

        callOrder.Should().Equal("first", "second", "third");
    }
}
