# Phase 2D.1 — Cache Abstraction + Redis

**Status:** Draft  
**Date:** 2026-07-23  
**Phase:** 2D — Scalability & Performance  
**Prerequisite:** Phase 2A (Platform Stabilization) complete

---

## 1. Goal

Replace all direct `IMemoryCache` usage across the solution with a single `ICacheService` abstraction that lives in `MSOSync.Common`. The abstraction must support two pluggable providers:

- **Memory** (default) — wraps `IMemoryCache`; zero new dependencies, identical runtime behavior for all existing callers.
- **Redis** — wraps `StackExchange.Redis`; serializes values with `System.Text.Json`; enables cache sharing across horizontally scaled API instances.

Provider selection is entirely configuration-driven. Switching from Memory to Redis requires only an `appsettings.json` change and a restart — no code change.

---

## 2. Architecture

### 2.1 Abstraction Layer

```
MSOSync.Common
└── Caching/
    ├── ICacheService.cs          ← public interface (contract)
    ├── CacheOptions.cs           ← IOptions<CacheOptions>
    ├── CacheKeyHelper.cs         ← static key factory
    └── CachingExtensions.cs      ← AddCacheService(IServiceCollection, IConfiguration)

MSOSync.Common (implementations — same assembly, no new project)
    ├── InMemoryCacheService.cs   ← IMemoryCache wrapper
    └── RedisCacheService.cs      ← IConnectionMultiplexer wrapper
```

Both implementations are internal to `MSOSync.Common`. External projects consume only `ICacheService`.

### 2.2 Dependency Graph (no circular references)

```
MSOSync.Common
  └── Microsoft.Extensions.Caching.Memory   (already in Directory.Packages.props)
  └── StackExchange.Redis                   (new — add to Directory.Packages.props)
  └── Microsoft.Extensions.Options.ConfigurationExtensions (existing)

MSOSync.Metadata  →  MSOSync.Common  (existing edge; gains ICacheService from same path)
MSOSync.Routing   →  MSOSync.Common  (existing edge; gains ICacheService from same path)
MSOSync.Api       →  MSOSync.Common  (existing edge)
```

`MSOSync.Common` takes on the StackExchange.Redis package reference. This is acceptable: the package is only instantiated when `Provider == "Redis"`. When `Provider == "Memory"`, the Redis connection is never created and no Redis assemblies need to be loaded at runtime in any meaningful sense — they are present on disk but the DI registration path never touches `IConnectionMultiplexer`.

---

## 3. `ICacheService` Interface

File: `src/MSOSync.Common/Caching/ICacheService.cs`  
Namespace: `MSOSync.Common.Caching`

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
    /// For InMemory: not supported — throws NotSupportedException (see §8 Error Handling).
    /// For Redis: uses SCAN + DEL with the prefix pattern.
    /// </summary>
    Task RemoveByPrefixAsync(string prefix, CancellationToken ct = default);
}
```

**Design notes:**

- All methods are async. The `InMemoryCacheService` wraps synchronous `IMemoryCache` calls in `Task.FromResult` / `ValueTask.CompletedTask` conversions — no actual I/O, no thread blocking.
- `GetAsync<T>` returns `T?` (nullable). Callers check for null to detect a cache miss. No `TryGet` pattern — the interface stays minimal.
- `RemoveByPrefixAsync` is a Redis-only capability. The InMemory implementation throws `NotSupportedException` with a clear message. This method is not called by any current caller; it is added for future use (e.g., invalidating all `metadata:node:*` keys at once). If callers need prefix invalidation with the memory provider, they must track keys themselves.

---

## 4. `CacheOptions`

File: `src/MSOSync.Common/Caching/CacheOptions.cs`  
Namespace: `MSOSync.Common.Caching`

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
    /// Example: "localhost:6379,password=secret,ssl=false,abortConnect=false"
    /// </summary>
    public string? RedisConnectionString { get; set; }

    /// <summary>
    /// Default TTL applied when SetAsync is called with expiry == null.
    /// Defaults to 5 minutes.
    /// </summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromMinutes(5);
}
```

**`appsettings.json` shape (Memory — default):**

```json
{
  "Cache": {
    "Provider": "Memory",
    "DefaultExpiry": "00:05:00"
  }
}
```

**`appsettings.json` shape (Redis):**

