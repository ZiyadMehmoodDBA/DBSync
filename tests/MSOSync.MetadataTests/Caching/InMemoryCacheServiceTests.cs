using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
using Xunit;

namespace MSOSync.MetadataTests.Caching;

public sealed class InMemoryCacheServiceTests : IDisposable
{
    private readonly MemoryCache         _memCache;
    private readonly InMemoryCacheService _sut;

    public InMemoryCacheServiceTests()
    {
        _memCache = new MemoryCache(new MemoryCacheOptions());
        var opts  = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        _sut      = new InMemoryCacheService(_memCache, opts);
    }

    public void Dispose() => _memCache.Dispose();

    [Fact]
    public async Task GetAsync_Miss_ReturnsNull()
    {
        var result = await _sut.GetAsync<string>("missing");
        result.Should().BeNull();
    }

    [Fact]
    public async Task SetAsync_ThenGetAsync_ReturnsValue()
    {
        await _sut.SetAsync("k1", "hello");
        var result = await _sut.GetAsync<string>("k1");
        result.Should().Be("hello");
    }

    [Fact]
    public async Task SetAsync_WithExplicitExpiry_ValuePresentBeforeExpiry()
    {
        await _sut.SetAsync("k2", 42, TimeSpan.FromSeconds(10));
        var result = await _sut.GetAsync<int?>("k2");
        result.Should().Be(42);
    }

    [Fact]
    public async Task RemoveAsync_AfterSet_ReturnsNull()
    {
        await _sut.SetAsync("k3", "remove-me");
        await _sut.RemoveAsync("k3");
        var result = await _sut.GetAsync<string>("k3");
        result.Should().BeNull();
    }

    [Fact]
    public async Task RemoveAsync_OnMissingKey_DoesNotThrow()
    {
        var act = async () => await _sut.RemoveAsync("nonexistent");
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task GetAsync_ComplexType_RoundTrips()
    {
        var dto = new TestDto("Alice", 30);
        await _sut.SetAsync("dto", dto);
        var result = await _sut.GetAsync<TestDto>("dto");
        result.Should().NotBeNull();
        result!.Name.Should().Be("Alice");
        result.Age.Should().Be(30);
    }

    [Fact]
    public async Task SetAsync_WithNullExpiry_UsesDefaultExpiry()
    {
        // Default is 5 minutes — just verify no exception and value is set
        await _sut.SetAsync("k4", "default-ttl", expiry: null);
        var result = await _sut.GetAsync<string>("k4");
        result.Should().Be("default-ttl");
    }

    [Fact]
    public async Task RemoveByPrefixAsync_ThrowsNotSupportedException()
    {
        var act = async () => await _sut.RemoveByPrefixAsync("metadata:node:");
        await act.Should().ThrowAsync<NotSupportedException>()
            .WithMessage("*InMemory*");
    }

    private sealed record TestDto(string Name, int Age);
}
