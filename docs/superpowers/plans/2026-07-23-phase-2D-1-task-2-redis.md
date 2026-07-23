# Task 2: RedisCacheService + RedisCacheHealthCheck + Tests

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement `RedisCacheService` and `RedisCacheHealthCheck`, complete the `CachingExtensions.RegisterRedis` method, add `StackExchange.Redis` to `Directory.Packages.props`, write unit tests with a mocked `IConnectionMultiplexer`, and write integration tests with a real Redis via Testcontainers.

**Prerequisite:** Task 1 complete (ICacheService, CacheKeyHelper, CachingExtensions skeleton exist).

**Files:**
- Modify: `Directory.Packages.props` — add `StackExchange.Redis` 2.8.16 and `Testcontainers.Redis` 4.4.0
- Create: `src/MSOSync.Common/Caching/RedisCacheService.cs`
- Create: `src/MSOSync.Common/Caching/RedisCacheHealthCheck.cs`
- Modify: `src/MSOSync.Common/Caching/CachingExtensions.cs` — replace `RegisterRedis` body
- Create: `tests/MSOSync.MetadataTests/Caching/RedisCacheServiceTests.cs`
- Create: `tests/MSOSync.IntegrationTests/Caching/RedisCacheIntegrationTests.cs`
- Modify: `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj` — add `Testcontainers.Redis`

**Interfaces:**
- Consumes: `ICacheService`, `CacheOptions` from Task 1.
- Produces: `RedisCacheService` (singleton, `internal sealed`), `RedisCacheHealthCheck` (singleton, `internal sealed`). Consumed by `CachingExtensions.RegisterRedis`.

---

## Steps

- [ ] **Step 1: Add package versions to `Directory.Packages.props`**

Open `Directory.Packages.props`. Find the `<ItemGroup Label="Testing">` block. Add `Testcontainers.Redis` next to the existing `Testcontainers.MsSql`. Then add a new `<ItemGroup Label="Caching">` block after the `Extensions` group:

```xml
  <!-- Inside <ItemGroup Label="Testing"> — add alongside Testcontainers.MsSql -->
  <PackageVersion Include="Testcontainers.Redis" Version="4.4.0" />

  <!-- New group — add after the Extensions ItemGroup -->
  <ItemGroup Label="Caching">
    <PackageVersion Include="StackExchange.Redis" Version="2.8.16" />
  </ItemGroup>
```

After editing the file, verify it parses:

```bash
dotnet build src/MSOSync.Common/MSOSync.Common.csproj
```

Expected: Builds cleanly with no package version errors.

- [ ] **Step 2: Write the failing unit tests for `RedisCacheService`**

Create `tests/MSOSync.MetadataTests/Caching/RedisCacheServiceTests.cs`:

```csharp
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
            It.Is<RedisValue>(v => v.ToString().Contains("carol")),
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

        mux.Setup(m => m.GetServers()).Returns([serverMock.Object]);

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
```

- [ ] **Step 3: Run the tests to verify they fail (RedisCacheService not yet defined)**

```bash
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj --filter "FullyQualifiedName~MSOSync.MetadataTests.Caching.RedisCacheServiceTests" --no-build 2>&1 | head -20
```

Expected: Compilation errors — `RedisCacheService` not found.

- [ ] **Step 4: Create `RedisCacheService`**

Create `src/MSOSync.Common/Caching/RedisCacheService.cs`:

```csharp
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MSOSync.Common.Caching;

internal sealed class RedisCacheService(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
    private static readonly JsonSerializerOptions _json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly CacheOptions _opts = options.Value;
    private IDatabase Db => redis.GetDatabase();

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        try
        {
            var raw = await Db.StringGetAsync(key).ConfigureAwait(false);
            if (!raw.HasValue) return default;
            return JsonSerializer.Deserialize<T>(raw!, _json);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis GET failed for key {Key}; returning cache miss", key);
            return default;
        }
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var ttl = expiry ?? _opts.DefaultExpiry;
        try
        {
            var json = JsonSerializer.Serialize(value, _json);
            await Db.StringSetAsync(key, json, ttl).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis SET failed for key {Key}; value not cached", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken ct = default)
    {
        try
        {
            await Db.KeyDeleteAsync(key).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis DEL failed for key {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
    {
        try
        {
            var server = redis.GetServers().FirstOrDefault()
                ?? throw new InvalidOperationException("No Redis server endpoints available.");

            var pattern = $"{prefix}*";
            var keys = new List<RedisKey>();
            await foreach (var key in server.KeysAsync(pattern: pattern).WithCancellation(ct))
                keys.Add(key);

            if (keys.Count > 0)
                await Db.KeyDeleteAsync(keys.ToArray()).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or RedisTimeoutException)
        {
            logger.LogWarning(ex, "Redis prefix scan/DEL failed for prefix {Prefix}", prefix);
        }
    }
}
```

