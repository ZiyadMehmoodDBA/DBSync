using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Metadata.Marketplace;
using MSOSync.Plugin.Marketplace;
using MSOSync.Plugin.Marketplace.Models;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace MSOSync.MetadataTests.Marketplace;

public sealed class MarketplaceServiceTests : IDisposable
{
    private readonly Mock<IMarketplaceCacheStore> _cacheStore;
    private readonly MemoryCache                  _memCache;
    private readonly MarketplaceOptions           _opts;
    private readonly MarketplaceService           _sut;

    // Backing handler for HttpClient injection
    private readonly FakeHttpMessageHandler _httpHandler;

    public MarketplaceServiceTests()
    {
        _cacheStore  = new Mock<IMarketplaceCacheStore>();
        _memCache    = new MemoryCache(new MemoryCacheOptions());
        _httpHandler = new FakeHttpMessageHandler();
        _opts        = new MarketplaceOptions
        {
            RegistryUrl        = "https://registry.example.com",
            CacheMinutes       = 60,
            MemoryCacheMinutes = 5,
        };

        var httpFactory = new Mock<IHttpClientFactory>();
        httpFactory
            .Setup(f => f.CreateClient("MarketplaceRegistry"))
            .Returns(new HttpClient(_httpHandler) { BaseAddress = new Uri(_opts.RegistryUrl + "/v1/") });

        _sut = new MarketplaceService(
            httpFactory.Object,
            _cacheStore.Object,
            _memCache,
            Options.Create(_opts),
            NullLogger<MarketplaceService>.Instance);
    }

    public void Dispose() => _memCache.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    private static RegistryPluginEntry MakeEntry(string id, string version = "1.0.0",
        IReadOnlyList<RegistryVersionEntry>? versions = null) =>
        new()
        {
            Id            = id,
            Name          = $"Plugin {id}",
            Author        = "Author",
            Description   = "Desc",
            Category      = "General",
            LatestVersion = version,
            MinHostVersion = "1.0.0",
            PublishedAt   = DateTime.UtcNow.AddDays(-10),
            UpdatedAt     = DateTime.UtcNow.AddDays(-1),
            Versions      = versions ?? [],
        };

    private static RegistryVersionEntry MakeVersion(string version) =>
        new()
        {
            Version        = version,
            MinHostVersion = "1.0.0",
            MaxHostVersion = "99.0.0",
            PublishedAt    = DateTime.UtcNow.AddDays(-5),
            DownloadUrl    = $"https://cdn.example.com/{version}.msopkg",
            Sha256         = "abc123",
        };

    private static RegistrySearchResult MakeSearchResult(params RegistryPluginEntry[] entries) =>
        new() { Data = entries, Total = entries.Length, Page = 1, PageSize = 20, TotalPages = 1 };

    // ── SearchAsync — HTTP call, cache behavior ───────────────────────────────

    [Fact]
    public async Task SearchAsync_DbCacheHit_DoesNotCallHttp()
    {
        var entries = new[] { MakeEntry("plugin-a") };
        _cacheStore
            .Setup(s => s.GetSearchCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RegistryPluginEntry>)entries);

        var result = await _sut.SearchAsync(null, null, 1, 20, default);

        result.Data.Should().HaveCount(1);
        _httpHandler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task SearchAsync_DbCacheMiss_CallsHttpAndCaches()
    {
        _cacheStore
            .Setup(s => s.GetSearchCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RegistryPluginEntry>?)null);