```json
{
  "Cache": {
    "Provider": "Redis",
    "RedisConnectionString": "localhost:6379,abortConnect=false",
    "DefaultExpiry": "00:05:00"
  }
}
```

---

## 5. Implementations

### 5.1 `InMemoryCacheService`

File: `src/MSOSync.Common/Caching/InMemoryCacheService.cs`  
Namespace: `MSOSync.Common.Caching`

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

**Important:** `IMemoryCache` is registered as a singleton by `AddMemoryCache()`. `InMemoryCacheService` is registered as **singleton** (matching the cache lifetime). Scoped services that depend on `ICacheService` are safe to receive a singleton — this is the existing pattern.

### 5.2 `RedisCacheService`

File: `src/MSOSync.Common/Caching/RedisCacheService.cs`  
Namespace: `MSOSync.Common.Caching`

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

**Serialization contract:** `System.Text.Json` with camelCase property naming. All types stored in `ICacheService` must be serializable (no `IMemoryCache`-specific features like `PostEvictionCallbacks` or change tokens are carried across; callers that needed those continue to use `IMemoryCache` directly — see §9).

**Redis connection lifetime:** `IConnectionMultiplexer` is registered as a **singleton** by `AddCacheService`. It is thread-safe and designed for reuse across the application lifetime. Never create a new `ConnectionMultiplexer` per request.

---

## 6. Key Convention — `CacheKeyHelper`

File: `src/MSOSync.Common/Caching/CacheKeyHelper.cs`  
Namespace: `MSOSync.Common.Caching`

### 6.1 Pattern

All cache keys follow the pattern:

```
{domain}:{entity}:{qualifier}
```

Where:
- **domain** identifies the subsystem (e.g., `metadata`, `topology`, `metrics`, `routing`, `permissions`, `overview`)
- **entity** identifies the type of object (e.g., `node`, `channel`, `trigger`, `router`, `parameter`)
- **qualifier** identifies the specific instance or variant (e.g., a node ID, `v1`, `summary`)

No tenant segment is included in Phase 2D.1. The current codebase is single-tenant. A `{tenantId}:` prefix will be prepended in a future phase when multi-tenant caching is required (Phase 15 migration path), and `CacheKeyHelper` will be extended at that point.

### 6.2 Static Class

```csharp
namespace MSOSync.Common.Caching;

/// <summary>
/// Centralized cache key factory. Ensures consistent key format across all callers.
/// Pattern: {domain}:{entity}:{qualifier}
/// </summary>
public static class CacheKeyHelper
{
    // ── Metadata ─────────────────────────────────────────────────────────────

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

    // ── Topology ─────────────────────────────────────────────────────────────

    /// <summary>Topology graph: "topology:graph"</summary>
    public static string TopologyGraph()
        => "topology:graph";

    /// <summary>Topology groups list: "topology:groups:v1"</summary>
    public static string TopologyGroups()
        => "topology:groups:v1";

    // ── Metrics ──────────────────────────────────────────────────────────────

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

    // ── Permissions ──────────────────────────────────────────────────────────

    /// <summary>Role permission list: "permissions:{roleName}"</summary>
    public static string Permissions(string roleName)
        => $"permissions:{roleName}";

    // ── Overview ─────────────────────────────────────────────────────────────

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

**Why a static class and not constants?** The qualifier segment (node ID, role name, etc.) is runtime data. Methods with parameters generate the full key. The prefix helpers return fixed strings for use with `RemoveByPrefixAsync`.

---

## 7. DI Registration

File: `src/MSOSync.Common/Caching/CachingExtensions.cs`  
Namespace: `MSOSync.Common.Caching`

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
    /// based on Cache:Provider in configuration.
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
            services.AddSingleton<IConnectionMultiplexer>(sp =>
            {
                var opts = sp.GetRequiredService<IOptions<CacheOptions>>().Value;

                if (string.IsNullOrWhiteSpace(opts.RedisConnectionString))
                    throw new InvalidOperationException(
                        "Cache:RedisConnectionString must be set when Cache:Provider is \"Redis\".");

                return ConnectionMultiplexer.Connect(opts.RedisConnectionString);
            });

            services.AddSingleton<ICacheService, RedisCacheService>();
        }
        else
        {
            // Memory provider — ensure IMemoryCache is registered (idempotent)
            services.AddMemoryCache();
            services.AddSingleton<ICacheService, InMemoryCacheService>();
        }

        return services;
    }
}
```

