using FluentAssertions;
using Moq;
using MSOSync.Persistence.Lock;
using StackExchange.Redis;
using Xunit;

namespace MSOSync.Tests.Lock;

public sealed class RedisDistributedLockServiceTests
{
    private readonly Mock<IConnectionMultiplexer> _multiplexer = new();
    private readonly Mock<IDatabase>              _db          = new();

    public RedisDistributedLockServiceTests()
    {
        _multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>()))
                    .Returns(_db.Object);
    }

    private RedisDistributedLockService Svc() => new(_multiplexer.Object);

    // ── TryAcquireAsync ───────────────────────────────────────────────────────

    [Fact]
    public async Task TryAcquireAsync_ReturnsHandle_WhenSetNxSucceeds()
    {
        _db.Setup(d => d.StringSetAsync(
                "LOCK:RES", "OWNER1",
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                When.NotExists,
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var handle = await Svc().TryAcquireAsync("LOCK:RES", "OWNER1", TimeSpan.FromSeconds(30));

        handle.Should().NotBeNull();
        handle!.Resource.Should().Be("LOCK:RES");
        handle.Owner.Should().Be("OWNER1");
        handle.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(30), TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryAcquireAsync_ReturnsNull_WhenSetNxFails()
    {
        _db.Setup(d => d.StringSetAsync(
                "LOCK:RES", "OWNER1",
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                When.NotExists,
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var handle = await Svc().TryAcquireAsync("LOCK:RES", "OWNER1", TimeSpan.FromSeconds(30));

        handle.Should().BeNull();
    }

    // ── RenewAsync ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RenewAsync_ReturnsTrue_WhenLuaReturns1()
    {
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k[0] == "LOCK:RES"),
                It.Is<RedisValue[]>(v => v[0] == "OWNER1"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));

        var result = await Svc().RenewAsync("LOCK:RES", "OWNER1", TimeSpan.FromSeconds(30));

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RenewAsync_ReturnsFalse_WhenLuaReturns0()
    {
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.Is<RedisKey[]>(k => k[0] == "LOCK:RES"),
                It.Is<RedisValue[]>(v => v[0] == "OWNER1"),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)0L));

        var result = await Svc().RenewAsync("LOCK:RES", "OWNER1", TimeSpan.FromSeconds(30));

        result.Should().BeFalse();
    }

    // ── ReleaseAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ReleaseAsync_InvokesLuaScript_WithCorrectKeysAndArgs()
    {
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]?>(),
                It.IsAny<RedisValue[]?>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));

        await Svc().ReleaseAsync("LOCK:RES", "OWNER1");

        _db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.Is<RedisKey[]>(k => k.Length == 1 && k[0] == "LOCK:RES"),
            It.Is<RedisValue[]>(v => v.Length == 1 && v[0] == "OWNER1"),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }

    // ── IsHeldAsync ───────────────────────────────────────────────────────────

    [Fact]
    public async Task IsHeldAsync_ReturnsTrue_WhenKeyExists()
    {
        _db.Setup(d => d.KeyExistsAsync("LOCK:RES", It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);

        var held = await Svc().IsHeldAsync("LOCK:RES");

        held.Should().BeTrue();
    }

    [Fact]
    public async Task IsHeldAsync_ReturnsFalse_WhenKeyAbsent()
    {
        _db.Setup(d => d.KeyExistsAsync("LOCK:RES", It.IsAny<CommandFlags>()))
            .ReturnsAsync(false);

        var held = await Svc().IsHeldAsync("LOCK:RES");

        held.Should().BeFalse();
    }

    // ── DisposeAsync ──────────────────────────────────────────────────────────

    [Fact]
    public async Task DisposeAsync_CallsReleaseScript()
    {
        // Arrange: acquire first
        _db.Setup(d => d.StringSetAsync(
                "LOCK:RES", "OWNER1",
                It.IsAny<TimeSpan?>(),
                It.IsAny<bool>(),
                When.NotExists,
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(true);
        _db.Setup(d => d.ScriptEvaluateAsync(
                It.IsAny<string>(),
                It.IsAny<RedisKey[]?>(),
                It.IsAny<RedisValue[]?>(),
                It.IsAny<CommandFlags>()))
            .ReturnsAsync(RedisResult.Create((RedisValue)1L));

        var handle = await Svc().TryAcquireAsync("LOCK:RES", "OWNER1", TimeSpan.FromSeconds(30));
        handle.Should().NotBeNull();

        // Act: dispose
        await handle!.DisposeAsync();

        // Assert: release Lua script called
        _db.Verify(d => d.ScriptEvaluateAsync(
            It.IsAny<string>(),
            It.Is<RedisKey[]>(k => k[0] == "LOCK:RES"),
            It.Is<RedisValue[]>(v => v[0] == "OWNER1"),
            It.IsAny<CommandFlags>()),
            Times.Once);
    }
}