        var remote = MakeSearchResult(MakeEntry("plugin-b", "2.0.0"));
        _httpHandler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(remote, JsonOpts));

        var result = await _sut.SearchAsync(null, null, 1, 20, default);

        result.Data.Should().HaveCount(1);
        result.Data[0].Id.Should().Be("plugin-b");
        _httpHandler.CallCount.Should().Be(1);

        // Verify DB upsert was called
        _cacheStore.Verify(
            s => s.UpsertBulkAsync(
                It.IsAny<string>(),
                It.IsAny<IReadOnlyList<RegistryPluginEntry>>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_MemoryCacheHit_SecondCallSkipsDbAndHttp()
    {
        _cacheStore
            .Setup(s => s.GetSearchCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RegistryPluginEntry>?)null);

        var remote = MakeSearchResult(MakeEntry("plugin-c"));
        _httpHandler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(remote, JsonOpts));

        // First call: hits HTTP
        await _sut.SearchAsync(null, null, 1, 20, default);

        // Second call: should be served from memory cache
        await _sut.SearchAsync(null, null, 1, 20, default);

        _httpHandler.CallCount.Should().Be(1);
        _cacheStore.Verify(
            s => s.GetSearchCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task SearchAsync_HttpFailure_ReturnsEmptyResult()
    {
        _cacheStore
            .Setup(s => s.GetSearchCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<RegistryPluginEntry>?)null);

        _httpHandler.SetException(new HttpRequestException("Network error"));

        var result = await _sut.SearchAsync(null, null, 1, 20, default);

        result.Should().NotBeNull();
        result.Data.Should().BeEmpty();
    }

    // ── GetPluginAsync ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetPluginAsync_DbCacheHit_ReturnsEntry()
    {
        var entry = MakeEntry("cached-plugin");
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), "cached-plugin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var result = await _sut.GetPluginAsync("cached-plugin", default);

        result.Should().NotBeNull();
        result!.Id.Should().Be("cached-plugin");
        _httpHandler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task GetPluginAsync_DbCacheMiss_CallsHttpAndCaches()
    {
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryPluginEntry?)null);

        var entry = MakeEntry("remote-plugin", "3.0.0");
        _httpHandler.SetResponse(HttpStatusCode.OK, JsonSerializer.Serialize(entry, JsonOpts));

        var result = await _sut.GetPluginAsync("remote-plugin", default);

        result.Should().NotBeNull();
        result!.Id.Should().Be("remote-plugin");

        _cacheStore.Verify(
            s => s.UpsertAsync(
                It.IsAny<string>(),
                It.IsAny<RegistryPluginEntry>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetPluginAsync_HttpReturns404_ReturnsNull()
    {
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryPluginEntry?)null);

        _httpHandler.SetResponse(HttpStatusCode.NotFound, "Not Found");

        var result = await _sut.GetPluginAsync("missing", default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPluginAsync_HttpException_ReturnsNull()
    {
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryPluginEntry?)null);

        _httpHandler.SetException(new HttpRequestException("timeout"));

        var result = await _sut.GetPluginAsync("boom", default);

        result.Should().BeNull();
    }

    // ── GetVersionsAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetVersionsAsync_PluginWithVersions_ReturnsVersionList()
    {
        var versions = new[] { MakeVersion("1.0.0"), MakeVersion("2.0.0") };
        var entry    = MakeEntry("versioned-plugin", "2.0.0", versions);
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), "versioned-plugin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var result = await _sut.GetVersionsAsync("versioned-plugin", default);

        result.Should().HaveCount(2);
        result.Select(v => v.Version).Should().Contain(["1.0.0", "2.0.0"]);
    }

    [Fact]
    public async Task GetVersionsAsync_PluginNotFound_ReturnsEmpty()
    {
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryPluginEntry?)null);

        _httpHandler.SetResponse(HttpStatusCode.NotFound, "Not Found");

        var result = await _sut.GetVersionsAsync("ghost", default);

        result.Should().BeEmpty();
    }

    // ── GetLatestUpdateAsync ──────────────────────────────────────────────────

    [Fact]
    public async Task GetLatestUpdateAsync_NewerVersionAvailable_ReturnsEntry()
    {
        var versions = new[] { MakeVersion("1.0.0"), MakeVersion("2.0.0") };
        var entry    = MakeEntry("update-plugin", "2.0.0", versions);
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), "update-plugin", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var result = await _sut.GetLatestUpdateAsync("update-plugin", "1.0.0", default);

        result.Should().NotBeNull();
        result!.Version.Should().Be("2.0.0");
    }

    [Fact]
    public async Task GetLatestUpdateAsync_AlreadyLatest_ReturnsNull()
    {
        var versions = new[] { MakeVersion("2.0.0") };
        var entry    = MakeEntry("up-to-date", "2.0.0", versions);
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), "up-to-date", It.IsAny<CancellationToken>()))
            .ReturnsAsync(entry);

        var result = await _sut.GetLatestUpdateAsync("up-to-date", "2.0.0", default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetLatestUpdateAsync_PluginNotInRegistry_ReturnsNull()
    {
        _cacheStore
            .Setup(s => s.GetPluginCacheAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RegistryPluginEntry?)null);

        _httpHandler.SetResponse(HttpStatusCode.NotFound, "Not Found");

        var result = await _sut.GetLatestUpdateAsync("not-in-registry", "1.0.0", default);

        result.Should().BeNull();
    }
}

// ── FakeHttpMessageHandler ────────────────────────────────────────────────────

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private HttpStatusCode _statusCode = HttpStatusCode.OK;
    private string         _content    = "{}";
    private Exception?     _exception;

    public int CallCount { get; private set; }

    public void SetResponse(HttpStatusCode statusCode, string content)
    {
        _statusCode = statusCode;
        _content    = content;
        _exception  = null;
    }

    public void SetException(Exception ex)
    {
        _exception = ex;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        CallCount++;
        if (_exception is not null)
            throw _exception;

        var response = new HttpResponseMessage(_statusCode)
        {
            Content = new StringContent(_content, System.Text.Encoding.UTF8, "application/json"),
        };
        return Task.FromResult(response);
    }
}