**Call site in `MSOSync.Api` / `Program.cs`:**

```csharp
builder.Services.AddCacheService(builder.Configuration);
```

**Call site in `MetadataServiceExtensions.AddMetadata`:** The existing `services.AddMemoryCache()` call remains unchanged (it is idempotent and still required for `RoutingService` which uses a `CancellationChangeToken` — see §9). No other change is needed inside `AddMetadata` for the DI wiring; the `ICacheService` singleton is resolved from the top-level container.

---

## 8. Migration Plan — Replacing Direct `IMemoryCache` Callers

Nine files currently depend on `IMemoryCache`. Each falls into one of two migration tiers:

### Tier A — Full migration to `ICacheService`

These callers use `IMemoryCache` only for simple Get/Set/Remove by key. They map directly onto `ICacheService` and will be migrated.

| File | Current keys | Migration action |
|---|---|---|
| `Overview/OverviewSnapshotCache.cs` | `"overview_snapshot"` | Replace with `CacheKeyHelper.OverviewSnapshot()`, replace `IMemoryCache` with `ICacheService`, adapt `GetOrCreateAsync` |
| `Topology/TopologyQueryService.cs` | `"topology:graph"`, `"topology:groups:v1"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper.TopologyGraph()` / `CacheKeyHelper.TopologyGroups()` |
| `Metrics/MetricsQueryService.cs` | `"metrics:summary:v1"`, `"metrics:nodes:v1"`, `"metrics:channels:v1"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper` |
| `Permissions/PermissionService.cs` | `"permissions:{roleName}"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper.Permissions(roleName)` |
| `Services/NodeMetadataService.cs` | `"metadata:node:{nodeId}"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper.Node(nodeId)` |
| `Services/ChannelMetadataService.cs` | `"metadata:channel:{channelId}"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper.Channel(channelId)` |
| `Services/TriggerMetadataService.cs` | `"metadata:trigger:{triggerId}"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper.Trigger(triggerId)` |
| `Services/RouterMetadataService.cs` | `"metadata:router:{routerId}"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper.Router(routerId)` |
| `Services/ParameterMetadataService.cs` | `"metadata:parameter:{name}"` | Replace `IMemoryCache` with `ICacheService`, use `CacheKeyHelper.Parameter(name)` |

### Tier B — Retain `IMemoryCache` directly (not migrated)

| File | Reason to keep `IMemoryCache` |
|---|---|
| `Routing/RoutingService.cs` | Uses `cache.CreateEntry(key).AddExpirationToken(new CancellationChangeToken(...))` — this is a `MemoryCacheEntryOptions`-specific feature with no equivalent in `ICacheService`. The `RoutingService` should retain its direct `IMemoryCache` dependency. `IMemoryCache` is still registered by `AddMemoryCache()` (called from both `MetadataServiceExtensions` and `RoutingServiceExtensions`). |

`RoutingService` is a legitimate exception: its invalidation model is token-based mass eviction, not key-by-key removal. `ICacheService.RemoveByPrefixAsync` on Redis would work for this purpose, but the token-based in-process notification would be lost, changing the eviction semantics. The routing table is extremely hot; changing its invalidation model is a separate, deliberate decision and is out of scope for 2D.1.

### Migration Code Shape

Each Tier A service changes its constructor from:

```csharp
// Before
public sealed class TopologyQueryService(AppDbContext db, IMemoryCache cache)
```

to:

```csharp
// After
using MSOSync.Common.Caching;

public sealed class TopologyQueryService(AppDbContext db, ICacheService cache)
```

And each Get/Set/Remove call converts:

```csharp
// Before — synchronous TryGetValue
if (cache.TryGetValue("topology:graph", out TopologyGraphDto? cached))
    return cached!;
// ...
cache.Set("topology:graph", result, CacheOptions);

// After — async await
var cached = await cache.GetAsync<TopologyGraphDto>(CacheKeyHelper.TopologyGraph(), ct);
if (cached is not null)
    return cached;
// ...
await cache.SetAsync(CacheKeyHelper.TopologyGraph(), result, TimeSpan.FromSeconds(60), ct);
```

```csharp
// Before
cache.Remove($"metadata:node:{nodeId}");

// After
await cache.RemoveAsync(CacheKeyHelper.Node(nodeId), ct);
```

### `OverviewSnapshotCache` — Special Case