- [ ] **Step 5: Create `RedisCacheHealthCheck`**

Create `src/MSOSync.Common/Caching/RedisCacheHealthCheck.cs`:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using StackExchange.Redis;

namespace MSOSync.Common.Caching;

internal sealed class RedisCacheHealthCheck(IConnectionMultiplexer redis)
    : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken ct = default)
    {
        try
        {
            await redis.GetDatabase().PingAsync().ConfigureAwait(false);
            return HealthCheckResult.Healthy("Redis is reachable.");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("Redis ping failed.", ex);
        }
    }
}
```

- [ ] **Step 6: Replace the `RegisterRedis` body in `CachingExtensions`**

Open `src/MSOSync.Common/Caching/CachingExtensions.cs`. Replace the entire file content with:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace MSOSync.Common.Caching;

public static class CachingExtensions
{
    /// <summary>
    /// Registers ICacheService backed by either IMemoryCache or Redis,
    /// based on Cache:Provider in configuration ("Memory" or "Redis").
    /// </summary>
    public static IServiceCollection AddCacheService(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<CacheOptions>(configuration.GetSection(CacheOptions.Section));

        var provider = configuration
            .GetSection(CacheOptions.Section)
            .GetValue<string>("Provider") ?? "Memory";

        if (string.Equals(provider, "Redis", StringComparison.OrdinalIgnoreCase))
        {
            RegisterRedis(services);
        }
        else
        {
            // Memory provider — ensure IMemoryCache is registered (idempotent)
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, InMemoryCacheService>();
        }

        return services;
    }

    private static void RegisterRedis(IServiceCollection services)
    {
        services.AddSingleton<IConnectionMultiplexer>(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;

            if (string.IsNullOrWhiteSpace(opts.RedisConnectionString))
                throw new InvalidOperationException(
                    "Cache:RedisConnectionString must be set when Cache:Provider is \"Redis\".");

            return ConnectionMultiplexer.Connect(opts.RedisConnectionString);
        });

        services.AddSingleton<ICacheService, RedisCacheService>();

        services.AddHealthChecks()
            .AddCheck<RedisCacheHealthCheck>("redis-cache");
    }
}
```

- [ ] **Step 7: Run `RedisCacheService` unit tests**

```bash
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj --filter "FullyQualifiedName~MSOSync.MetadataTests.Caching.RedisCacheServiceTests" -v normal
```

Expected: All 9 tests pass.

Note on the `RemoveByPrefixAsync` test: if Moq has difficulty with `GetServers()` returning an array, adjust the mock setup to `mux.Setup(m => m.GetServers()).Returns(new IServer[] { serverMock.Object })`.

- [ ] **Step 8: Add `Testcontainers.Redis` to the integration test project**

Open `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj`. In the `<ItemGroup>` with other package references, add:

```xml
<PackageReference Include="Testcontainers.Redis" />
```

- [ ] **Step 9: Write the Redis integration tests**

Create `tests/MSOSync.IntegrationTests/Caching/RedisCacheIntegrationTests.cs`:

