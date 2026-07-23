# Task 1: ICacheService + InMemoryCacheService + CacheKeyHelper + DI

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Define the `ICacheService` contract, `CacheOptions`, `CacheKeyHelper`, `InMemoryCacheService`, and the `AddCacheService` DI extension (Memory branch only). Write unit tests for `InMemoryCacheService`.

**Files:**
- Create: `src/MSOSync.Common/Caching/ICacheService.cs`
- Create: `src/MSOSync.Common/Caching/CacheOptions.cs`
- Create: `src/MSOSync.Common/Caching/CacheKeyHelper.cs`
- Create: `src/MSOSync.Common/Caching/InMemoryCacheService.cs`
- Create: `src/MSOSync.Common/Caching/CachingExtensions.cs`
- Modify: `src/MSOSync.Common/MSOSync.Common.csproj`
- Create: `tests/MSOSync.MetadataTests/Caching/InMemoryCacheServiceTests.cs`

**Interfaces:**
- Produces: `ICacheService` (namespace `MSOSync.Common.Caching`) with `GetAsync<T>`, `SetAsync<T>`, `RemoveAsync`, `RemoveByPrefixAsync`. Used by Tasks 2, 3.
- Produces: `CacheKeyHelper` (static) with `Node`, `Channel`, `Trigger`, `Router`, `Parameter`, `TopologyGraph`, `TopologyGroups`, `MetricsSummary`, `MetricsNodes`, `MetricsChannels`, `RoutingTrigger`, `Permissions`, `OverviewSnapshot`, `NodePrefix`, `ChannelPrefix`, `TriggerPrefix`, `RoutingPrefix`, `MetricsPrefix`, `TopologyPrefix`. Used by Tasks 2, 3.
- Produces: `AddCacheService(IServiceCollection, IConfiguration)` extension on `IServiceCollection`. Called by Task 3 in `Program.cs`.

---

## Steps

- [ ] **Step 1: Write the failing unit tests for `InMemoryCacheService`**

Create `tests/MSOSync.MetadataTests/Caching/InMemoryCacheServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run the tests to verify they fail (types not yet defined)**

```bash
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj --filter "FullyQualifiedName~MSOSync.MetadataTests.Caching" --no-build 2>&1 | head -30
```

Expected: Compilation errors — `MSOSync.Common.Caching` namespace and `InMemoryCacheService` not found.

- [ ] **Step 3: Add package references to `MSOSync.Common.csproj`**

The current `src/MSOSync.Common/MSOSync.Common.csproj` only has a `<Description>` property group and no explicit package references. Replace it with:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <Description>Shared models, exceptions, enums, constants, and utilities</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
    <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" />
    <PackageReference Include="StackExchange.Redis" />
  </ItemGroup>
</Project>
```

Note: `StackExchange.Redis` is added here even though `RedisCacheService` is written in Task 2. The package version entry will be added to `Directory.Packages.props` in Task 2. For Task 1 to build, the version must already exist — do **not** run the build step below until Task 2 has added the version to `Directory.Packages.props`. If you prefer, defer the `StackExchange.Redis` and `HealthChecks.Abstractions` lines to Task 2 and add the others now. Either order works as long as both tasks run before `dotnet build`.

**Recommended approach for Task 1:** Add all four packages to the csproj now; add the `Directory.Packages.props` version entry in Task 2 Step 1 before building.

- [ ] **Step 4: Create `CacheOptions`**

Create `src/MSOSync.Common/Caching/CacheOptions.cs`:

```csharp
namespace MSOSync.Common.Caching;

public sealed class CacheOptions
{
    public const string Section = "Cache";

    /// <summary>"Memory" (default) or "Redis".</summary>
    public string Provider { get; set; } = "Memory";

    /// <summary>
    /// StackExchange.Redis connection string.
    /// Required when Provider == "Redis". Ignored when Provider == "Memory".
    /// Example: "localhost:6379,password=secret,abortConnect=false"
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Default TTL applied when SetAsync is called with expiry == null.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(5);
}
```