`OverviewSnapshotCache` has a double-checked locking pattern with a `SemaphoreSlim`. This logic remains intact; only the underlying cache calls change:

```csharp
// Before
cache.TryGetValue(Key, out OverviewDto? dto)
cache.Set(Key, dto, Ttl);

// After
var dto = await _cache.GetAsync<OverviewDto>(CacheKeyHelper.OverviewSnapshot(), ct);
// ...
await _cache.SetAsync(CacheKeyHelper.OverviewSnapshot(), dto, Ttl, ct);
```

The `SemaphoreSlim _refreshLock` is retained — it prevents stampede in the Memory provider. With Redis it provides an additional in-process guard (not strictly required but harmless and cheap).

The field `Key` constant and `Ttl` constant become local call-site constants. The semaphore field stays.

---

## 9. Error Handling

### 9.1 Redis Down — No Automatic Fallback

`RedisCacheService` does **not** automatically fall back to an in-process memory cache when Redis is unavailable. The rationale:

- A silent fallback creates a split-brain: multiple instances diverge on cached data while believing they are consistent.
- A Redis outage should surface immediately in health checks, not be silently absorbed.
- Callers that cannot tolerate a cache miss (extreme hot paths) should use their own fallback strategy.

The behavior on Redis failure is: all `GetAsync` calls return `default(T)` (cache miss), all `SetAsync`/`RemoveAsync` calls are swallowed. The application continues to function — it hits the database on every request until Redis recovers. This is the same behaviour as starting the application cold.

All Redis exceptions are logged at `Warning` level with the key name. Operators monitoring logs or metrics will see the degradation immediately.

### 9.2 Redis Connection Failure at Startup

If `ConnectionMultiplexer.Connect` throws (bad connection string, DNS failure, auth error), the exception propagates out of the DI registration lambda and the application fails to start. This is intentional: if the operator configures Redis, Redis must be available.

To use Redis in a graceful-degrade mode (start without Redis, connect later), the operator must configure `abortConnect=false` in the connection string. With this flag, `ConnectionMultiplexer.Connect` will succeed even if Redis is unreachable; individual commands will fail and be swallowed per §9.1.

Recommended connection string for non-strict environments:

```
localhost:6379,abortConnect=false,connectTimeout=3000,syncTimeout=3000
```

### 9.3 `InMemoryCacheService` — `RemoveByPrefixAsync`

Throws `NotSupportedException` synchronously. No callers in Phase 2D.1 call this method; it exists for future use. If it is accidentally called against the Memory provider, the exception will surface immediately in development/test, making the misconfiguration obvious.

### 9.4 Health Check

A Redis health check is registered when `Provider == "Redis"`:

```csharp
// In CachingExtensions.AddCacheService, after the Redis branch:
services.AddHealthChecks()
    .AddCheck<RedisCacheHealthCheck>("redis-cache");
```

`RedisCacheHealthCheck` pings the Redis server with a `PING` command and reports `Healthy`/`Unhealthy`. This integrates with the existing `ISystemHealthService`.

File: `src/MSOSync.Common/Caching/RedisCacheHealthCheck.cs`

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

The health check is only registered in the Redis branch of `AddCacheService`. Memory provider installs no cache health check.

---

## 10. Package Changes

### `Directory.Packages.props`

Add StackExchange.Redis to the existing `Extensions` item group:

```xml
<ItemGroup Label="Caching">
  <PackageVersion Include="StackExchange.Redis" Version="2.8.16" />
</ItemGroup>
```

### `src/MSOSync.Common/MSOSync.Common.csproj`

Add conditional package reference. The Redis assembly is always compiled into `MSOSync.Common` but only instantiated when the provider is configured:

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Extensions.Caching.Memory" />
  <PackageReference Include="Microsoft.Extensions.Options.ConfigurationExtensions" />
  <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" />
  <PackageReference Include="Microsoft.Extensions.Diagnostics.HealthChecks.Abstractions" />
  <PackageReference Include="StackExchange.Redis" />
</ItemGroup>
```

No project other than `MSOSync.Common` references `StackExchange.Redis` directly.

---

## 11. Testing

### 11.1 Unit Tests — Service Layer

All Tier A services now depend on `ICacheService`. Mock with `Moq`:

```csharp
var cacheMock = new Mock<ICacheService>();
cacheMock
    .Setup(c => c.GetAsync<TopologyGraphDto>(CacheKeyHelper.TopologyGraph(), It.IsAny<CancellationToken>()))
    .ReturnsAsync((TopologyGraphDto?)null); // simulate miss

