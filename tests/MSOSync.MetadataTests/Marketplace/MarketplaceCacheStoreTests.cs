using FluentAssertions;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Stores;
using MSOSync.Plugin.Marketplace.Models;
using System.Text.Json;
using Xunit;

namespace MSOSync.MetadataTests.Marketplace;

public sealed class MarketplaceCacheStoreTests : IDisposable
{
    private readonly AppDbContext          _db;
    private readonly MarketplaceCacheStore _sut;

    private const string Registry = "https://registry.example.com";

    public MarketplaceCacheStoreTests()
    {
        _db  = TestDbContext.Create();
        _sut = new MarketplaceCacheStore(_db);
    }

    public void Dispose() => _db.Dispose();

    // ── helpers ───────────────────────────────────────────────────────────────

    private static RegistryPluginEntry MakeEntry(string id, string version = "1.0.0") =>
        new()
        {
            Id            = id,
            Name          = $"Plugin {id}",
            Author        = "Test Author",
            Description   = "A test plugin.",
            Category      = "General",
            LatestVersion = version,
            MinHostVersion = "1.0.0",
            PublishedAt   = DateTime.UtcNow.AddDays(-10),
            UpdatedAt     = DateTime.UtcNow.AddDays(-1),
        };

    private static SyncMarketplaceCache MakeRow(
        string url, string id, string version,
        DateTime? expiresAt = null)
    {
        var entry = MakeEntry(id, version);
        var json  = JsonSerializer.Serialize(entry, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return new SyncMarketplaceCache
        {
            RegistryUrl   = url.TrimEnd('/'),
            PluginId      = id,
            LatestVersion = version,
            MetadataJson  = json,
            CachedAt      = DateTime.UtcNow.AddMinutes(-5),
            ExpiresAt     = expiresAt ?? DateTime.UtcNow.AddMinutes(55),
        };
    }

    // ── GetSearchCacheAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetSearchCacheAsync_EmptyDb_ReturnsNull()
    {
        var result = await _sut.GetSearchCacheAsync(Registry, "key", default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSearchCacheAsync_ValidRows_ReturnsDeserializedEntries()
    {
        _db.MarketplaceCache.AddRange(
            MakeRow(Registry, "plugin-a", "1.0.0"),
            MakeRow(Registry, "plugin-b", "2.0.0"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSearchCacheAsync(Registry, "any-key", default);

        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
        result!.Select(e => e.Id).Should().Contain(["plugin-a", "plugin-b"]);
    }

    [Fact]
    public async Task GetSearchCacheAsync_ExpiredRows_ReturnsNull()
    {
        _db.MarketplaceCache.Add(MakeRow(Registry, "plugin-a", "1.0.0",
            expiresAt: DateTime.UtcNow.AddMinutes(-1)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSearchCacheAsync(Registry, "key", default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSearchCacheAsync_DifferentRegistry_ReturnsNull()
    {
        _db.MarketplaceCache.Add(MakeRow("https://other.registry.com", "plugin-a", "1.0.0"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetSearchCacheAsync(Registry, "key", default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetSearchCacheAsync_TrailingSlashNormalized()
    {
        _db.MarketplaceCache.Add(MakeRow(Registry, "plugin-a", "1.0.0"));
        await _db.SaveChangesAsync();

        // Registry with trailing slash should still match
        var result = await _sut.GetSearchCacheAsync(Registry + "/", "key", default);
        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
    }

    // ── GetPluginCacheAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task GetPluginCacheAsync_NotFound_ReturnsNull()
    {
        var result = await _sut.GetPluginCacheAsync(Registry, "missing-plugin", default);
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetPluginCacheAsync_ValidRow_ReturnsEntry()
    {
        _db.MarketplaceCache.Add(MakeRow(Registry, "my-plugin", "3.1.0"));
        await _db.SaveChangesAsync();

        var result = await _sut.GetPluginCacheAsync(Registry, "my-plugin", default);

        result.Should().NotBeNull();
        result!.Id.Should().Be("my-plugin");
        result.LatestVersion.Should().Be("3.1.0");
    }

    [Fact]
    public async Task GetPluginCacheAsync_ExpiredRow_ReturnsNull()
    {
        _db.MarketplaceCache.Add(MakeRow(Registry, "my-plugin", "1.0.0",
            expiresAt: DateTime.UtcNow.AddMinutes(-1)));
        await _db.SaveChangesAsync();

        var result = await _sut.GetPluginCacheAsync(Registry, "my-plugin", default);
        result.Should().BeNull();
    }

    // ── UpsertAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertAsync_NewEntry_InsertsRow()
    {
        var entry = MakeEntry("new-plugin", "1.2.3");
        await _sut.UpsertAsync(Registry, entry, 60, default);

        var rows = _db.MarketplaceCache.ToList();
        rows.Should().HaveCount(1);
        rows[0].PluginId.Should().Be("new-plugin");
        rows[0].LatestVersion.Should().Be("1.2.3");
    }

    [Fact]
    public async Task UpsertAsync_ExistingEntry_UpdatesRow()
    {
        // Seed an existing row
        _db.MarketplaceCache.Add(MakeRow(Registry, "existing-plugin", "1.0.0"));
        await _db.SaveChangesAsync();

        var updatedEntry = MakeEntry("existing-plugin", "2.0.0");
        await _sut.UpsertAsync(Registry, updatedEntry, 60, default);

        var rows = _db.MarketplaceCache.ToList();
        rows.Should().HaveCount(1);
        rows[0].LatestVersion.Should().Be("2.0.0");
    }

    [Fact]
    public async Task UpsertAsync_SetsCorrectExpiry()
    {
        var before = DateTime.UtcNow;
        var entry  = MakeEntry("timed-plugin");
        await _sut.UpsertAsync(Registry, entry, 30, default);

        var row = _db.MarketplaceCache.First();
        row.ExpiresAt.Should().BeAfter(before.AddMinutes(29));
        row.ExpiresAt.Should().BeBefore(before.AddMinutes(31));
    }

    // ── UpsertBulkAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpsertBulkAsync_MultipleEntries_InsertsAll()
    {
        var entries = new[]
        {
            MakeEntry("plugin-1", "1.0.0"),
            MakeEntry("plugin-2", "2.0.0"),
            MakeEntry("plugin-3", "3.0.0"),
        };

        await _sut.UpsertBulkAsync(Registry, entries, 60, default);

        var rows = _db.MarketplaceCache.ToList();
        rows.Should().HaveCount(3);
    }

    [Fact]
    public async Task UpsertBulkAsync_EmptyList_DoesNothing()
    {
        await _sut.UpsertBulkAsync(Registry, [], 60, default);
        _db.MarketplaceCache.Should().BeEmpty();
    }

    // ── PurgeExpiredAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task PurgeExpiredAsync_NoExpiredRows_ReturnsZero()
    {
        _db.MarketplaceCache.Add(MakeRow(Registry, "plugin-a", "1.0.0"));
        await _db.SaveChangesAsync();

        var count = await _sut.PurgeExpiredAsync(default);
        count.Should().Be(0);
        _db.MarketplaceCache.Should().HaveCount(1);
    }

    [Fact]
    public async Task PurgeExpiredAsync_DeletesExpiredOnly()
    {
        _db.MarketplaceCache.AddRange(
            MakeRow(Registry, "fresh",   "1.0.0"),
            MakeRow(Registry, "expired", "1.0.0", expiresAt: DateTime.UtcNow.AddMinutes(-5)));
        await _db.SaveChangesAsync();

        var count = await _sut.PurgeExpiredAsync(default);

        count.Should().Be(1);
        _db.MarketplaceCache.Should().HaveCount(1);
        _db.MarketplaceCache.First().PluginId.Should().Be("fresh");
    }

    [Fact]
    public async Task PurgeExpiredAsync_EmptyDb_ReturnsZero()
    {
        var count = await _sut.PurgeExpiredAsync(default);
        count.Should().Be(0);
    }
}