- [ ] **Step 5: Create `ICacheService`**

Create `src/MSOSync.Common/Caching/ICacheService.cs`:

```csharp
namespace MSOSync.Common.Caching;

public interface ICacheService
{
    /// <summary>Returns the cached value, or default(T) if the key does not exist.</summary>
    Task<T?> GetAsync<T>(string key, CancellationToken ct = default);

    /// <summary>
    /// Stores a value under the specified key.
    /// If <paramref name="expiry"/> is null, <see cref="CacheOptions.DefaultExpiry"/> is used.
    /// </summary>
    Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default);

    /// <summary>Removes a single key. No-op if the key does not exist.</summary>
    Task RemoveAsync(string key, CancellationToken ct = default);

    /// <summary>
    /// Removes all keys whose string representation begins with <paramref name="prefix"/>.
    /// Memory provider: throws NotSupportedException.
    /// Redis provider: uses SCAN + DEL.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
```

- [ ] **Step 6: Create `CacheKeyHelper`**

Create `src/MSOSync.Common/Caching/CacheKeyHelper.cs`:

```csharp
namespace MSOSync.Common.Caching;

/// <summary>
/// Centralized cache key factory. Ensures consistent key format across all callers.
/// Pattern: {domain}:{entity}:{qualifier}
/// </summary>
public static class CacheKeyHelper
{
    // ── Metadata ──────────────────────────────────────────────────────────────

    /// <summary>Single node: "metadata:node:{nodeId}"</summary>
    public static string Node(string nodeId)
        => $"metadata:node:{nodeId}";

    /// <summary>Single channel: "metadata:channel:{channelId}"</summary>
    public static string Channel(string channelId)
        => $"metadata:channel:{channelId}";

    /// <summary>Single trigger: "metadata:trigger:{triggerId}"</summary>
    public static string Trigger(string triggerId)
        => $"metadata:trigger:{triggerId}";

    /// <summary>Single router: "metadata:router:{routerId}"</summary>
    public static string Router(string routerId)
        => $"metadata:router:{routerId}";

    /// <summary>Single parameter: "metadata:parameter:{name}"</summary>
    public static string Parameter(string name)
        => $"metadata:parameter:{name}";

    // ── Topology ──────────────────────────────────────────────────────────────

    /// <summary>Topology graph: "topology:graph"</summary>
    public static string TopologyGraph()
        => "topology:graph";

    /// <summary>Topology groups list: "topology:groups:v1"</summary>
    public static string TopologyGroups()
        => "topology:groups:v1";

    // ── Metrics ───────────────────────────────────────────────────────────────

    /// <summary>Metrics summary: "metrics:summary:v1"</summary>
    public static string MetricsSummary()
        => "metrics:summary:v1";

    /// <summary>Node metrics list: "metrics:nodes:v1"</summary>
    public static string MetricsNodes()
        => "metrics:nodes:v1";

    /// <summary>Channel metrics list: "metrics:channels:v1"</summary>
    public static string MetricsChannels()
        => "metrics:channels:v1";

    // ── Routing ───────────────────────────────────────────────────────────────

    /// <summary>Routing table for a trigger: "routing:trigger:{triggerId}"</summary>
    public static string RoutingTrigger(string triggerId)
        => $"routing:trigger:{triggerId}";

    // ── Permissions ───────────────────────────────────────────────────────────

    /// <summary>Role permission list: "permissions:{roleName}"</summary>
    public static string Permissions(string roleName)
        => $"permissions:{roleName}";

    // ── Overview ──────────────────────────────────────────────────────────────

    /// <summary>Overview snapshot: "overview:snapshot"</summary>
    public static string OverviewSnapshot()
        => "overview:snapshot";

    // ── Prefix helpers (for RemoveByPrefixAsync) ──────────────────────────────

    /// <summary>All metadata node keys: "metadata:node:"</summary>
    public static string NodePrefix() => "metadata:node:";

    /// <summary>All metadata channel keys: "metadata:channel:"</summary>
    public static string ChannelPrefix() => "metadata:channel:";

    /// <summary>All metadata trigger keys: "metadata:trigger:"</summary>
    public static string TriggerPrefix() => "metadata:trigger:";

    /// <summary>All routing keys: "routing:"</summary>
    public static string RoutingPrefix() => "routing:";

    /// <summary>All metrics keys: "metrics:"</summary>
    public static string MetricsPrefix() => "metrics:";

    /// <summary>All topology keys: "topology:"</summary>
    public static string TopologyPrefix() => "topology:";
}
```