cacheMock
    .Setup(c => c.SetAsync(
        CacheKeyHelper.TopologyGraph(),
        It.IsAny<TopologyGraphDto>(),
        It.IsAny<TimeSpan?>(),
        It.IsAny<CancellationToken>()))
    .Returns(Task.CompletedTask);
```

This is strictly simpler than mocking `IMemoryCache` (which requires `TryGetValue` out-parameter setup).

### 11.2 Unit Tests — `InMemoryCacheService`

File: `tests/MSOSync.Common.Tests/Caching/InMemoryCacheServiceTests.cs`

Scenarios:
- `GetAsync` returns `null` on miss.
- `SetAsync` then `GetAsync` returns the value.
- `SetAsync` with explicit expiry; value absent after expiry (use real `MemoryCache` with `SystemClock` override or test with a very short TTL and `Task.Delay`).
- `RemoveAsync` after `SetAsync` returns `null`.
- `RemoveByPrefixAsync` throws `NotSupportedException`.
- Default expiry from `CacheOptions.DefaultExpiry` is applied when `expiry == null`.

### 11.3 Unit Tests — `RedisCacheService`

File: `tests/MSOSync.Common.Tests/Caching/RedisCacheServiceTests.cs`

Mock `IConnectionMultiplexer` and `IDatabase`:

```csharp
var dbMock = new Mock<IDatabase>();
var muxMock = new Mock<IConnectionMultiplexer>();
muxMock.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(dbMock.Object);
```

Scenarios:
- `GetAsync` returns deserialized value when Redis returns a JSON string.
- `GetAsync` returns `null` when Redis returns `RedisValue.Null`.
- `GetAsync` returns `null` and logs warning when `RedisException` thrown.
- `SetAsync` calls `StringSetAsync` with correct JSON and TTL.
- `SetAsync` swallows `RedisTimeoutException` and logs warning.
- `RemoveAsync` calls `KeyDeleteAsync`.
- `RemoveByPrefixAsync` scans keys and batches deletes.

### 11.4 Integration Tests — Redis via Testcontainers

File: `tests/MSOSync.Integration.Tests/Caching/RedisCacheIntegrationTests.cs`

```csharp
[Collection("Redis")]
public class RedisCacheIntegrationTests : IAsyncLifetime
{
    private RedisContainer _container = default!;
    private RedisCacheService _svc = default!;

