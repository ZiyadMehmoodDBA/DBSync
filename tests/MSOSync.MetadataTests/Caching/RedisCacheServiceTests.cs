using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Common.Caching;
using StackExchange.Redis;
using Xunit;

namespace MSOSync.MetadataTests.Caching;

public sealed class RedisCacheServiceTests
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private static (RedisCacheService svc, Mock<IDatabase> dbMock, Mock<IConnectionMultiplexer> muxMock)
        MakeSvc(CacheOptions? opts = null)
    {
        var dbMock  = new Mock<IDatabase>();
        var muxMock = new Mock<IConnectionMultiplexer>();
        muxMock
            .Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
            .Returns(dbMock.Object);

        var options = Options.Create(opts ?? new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        var logger  = NullLogger<RedisCacheService>.Instance;
        var svc     = new RedisCacheService(muxMock.Object, options, logger);
        return (svc, dbMock, muxMock);
    }

    // ── GetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetAsync_HitWithJsonValue_DeserializesAndReturns()
    {
        var (svc, db, _) = MakeSvc();
        var expected = new TestDto("Bob", 25);
        var json     = JsonSerializer.Serialize(expected, Json);

        db.Setup(d => d.StringGetAsync("k1", It.IsAny<CommandFlags>()))
          .ReturnsAsync((RedisValue)json);

        var result = await svc.GetAsync<TestDto>("k1");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Bob");
        result.Age.Should().Be(25);
    }

    [Fact]
    public async Task GetAsync_Miss_ReturnsNull()
    {
        var (svc, db, _) = MakeSvc();
        db.Setup(d => d.StringGetAsync("missing", It.IsAny<CommandFlags>()))
          .ReturnsAsync(RedisValue.Null);

        var result = await svc.GetAsync<TestDto>("missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_RedisException_LogsWarningAndReturnsNull()
    {
        var (svc, db, _) = MakeSvc();
        db.Setup(d => d.StringGetAsync("k2", It.IsAny<CommandFlags>()))
          .ThrowsAsync(new RedisException("Connection failed"));

        // Should not throw; returns null (cache miss behavior)
        var result = await svc.GetAsync<TestDto>("k2");

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetAsync_RedisTimeoutException_LogsWarningAndReturnsNull()
    {
        var (svc, db, _) = MakeSvc();
        db.Setup(d => d.StringGetAsync("k3", It.IsAny<CommandFlags>()))
          .ThrowsAsync(new RedisTimeoutException("Timeout", CommandStatus.Unknown));

        var result = await svc.GetAsync<TestDto>("k3");

        result.Should().BeNull();
    }

    // ── SetAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SetAsync_CallsStringSetWithJsonAndTtl()
    {
        var (svc, db, _) = MakeSvc();
        var dto  = new TestDto("Carol", 40);
        var expiry = TimeSpan.FromSeconds(30);

        db.Setup(d => d.StringSetAsync(
              "k4", It.IsAny<RedisValue>(), expiry, false, When.Always, It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

        await svc.SetAsync("k4", dto, expiry);

        db.Verify(d => d.StringSetAsync(
            "k4",
            It.Is<RedisValue>(v => v.ToString().Contains("Carol")),
            expiry, false, When.Always, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task SetAsync_NullExpiry_UsesDefaultExpiry()
    {
        var opts = new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(7) };
        var (svc, db, _) = MakeSvc(opts);

        db.Setup(d => d.StringSetAsync(
              "k5", It.IsAny<RedisValue>(), TimeSpan.FromMinutes(7), false, When.Always, It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

        await svc.SetAsync("k5", "value", expiry: null);

        db.Verify(d => d.StringSetAsync(
            "k5", It.IsAny<RedisValue>(), TimeSpan.FromMinutes(7), false, When.Always, It.IsAny<CommandFlags>()),
            Times.Once);
    }

    [Fact]
    public async Task SetAsync_RedisTimeoutException_SwallowedAndLogsWarning()
    {
        var (svc, db, _) = MakeSvc();
        db.Setup(d => d.StringSetAsync(
              It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan?>(),
              false, When.Always, It.IsAny<CommandFlags>()))
          .ThrowsAsync(new RedisTimeoutException("Timeout", CommandStatus.Unknown));

        var act = async () => await svc.SetAsync("k6", "v");
        await act.Should().NotThrowAsync();
    }

    // ── RemoveAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveAsync_CallsKeyDelete()
    {
        var (svc, db, _) = MakeSvc();
        db.Setup(d => d.KeyDeleteAsync("k7", It.IsAny<CommandFlags>()))
          .ReturnsAsync(true);

        await svc.RemoveAsync("k7");

        db.Verify(d => d.KeyDeleteAsync("k7", It.IsAny<CommandFlags>()), Times.Once);
    }

    [Fact]
    public async Task RemoveAsync_RedisException_SwallowedAndLogsWarning()
    {
        var (svc, db, _) = MakeSvc();
        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey>(), It.IsAny<CommandFlags>()))
          .ThrowsAsync(new RedisException("Down"));

        var act = async () => await svc.RemoveAsync("k8");
        await act.Should().NotThrowAsync();
    }

    // ── RemoveByPrefixAsync ───────────────────────────────────────────────────

    [Fact]
    public async Task RemoveByPrefixAsync_ScansAndDeletesMatchingKeys()
    {
        var (svc, db, mux) = MakeSvc();

        var serverMock = new Mock<IServer>();
        // IAsyncEnumerable of keys matching prefix
        serverMock
            .Setup(s => s.KeysAsync(
                It.IsAny<int>(), It.Is<RedisValue>(v => v.ToString() == "metadata:node:*"),
                It.IsAny<int>(), It.IsAny<long>(), It.IsAny<int>(), It.IsAny<CommandFlags>()))
            .Returns(AsyncEnumerable(["metadata:node:n1", "metadata:node:n2"]));

        mux.Setup(m => m.GetServers()).Returns(new IServer[] { serverMock.Object });

        db.Setup(d => d.KeyDeleteAsync(It.IsAny<RedisKey[]>(), It.IsAny<CommandFlags>()))
          .ReturnsAsync(2L);

        await svc.RemoveByPrefixAsync("metadata:node:");

        db.Verify(d => d.KeyDeleteAsync(
            It.Is<RedisKey[]>(keys => keys.Length == 2),
            It.IsAny<CommandFlags>()), Times.Once);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async IAsyncEnumerable<RedisKey> AsyncEnumerable(IEnumerable<string> keys)
    {
        foreach (var k in keys)
            yield return (RedisKey)k;
        await Task.CompletedTask;
    }

    private sealed record TestDto(string Name, int Age);
}