- [ ] **Step 7: Create `InMemoryCacheService`**

Create `src/MSOSync.Common/Caching/InMemoryCacheService.cs`:

```csharp
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace MSOSync.Common.Caching;

internal sealed class InMemoryCacheService(
    IMemoryCache cache,
    IOptions<CacheOptions> options) : ICacheService
{
    private readonly CacheOptions _opts = options.Value;

    public Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        cache.TryGetValue(key, out T? value);
        return Task.FromResult(value);
    }

    public Task SetAsync<T>(string key, T value, TimeSpan? expiry = null, CancellationToken ct = default)
    {
        var ttl = expiry ?? _opts.DefaultExpiry;
        cache.Set(key, value, ttl);
        return Task.CompletedTask;
    }

    public Task RemoveAsync(string key, CancellationToken ct = default)
    {
        cache.Remove(key);
        return Task.CompletedTask;
    }

    public Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default)
        => throw new NotSupportedException(
            "RemoveByPrefixAsync is not supported by the InMemory cache provider. " +
            "Switch to Provider=Redis or invalidate keys individually.");
}
```

- [ ] **Step 8: Create `CachingExtensions` (Memory branch only)**

Create `src/MSOSync.Common/Caching/CachingExtensions.cs`:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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

    // Partial method — filled in by Task 2 when RedisCacheService exists.
    // Declared as a separate private method so Task 2 can replace the body
    // without touching the public method signature.
    private static void RegisterRedis(IServiceCollection services)
    {
        // Task 2 replaces this body with Redis registration.
        // For Task 1, this path is unreachable when Provider=Memory (the default).
        throw new InvalidOperationException(
            "Redis provider is not yet wired. Add RedisCacheService (Task 2) first.");
    }
}
```

Note: Task 2 will replace the `RegisterRedis` body in-place. The public `AddCacheService` signature is already correct and does not change.

- [ ] **Step 9: Run the failing tests to verify types compile**

```bash
dotnet build src/MSOSync.Common/MSOSync.Common.csproj
```

Expected: Builds (StackExchange.Redis will fail until Task 2 adds the version to `Directory.Packages.props`). If you deferred the Redis package refs to Task 2, this builds cleanly now.

- [ ] **Step 10: Run `InMemoryCacheService` tests**

```bash
dotnet test tests/MSOSync.MetadataTests/MSOSync.MetadataTests.csproj --filter "FullyQualifiedName~MSOSync.MetadataTests.Caching.InMemoryCacheServiceTests" -v normal
```

Expected: All 8 tests pass.

- [ ] **Step 11: Commit**

```bash
git add src/MSOSync.Common/Caching/ICacheService.cs
git add src/MSOSync.Common/Caching/CacheOptions.cs
git add src/MSOSync.Common/Caching/CacheKeyHelper.cs
git add src/MSOSync.Common/Caching/InMemoryCacheService.cs
git add src/MSOSync.Common/Caching/CachingExtensions.cs
git add src/MSOSync.Common/MSOSync.Common.csproj
git add tests/MSOSync.MetadataTests/Caching/InMemoryCacheServiceTests.cs
git commit -m "feat(2D.1-T1): add ICacheService abstraction, CacheKeyHelper, InMemoryCacheService"
```