    public async Task InitializeAsync()
    {
        // Skip if Docker unavailable
        if (!DockerIsAvailable())
        {
            // Signal to xUnit to skip — use [Trait("Category","Integration")] + filter
            return;
        }

        _container = new RedisBuilder().Build();
        await _container.StartAsync();

        var mux = await ConnectionMultiplexer.ConnectAsync(_container.GetConnectionString());
        var opts = Options.Create(new CacheOptions { DefaultExpiry = TimeSpan.FromMinutes(5) });
        var logger = NullLogger<RedisCacheService>.Instance;
        _svc = new RedisCacheService(mux, opts, logger);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
```

Scenarios:
- Round-trip: Set a value, Get returns it correctly deserialized.
- Expiry: Set with 100ms TTL; after 200ms Get returns null.
- Remove: Set then Remove; Get returns null.
- RemoveByPrefix: Set three keys with same prefix; RemoveByPrefixAsync; all three return null.
- Large payload: 10 KB JSON blob; deserializes correctly.

**Testcontainers.Redis** package version — add to `Directory.Packages.props`:

```xml
<PackageVersion Include="Testcontainers.Redis" Version="4.4.0" />
```

(Matches existing `Testcontainers.MsSql` version for consistency.)

Add package reference to the integration test project `.csproj`:

```xml
<PackageReference Include="Testcontainers.Redis" />
```

Docker skip guard helper:

```csharp
private static bool DockerIsAvailable()
{
    try
    {
        using var proc = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
        {
            FileName = "docker",
            Arguments = "info",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        });
        proc!.WaitForExit(3000);
        return proc.ExitCode == 0;
    }
    catch { return false; }
}
```

### 11.5 Architecture Tests

Extend existing arch tests in `tests/MSOSync.Architecture.Tests/`:

```csharp
// MSOSync.Metadata must not reference StackExchange.Redis directly
Types.InAssembly(MetadataAssembly)
    .ShouldNot()
    .HaveDependencyOn("StackExchange.Redis")
    .GetResult().IsSuccessful.Should().BeTrue();

// MSOSync.Api must not reference StackExchange.Redis directly
Types.InAssembly(ApiAssembly)
    .ShouldNot()
    .HaveDependencyOn("StackExchange.Redis")
    .GetResult().IsSuccessful.Should().BeTrue();
```

---

## 12. Global Constraints

| Constraint | Detail |
|---|---|
| No EF migrations | `ICacheService` is infrastructure only; no database schema changes. |
| No new projects | All cache code lives in `MSOSync.Common`. No `MSOSync.Caching` project. |
| `IMemoryCache` retained for `RoutingService` | The `CancellationChangeToken` eviction model is irreplaceable via `ICacheService`. `IMemoryCache` remains registered. |
| `ICacheService` is singleton | Both implementations are registered as singletons. Scoped services may safely receive a singleton cache (consistent with current IMemoryCache pattern). |
| `StackExchange.Redis` in `MSOSync.Common` only | No other project references the Redis package directly. |
| Serialization: `System.Text.Json` only | No Newtonsoft.Json. All cached types must be `System.Text.Json`-serializable (no constructor-binding issues, no `[JsonConstructor]` gaps). Verify per-type at migration time. |
| Key format locked | Keys must be generated via `CacheKeyHelper`. Hard-coded key strings are a breaking smell when migrating to Redis (namespace collisions between environments require key prefixing strategy, added later). |
| Existing behavior unchanged on `Provider=Memory` | The abstraction wraps `IMemoryCache` synchronously; all TTLs from existing code are preserved as explicit `expiry` arguments on each `SetAsync` call. |
| `RemoveByPrefixAsync` is Redis-only | Callers must not call this method when `Provider=Memory`. Current callers in 2D.1 scope: zero. |
| Thread safety | `InMemoryCacheService` relies on `IMemoryCache` thread safety (MS guarantee). `RedisCacheService` relies on `IConnectionMultiplexer` thread safety (StackExchange.Redis guarantee). `OverviewSnapshotCache._refreshLock` semaphore is preserved. |
| Logging | `RedisCacheService` logs at `Warning` on caught Redis exceptions. No logging in `InMemoryCacheService` (no I/O). |
| Health check registered only for Redis | The `redis-cache` health check endpoint only appears when `Provider=Redis`. Memory provider adds no health check. |

---

## 13. File Delivery Summary

| File | Action |
|---|---|
| `src/MSOSync.Common/Caching/ICacheService.cs` | Create |
| `src/MSOSync.Common/Caching/CacheOptions.cs` | Create |
| `src/MSOSync.Common/Caching/CacheKeyHelper.cs` | Create |
| `src/MSOSync.Common/Caching/CachingExtensions.cs` | Create |
| `src/MSOSync.Common/Caching/InMemoryCacheService.cs` | Create |
| `src/MSOSync.Common/Caching/RedisCacheService.cs` | Create |
| `src/MSOSync.Common/Caching/RedisCacheHealthCheck.cs` | Create |
| `src/MSOSync.Common/MSOSync.Common.csproj` | Modify (add package refs) |
| `Directory.Packages.props` | Modify (add StackExchange.Redis, Testcontainers.Redis) |
| `src/MSOSync.Metadata/Overview/OverviewSnapshotCache.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Topology/TopologyQueryService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Metrics/MetricsQueryService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Permissions/PermissionService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Services/NodeMetadataService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Services/ChannelMetadataService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Services/TriggerMetadataService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Services/RouterMetadataService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Metadata/Services/ParameterMetadataService.cs` | Modify (Tier A migration) |
| `src/MSOSync.Routing/RoutingService.cs` | No change (Tier B — retains `IMemoryCache`) |
| `src/MSOSync.Api/Program.cs` | Modify (add `AddCacheService(configuration)` call) |
| `tests/MSOSync.Common.Tests/Caching/InMemoryCacheServiceTests.cs` | Create |
| `tests/MSOSync.Common.Tests/Caching/RedisCacheServiceTests.cs` | Create |
| `tests/MSOSync.Integration.Tests/Caching/RedisCacheIntegrationTests.cs` | Create |