```csharp
using DotNet.Testcontainers.Builders;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Common.Caching;
using StackExchange.Redis;
using Testcontainers.Redis;
using Xunit;

namespace MSOSync.IntegrationTests.Caching;

[Trait("Category", "Integration")]
public sealed class RedisCacheIntegrationTests : IAsyncLifetime
{
    private RedisContainer?     _container;
    private RedisCacheService?  _svc;
    private IConnectionMultiplexer? _mux;

    public async Task InitializeAsync()
    {
        if (!DockerIsAvailable())
        {
            // Tests will be skipped in CheckIfAvailable()
            return;
        }

        _container = new RedisBuilder().Build();
        await _container.StartAsync();

        _mux = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var opts   = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        var logger = NullLogger<RedisCacheService>.Instance;
        _svc       = new RedisCacheService(_mux, opts, logger);
    }

    public async Task DisposeAsync()
    {
        _mux?.Dispose();
        if (_container is not null)
            await _container.DisposeAsync();
    }

    private void SkipIfUnavailable()
    {
        if (_svc is null)
            throw new SkipException("Docker unavailable — Redis integration tests skipped.");
    }

    // ── Round-trip ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RoundTrip_SetThenGet_ReturnsCorrectValue()
    {
        SkipIfUnavailable();
        var dto = new CacheTestDto("Integration", 99);

        await _svc!.SetAsync("it:round-trip", dto);
        var result = await _svc.GetAsync<CacheTestDto>("it:round-trip");

        result.Should().NotBeNull();
        result!.Name.Should().Be("Integration");
        result.Value.Should().Be(99);
    }

    // ── Expiry ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Expiry_ValueAbsentAfterTtl()
    {
        SkipIfUnavailable();
        await _svc!.SetAsync("it:expiry", "expires-soon", TimeSpan.FromMilliseconds(100));

        await Task.Delay(300);

        var result = await _svc.GetAsync<string>("it:expiry");
        result.Should().BeNull();
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_AfterSet_ReturnsNull()
    {
        SkipIfUnavailable();
        await _svc!.SetAsync("it:remove", "to-delete");
        await _svc.RemoveAsync("it:remove");

        var result = await _svc.GetAsync<string>("it:remove");
        result.Should().BeNull();
    }

    // ── RemoveByPrefix ────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveByPrefix_DeletesAllKeysWithPrefix()
    {
        SkipIfUnavailable();
        await _svc!.SetAsync("it:prefix:a", "1");
        await _svc.SetAsync("it:prefix:b", "2");
        await _svc.SetAsync("it:prefix:c", "3");
        await _svc.SetAsync("it:other:z", "keep-me");

        await _svc.RemoveByPrefixAsync("it:prefix:");

        (await _svc.GetAsync<string>("it:prefix:a")).Should().BeNull();
        (await _svc.GetAsync<string>("it:prefix:b")).Should().BeNull();
        (await _svc.GetAsync<string>("it:prefix:c")).Should().BeNull();
        (await _svc.GetAsync<string>("it:other:z")).Should().Be("keep-me");
    }

    // ── Large payload ─────────────────────────────────────────────────────────

    [Fact]
    public async Task LargePayload_10KbJson_RoundTripsCorrectly()
    {
        SkipIfUnavailable();
        var items = Enumerable.Range(0, 500)
            .Select(i => new CacheTestDto($"Item-{i}", i))
            .ToList();

        await _svc!.SetAsync("it:large", items);
        var result = await _svc.GetAsync<List<CacheTestDto>>("it:large");

        result.Should().HaveCount(500);
        result![0].Name.Should().Be("Item-0");
        result[499].Value.Should().Be(499);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool DockerIsAvailable()
    {
        try
        {
            using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName             = "docker",
                Arguments            = "info",
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute      = false
            });
            proc!.WaitForExit(3000);
            return proc.ExitCode == 0;
        }
        catch { return false; }
    }

    private sealed record CacheTestDto(string Name, int Value);
}

/// <summary>
/// xUnit v2 does not have built-in skip. This exception causes the test runner
/// to report the test as failed with a "skip" message. For full skip support,
/// use the xunit.skippable.fact package or filter on Trait("Category","Integration").
/// </summary>
internal sealed class SkipException(string reason) : Exception(reason);
```

- [ ] **Step 10: Run all unit tests in `MSOSync.MetadataTests`**

```bash
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj -v normal
```

Expected: All existing tests plus the two new Caching test classes pass. No regressions.

- [ ] **Step 11: Run integration tests (requires Docker)**

If Docker is available on the machine:

```bash
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "Category=Integration&FullyQualifiedName~Caching" -v normal
```

Expected: All 5 Redis integration tests pass.

If Docker is unavailable, the tests will throw `SkipException` and report as failures with a skip message — this is acceptable. In CI without Docker, filter them out:

```bash
dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "Category!=Integration"
```

- [ ] **Step 12: Commit**

```bash
git add Directory.Packages.props
git add src/MSOSync.Common/Caching/RedisCacheService.cs
git add src/MSOSync.Common/Caching/RedisCacheHealthCheck.cs
git add src/MSOSync.Common/Caching/CachingExtensions.cs
git add tests/MSOSync.MetadataTests/Caching/RedisCacheServiceTests.cs
git add tests/MSOSync.IntegrationTests/Caching/RedisCacheIntegrationTests.cs
git add tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj
git commit -m "feat(2D.1-T2): add RedisCacheService, RedisCacheHealthCheck, integration tests"
```
