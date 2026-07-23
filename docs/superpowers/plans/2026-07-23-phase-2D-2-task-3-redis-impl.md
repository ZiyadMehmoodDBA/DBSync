# Task 3: RedisDistributedLockService

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the Redis-based distributed lock (single-node Redlock: SET NX PX + Lua scripts for owner-guarded renew/release). Registered only when `Provider=Redis`.

**Prerequisite:** Task 1 complete (`IDistributedLockService`, `IDistributedLock` exist). Task 2 complete (StackExchange.Redis already added to `MSOSync.Persistence.csproj`, `DistributedLockServiceExtensions` exists with a SQL-only registration).

**Files:**
- Create: `src/MSOSync.Persistence/Lock/RedisDistributedLock.cs`
- Create: `src/MSOSync.Persistence/Lock/RedisDistributedLockService.cs`
- Modify: `src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs` — add conditional Redis branch
- Create: `tests/MSOSync.Tests/Lock/RedisDistributedLockServiceTests.cs`
- Modify: `tests/MSOSync.Tests/MSOSync.Tests.csproj` — already has Moq; no additional packages needed

**Interfaces:**
- Consumes: `IDistributedLockService`, `IDistributedLock` from Task 1; `StackExchange.Redis.IConnectionMultiplexer` from the consuming host
- Produces: `RedisDistributedLockService` implementing `IDistributedLockService` — registered as scoped when `Provider=Redis`

---

- [ ] **Step 1: Write the failing tests first**

Create `tests/MSOSync.Tests/Lock/RedisDistributedLockServiceTests.cs`:

```csharp
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
```

- [ ] **Step 2: Run failing tests**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "FullyQualifiedName~RedisDistributedLockServiceTests" -v minimal
```

Expected: compile errors — `RedisDistributedLockService` does not exist yet.

- [ ] **Step 3: Write `RedisDistributedLock.cs`**

Create `src/MSOSync.Persistence/Lock/RedisDistributedLock.cs`:

```csharp
using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

internal sealed class RedisDistributedLock : IDistributedLock
{
    private readonly RedisDistributedLockService _service;
    private bool _disposed;

    public string         Resource  { get; }
    public string         Owner     { get; }
    public DateTimeOffset ExpiresAt { get; }

    internal RedisDistributedLock(
        RedisDistributedLockService service,
        string resource,
        string owner,
        DateTimeOffset expiresAt)
    {
        _service  = service;
        Resource  = resource;
        Owner     = owner;
        ExpiresAt = expiresAt;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _service.ReleaseAsync(Resource, Owner, CancellationToken.None);
    }
}
```

- [ ] **Step 4: Write `RedisDistributedLockService.cs`**

Create `src/MSOSync.Persistence/Lock/RedisDistributedLockService.cs`:

```csharp
using MSOSync.Common.Locks;
using StackExchange.Redis;

namespace MSOSync.Persistence.Lock;

public sealed class RedisDistributedLockService(
    IConnectionMultiplexer redis) : IDistributedLockService
{
    // Renew: extend expiry only if caller is the current owner
    private static readonly string RenewScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then " +
        "    return redis.call('PEXPIRE', KEYS[1], ARGV[2]) " +
        "else " +
        "    return 0 " +
        "end";

    // Release: delete key only if caller is the current owner (canonical Redlock release)
    private static readonly string ReleaseScript =
        "if redis.call('GET', KEYS[1]) == ARGV[1] then " +
        "    return redis.call('DEL', KEYS[1]) " +
        "else " +
        "    return 0 " +
        "end";

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var db      = redis.GetDatabase();
        var ok      = await db.StringSetAsync(resource, owner, expiry, When.NotExists);

        if (!ok) return null;

        return new RedisDistributedLock(this, resource, owner, DateTimeOffset.UtcNow.Add(expiry));
    }

    public async Task<bool> RenewAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var db     = redis.GetDatabase();
        var result = (long)await db.ScriptEvaluateAsync(
            RenewScript,
            new RedisKey[]   { resource },
            new RedisValue[] { owner, (long)expiry.TotalMilliseconds });
        return result == 1;
    }

    public async Task ReleaseAsync(
        string resource, string owner, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.ScriptEvaluateAsync(
            ReleaseScript,
            new RedisKey[]   { resource },
            new RedisValue[] { owner });
    }

    public async Task<bool> IsHeldAsync(
        string resource, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        return await db.KeyExistsAsync(resource);
    }
}
```

- [ ] **Step 5: Update `DistributedLockServiceExtensions` to add the Redis branch**

Edit `src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs`.

Replace the current body (SQL-only from Task 2 Step 15 temporary version) with the full conditional version:

```csharp
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MSOSync.Common.Locks;

namespace MSOSync.Persistence.Lock;

public static class DistributedLockServiceExtensions
{
    public static IServiceCollection AddDistributedLocks(
        this IServiceCollection services,
        IConfiguration          configuration)
    {
        services.Configure<DistributedLockOptions>(
            configuration.GetSection(DistributedLockOptions.SectionName));

        var provider = configuration
            .GetSection(DistributedLockOptions.SectionName)["Provider"] ?? "Sql";

        if (provider.Equals("Redis", StringComparison.OrdinalIgnoreCase))
        {
            // IConnectionMultiplexer must already be registered by the caller
            // (e.g., via AddSingleton<IConnectionMultiplexer>(...) in Phase 2D.1 Redis setup).
            services.AddScoped<IDistributedLockService, RedisDistributedLockService>();
        }
        else
        {
            services.AddScoped<IDistributedLockService, SqlDistributedLockService>();
        }

        return services;
    }
}
```

Note: Remove the `using StackExchange.Redis;` import from the extensions file — `IConnectionMultiplexer` is not referenced here directly.

- [ ] **Step 6: Run the Redis unit tests**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "FullyQualifiedName~RedisDistributedLockServiceTests" -v minimal
```

Expected: `Passed: 7, Failed: 0`

- [ ] **Step 7: Build the full solution**

```
dotnet build MSOSync.sln
```

Expected: `Build succeeded.` with 0 errors. (Verify from solution root where `MSOSync.sln` lives.)

- [ ] **Step 8: Run all tests in MSOSync.Tests**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj -v minimal
```

Expected: all tests pass (DistributedLockHelperTests + SqlDistributedLockServiceTests + RedisDistributedLockServiceTests).

- [ ] **Step 9: Commit**

```
git add src/MSOSync.Persistence/Lock/RedisDistributedLock.cs
git add src/MSOSync.Persistence/Lock/RedisDistributedLockService.cs
git add src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs
git add tests/MSOSync.Tests/Lock/RedisDistributedLockServiceTests.cs
git commit -m "feat(2D.2-T3): RedisDistributedLockService with Lua owner-guarded renew/release"
```
