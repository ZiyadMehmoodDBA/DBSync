# Phase 2D.2 — Distributed Lock Improvements

**Status:** Design approved — 2026-07-23
**Phase:** 2D — Scalability & Performance
**Last migration:** M034. No new migrations required.

---

## Goal

Replace the bare `IDatabaseLockProvider` / `DatabaseLockProvider` pair with a proper distributed-lock abstraction that:

1. Gives callers a clean, provider-agnostic `IDistributedLockService` interface in `MSOSync.Common`.
2. Retains the current SQL-based implementation (refactored) so existing behaviour is unchanged for SQL-only deployments.
3. Adds an optional Redis-based Redlock implementation that is activated only when Phase 2D.1 Redis is available.
4. Routes all three existing callers (`SyncJob`, `RetryJob`, `PurgeJob`) and the one API caller (`BatchController`) through the new interface.
5. Adds expiry semantics via a new `lock_expiry` column (M035 migration) so the SQL provider can honour configurable TTLs instead of the current hard-coded 10-minute stale threshold.

---

## Architecture

```
MSOSync.Common
└── Locks/
    ├── IDistributedLockService.cs    ← new public interface
    ├── IDistributedLock.cs           ← new public interface
    ├── DistributedLockOptions.cs     ← new options class
    └── LockProviderType.cs           ← new enum

MSOSync.Persistence
└── Lock/
    ├── IDatabaseLockProvider.cs      ← DELETED (replaced by IDistributedLockService)
    ├── DatabaseLockProvider.cs       ← DELETED (replaced by SqlDistributedLockService)
    ├── DatabaseLockLease.cs          ← DELETED (replaced by SqlDistributedLock)
    ├── SqlDistributedLockService.cs  ← NEW (implements IDistributedLockService)
    ├── SqlDistributedLock.cs         ← NEW (implements IDistributedLock)
    ├── LockNames.cs                  ← unchanged
    └── DistributedLockServiceExtensions.cs  ← NEW (AddDistributedLocks)

MSOSync.Infrastructure (or MSOSync.Persistence, see §Redis provider)
└── Lock/
    └── RedisDistributedLockService.cs  ← NEW, conditional on Provider=Redis

MSOSync.Scheduler
├── SyncJob.cs     ← updated: IDatabaseLockProvider → IDistributedLockService
├── RetryJob.cs    ← updated: IDatabaseLockProvider → IDistributedLockService
└── PurgeJob.cs    ← updated: IDatabaseLockProvider → IDistributedLockService

MSOSync.Api
└── Controllers/
    └── BatchController.cs  ← updated: IDatabaseLockProvider → IDistributedLockService

MSOSync.Persistence
└── Migrations/
    └── M035_LockExpiry.cs  ← adds lock_expiry column to sync_lock
```

### Dependency graph

```
MSOSync.Common          (no new deps)
        ↑
MSOSync.Persistence     (EF Core 9 — SqlDistributedLockService)
        ↑
MSOSync.Infrastructure  (StackExchange.Redis — RedisDistributedLockService, optional)
        ↑
MSOSync.Api / MSOSync.Scheduler
```

`MSOSync.Common` has no dependency on EF or Redis — it holds only the interfaces and options.

---

## IDistributedLockService Interface

Defined in `MSOSync.Common/Locks/IDistributedLockService.cs`.

```csharp
namespace MSOSync.Common.Locks;

/// <summary>
/// Provider-agnostic distributed lock service.
/// Lock acquisition is non-blocking: TryAcquireAsync returns null if the lock
/// cannot be taken immediately. Callers are responsible for retry if desired.
/// </summary>
public interface IDistributedLockService
{
    /// <summary>
    /// Attempt to acquire the named lock. Returns null if the lock is held by
    /// another owner. The returned handle must be disposed to release the lock.
    /// </summary>
    Task<IDistributedLock?> TryAcquireAsync(
        string            resource,
        string            owner,
        TimeSpan          expiry,
        CancellationToken ct = default);

    /// <summary>
    /// Extend the expiry of an existing lock held by <paramref name="owner"/>.
    /// Returns false if the lock is not currently held by that owner.
    /// </summary>
    Task<bool> RenewAsync(
        string            resource,
        string            owner,
        TimeSpan          expiry,
        CancellationToken ct = default);

    /// <summary>
    /// Release the lock unconditionally. No-op if the lock is not held by owner.
    /// </summary>
    Task ReleaseAsync(
        string            resource,
        string            owner,
        CancellationToken ct = default);

    /// <summary>
    /// Returns true if the named lock is currently held by any owner and has
    /// not expired. Used by diagnostic/admin endpoints only.
    /// </summary>
    Task<bool> IsHeldAsync(
        string            resource,
        CancellationToken ct = default);
}
```

Defined in `MSOSync.Common/Locks/IDistributedLock.cs`.

```csharp
namespace MSOSync.Common.Locks;

/// <summary>
/// Handle to an acquired distributed lock. Dispose to release.
/// </summary>
public interface IDistributedLock : IAsyncDisposable
{
    string         Resource  { get; }
    string         Owner     { get; }
    DateTimeOffset ExpiresAt { get; }
}
```

### Design decisions

- `TryAcquireAsync` returns `null` (not throws) when the lock is not available. This matches the existing `IDatabaseLockProvider` contract and keeps callers simple.
- `owner` is a caller-supplied string. Workers pass `$"{Environment.MachineName}:{Environment.ProcessId}"` as they do today. API callers may pass the same or a request-scoped identifier.
- `expiry` is explicit on `TryAcquireAsync` and `RenewAsync`, giving callers per-lock TTL control. `DistributedLockOptions.DefaultExpiry` is used when callers do not want to specify (see §DI Wiring helpers).
- `IDistributedLock` implements `IAsyncDisposable` so existing `await using var lease = ...` call sites need only type-swap.
- There is no built-in retry loop inside `TryAcquireAsync`. Callers that need retry are responsible for it; `DistributedLockOptions.RetryCount` and `RetryDelay` are provided for the convenience helper (see §DI Wiring helpers).

---

## DistributedLockOptions

Defined in `MSOSync.Common/Locks/DistributedLockOptions.cs`.

```csharp
namespace MSOSync.Common.Locks;

public sealed class DistributedLockOptions
{
    public const string SectionName = "DistributedLocks";

    /// <summary>"Sql" or "Redis". Defaults to "Sql".</summary>
    public string   Provider     { get; set; } = "Sql";

    /// <summary>Default TTL when callers use the convenience helpers.</summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of retry attempts for the optional retry helper (not used by
    /// TryAcquireAsync itself). Default 3.
    /// </summary>
    public int      RetryCount   { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts. Default 200 ms.
    /// </summary>
    public TimeSpan RetryDelay   { get; set; } = TimeSpan.FromMilliseconds(200);
}
```

`LockProviderType.cs` (same namespace):

```csharp
namespace MSOSync.Common.Locks;

public enum LockProviderType { Sql, Redis }
```

### appsettings.json section

```json
"DistributedLocks": {
  "Provider": "Sql",
  "DefaultExpiry": "00:00:30",
  "RetryCount": 3,
  "RetryDelay": "00:00:00.200"
}
```

---

## Migration M035 — LockExpiry Column

**File:** `src/MSOSync.Persistence/Migrations/M035_LockExpiry.cs`

The existing `sync_lock` table has `lock_name`, `lock_owner`, `lock_time`, and `lock_scope`. There is no expiry column. The current `DatabaseLockProvider` hard-codes stale detection as `lock_time < DATEADD(MINUTE, -10, GETUTCDATE())`.

M035 adds a `lock_expiry` column so the SQL provider can store and query the caller-specified TTL.

```csharp
[Migration("20260723000035_LockExpiry")]
public partial class M035_LockExpiry : Migration
{
    private const string Schema = "msosync";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(
            name:      "lock_expiry",
            schema:    Schema,
            table:     "sync_lock",
            type:      "datetime2(7)",
            nullable:  true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name:   "lock_expiry",
            schema: Schema,
            table:  "sync_lock");
    }
}
```

**Entity update** — `SyncLock.cs` gains one property:

```csharp
public DateTime? LockExpiry { get; set; }
```

**Configuration update** — `SyncLockConfiguration.cs` maps it:

```csharp
builder.Property(e => e.LockExpiry)
    .HasColumnName("lock_expiry")
    .HasColumnType("datetime2(7)");
```

No other migration changes. All existing rows default to `NULL` which the SQL provider treats as "no expiry set — use legacy 10-minute stale threshold" during the transition window.

---

## SqlDistributedLockService

**File:** `src/MSOSync.Persistence/Lock/SqlDistributedLockService.cs`

Replaces `DatabaseLockProvider`. Registered as `IDistributedLockService` (scoped).

### Acquisition — TryAcquireAsync

```
UPDATE [msosync].[sync_lock]
SET    lock_owner  = @owner,
       lock_time   = GETUTCDATE(),
       lock_expiry = DATEADD(ms, @expiryMs, GETUTCDATE())
WHERE  lock_name   = @resource
  AND  (lock_owner IS NULL
     OR lock_expiry IS NULL AND lock_time  < DATEADD(MINUTE, -10, GETUTCDATE())
     OR lock_expiry IS NOT NULL AND lock_expiry < GETUTCDATE())
```

- Returns affected row count. If 1, lock acquired → create `SqlDistributedLock` handle.
- If 0, lock held → return `null`.
- `@expiryMs` is `(long)expiry.TotalMilliseconds`.

### Renewal — RenewAsync

```
UPDATE [msosync].[sync_lock]
SET    lock_expiry = DATEADD(ms, @expiryMs, GETUTCDATE())
WHERE  lock_name  = @resource
  AND  lock_owner = @owner
```

Returns `rows == 1`.

### Release — ReleaseAsync

```
UPDATE [msosync].[sync_lock]
SET    lock_owner  = NULL,
       lock_time   = NULL,
       lock_expiry = NULL
WHERE  lock_name  = @resource
  AND  lock_owner = @owner
```

### IsHeldAsync

```
SELECT COUNT(1)
FROM   [msosync].[sync_lock]
WHERE  lock_name   = @resource
  AND  lock_owner IS NOT NULL
  AND  (lock_expiry IS NULL OR lock_expiry > GETUTCDATE())
```

Returns `count > 0`.

### C# class skeleton

```csharp
namespace MSOSync.Persistence.Lock;

public sealed class SqlDistributedLockService(AppDbContext db) : IDistributedLockService
{
    private static readonly string Schema =
        Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var expiryMs = (long)expiry.TotalMilliseconds;
        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE(), " +
            "    lock_expiry = DATEADD(ms, {1}, GETUTCDATE()) " +
            "WHERE lock_name = {2} " +
            "  AND (lock_owner IS NULL " +
            "    OR (lock_expiry IS NULL AND lock_time < DATEADD(MINUTE, -10, GETUTCDATE())) " +
            "    OR (lock_expiry IS NOT NULL AND lock_expiry < GETUTCDATE()))",
            new object[] { owner, expiryMs, resource }, ct);

        if (rows != 1) return null;

        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        return new SqlDistributedLock(this, resource, owner, expiresAt);
    }

    public async Task<bool> RenewAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var expiryMs = (long)expiry.TotalMilliseconds;
        var rows = await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_expiry = DATEADD(ms, {0}, GETUTCDATE()) " +
            "WHERE lock_name = {1} AND lock_owner = {2}",
            new object[] { expiryMs, resource, owner }, ct);
        return rows == 1;
    }

    public async Task ReleaseAsync(
        string resource, string owner, CancellationToken ct = default)
    {
        await db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{Schema}].[sync_lock] " +
            "SET lock_owner = NULL, lock_time = NULL, lock_expiry = NULL " +
            "WHERE lock_name = {0} AND lock_owner = {1}",
            new object[] { resource, owner }, ct);
    }

    public async Task<bool> IsHeldAsync(
        string resource, CancellationToken ct = default)
    {
        var locks = await db.Locks
            .AsNoTracking()
            .Where(l => l.LockName == resource
                     && l.LockOwner != null
                     && (l.LockExpiry == null || l.LockExpiry > DateTime.UtcNow))
            .CountAsync(ct);
        return locks > 0;
    }
}
```

**SqlDistributedLock** (`SqlDistributedLock.cs`) wraps the service for dispose:

```csharp
namespace MSOSync.Persistence.Lock;

internal sealed class SqlDistributedLock(
    SqlDistributedLockService service,
    string resource,
    string owner,
    DateTimeOffset expiresAt) : IDistributedLock
{
    private bool _disposed;

    public string         Resource  { get; } = resource;
    public string         Owner     { get; } = owner;
    public DateTimeOffset ExpiresAt { get; } = expiresAt;

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await service.ReleaseAsync(Resource, Owner, CancellationToken.None);
    }
}
```

### SQL concurrency guarantee

The single `UPDATE ... WHERE lock_owner IS NULL OR lock_expiry < GETUTCDATE()` is an atomic compare-and-swap at the SQL Server row level. No additional ROWLOCK hints or explicit transactions are needed — SQL Server's row-level locking handles concurrent UPDATE contention. This is the same pattern used by the current `DatabaseLockProvider`.

---

## RedisDistributedLockService

**File:** `src/MSOSync.Infrastructure/Lock/RedisDistributedLockService.cs`
(or `MSOSync.Persistence` if no Infrastructure project exists — place in whichever project already references StackExchange.Redis from Phase 2D.1)

**Only registered when `DistributedLockOptions.Provider == "Redis"`.**

### Redlock algorithm (single-node simplified)

For MSOSync's initial Redis integration, a single-node Redis SET NX PX pattern is used. Full multi-node Redlock is out of scope for 2D.2 (requires a Redis cluster, which is a 2D.3+ concern). The implementation is designed so that it can be upgraded to multi-node Redlock without changing `IDistributedLockService`.

### TryAcquireAsync

```
SET resource owner NX PX expiryMs
```

- Returns `"OK"` on success, `null` if lock held.
- `owner` is stored as value so only the owner can release.

### RenewAsync

Lua script to extend expiry only if caller is owner:

```lua
if redis.call("GET", KEYS[1]) == ARGV[1] then
    return redis.call("PEXPIRE", KEYS[1], ARGV[2])
else
    return 0
end
```

Returns `true` if Lua returns `1`.

### ReleaseAsync

Lua script (canonical Redlock release):

```lua
if redis.call("GET", KEYS[1]) == ARGV[1] then
    return redis.call("DEL", KEYS[1])
else
    return 0
end
```

### IsHeldAsync

```
EXISTS resource
```

Returns `true` if key exists (does not check owner value — diagnostic use only).

### C# class skeleton

```csharp
namespace MSOSync.Infrastructure.Lock;

public sealed class RedisDistributedLockService(
    IConnectionMultiplexer redis) : IDistributedLockService
{
    private static readonly string RenewScript = @"
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('PEXPIRE', KEYS[1], ARGV[2])
        else
            return 0
        end";

    private static readonly string ReleaseScript = @"
        if redis.call('GET', KEYS[1]) == ARGV[1] then
            return redis.call('DEL', KEYS[1])
        else
            return 0
        end";

    public async Task<IDistributedLock?> TryAcquireAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var db      = redis.GetDatabase();
        var expiryMs = (long)expiry.TotalMilliseconds;
        var ok      = await db.StringSetAsync(resource, owner,
            TimeSpan.FromMilliseconds(expiryMs), When.NotExists);

        if (!ok) return null;

        var expiresAt = DateTimeOffset.UtcNow.Add(expiry);
        return new RedisDistributedLock(this, resource, owner, expiresAt);
    }

    public async Task<bool> RenewAsync(
        string resource, string owner, TimeSpan expiry, CancellationToken ct = default)
    {
        var db      = redis.GetDatabase();
        var result  = (long)await db.ScriptEvaluateAsync(
            RenewScript,
            new RedisKey[] { resource },
            new RedisValue[] { owner, (long)expiry.TotalMilliseconds });
        return result == 1;
    }

    public async Task ReleaseAsync(
        string resource, string owner, CancellationToken ct = default)
    {
        var db = redis.GetDatabase();
        await db.ScriptEvaluateAsync(
            ReleaseScript,
            new RedisKey[] { resource },
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

`RedisDistributedLock` mirrors `SqlDistributedLock` — holds resource/owner/expiresAt, calls `ReleaseAsync` on dispose.

---

## Migration Plan — Existing Callers

Four callers use `IDatabaseLockProvider` directly. All are updated to `IDistributedLockService`.

### Change pattern

Old (all callers):
```csharp
using MSOSync.Persistence.Lock;
// ...
IDatabaseLockProvider lockProvider
// ...
await using var lease = await lockProvider.TryAcquireAsync(LockNames.SyncEngine, ct);
if (lease == null) { /* skip */ return; }
```

New (all callers):
```csharp
using MSOSync.Common.Locks;
// ...
IDistributedLockService lockService,
IOptions<DistributedLockOptions> lockOptions
// ...
var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
await using var handle = await lockService.TryAcquireAsync(
    LockNames.SyncEngine, owner, lockOptions.Value.DefaultExpiry, ct);
if (handle == null) { /* skip */ return; }
```

### Caller inventory

| File | Current injection | Lock name used | Change |
|---|---|---|---|
| `MSOSync.Scheduler/SyncJob.cs` | `IDatabaseLockProvider` (via scope) | `LockNames.SyncEngine` | Swap to `IDistributedLockService` |
| `MSOSync.Scheduler/RetryJob.cs` | `IDatabaseLockProvider` (via scope) | `LockNames.RetryEngine` | Swap to `IDistributedLockService` |
| `MSOSync.Scheduler/PurgeJob.cs` | `IDatabaseLockProvider` (via scope) | `LockNames.PurgeEngine` | Swap to `IDistributedLockService` |
| `MSOSync.Api/Controllers/BatchController.cs` | `IDatabaseLockProvider` (constructor) | `LockNames.RetryEngine` | Swap to `IDistributedLockService` |

`LockNames.cs` is unchanged — the three constants stay as the resource identifiers.

### LockAdminService

`MSOSync.Metadata/Locks/LockAdminService.cs` reads `db.Locks` directly for the admin list endpoint and deletes by removing the entity. This is read/admin access to the lock table, not lock acquisition. It does **not** use `IDatabaseLockProvider` and does not need to migrate to `IDistributedLockService`.

`LockDto` gains an `ExpiresAt` field to expose the new column:

```csharp
public sealed record LockDto(
    string    LockName,
    string?   LockOwner,
    DateTime? LockTime,
    DateTime? LockExpiry);   // ← new
```

`LockAdminService.GetLocksAsync` adds `.LockExpiry` to the projection.

### ClusterDiagnosticsQueryService

Reads `db.Set<SyncLock>()` for diagnostics — read-only, no acquisition. No change required beyond adding `LockExpiry` to the `ActiveLockDto` if the diagnostics consumer wants to show TTL. This is optional and can be done in the diagnostics spec if needed.

---

## DI Wiring

**File:** `src/MSOSync.Persistence/Lock/DistributedLockServiceExtensions.cs`

```csharp
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
            // RedisDistributedLockService is in MSOSync.Infrastructure.
            // Register via assembly-level extension if cross-project reference is undesirable,
            // or inline here if Infrastructure references Persistence.
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

**Registration site** — `PersistenceServiceExtensions.AddPersistence` replaces:

```csharp
// BEFORE
services.AddScoped<IDatabaseLockProvider, DatabaseLockProvider>();

// AFTER
services.AddDistributedLocks(configuration);
```

`IDatabaseLockProvider`, `DatabaseLockProvider`, and `DatabaseLockLease` are deleted from the codebase once all callers are migrated.

### Convenience helper (optional, in MSOSync.Common)

A static helper class `DistributedLockHelper` can provide retry logic for callers that need it:

```csharp
namespace MSOSync.Common.Locks;

public static class DistributedLockHelper
{
    /// <summary>
    /// Attempt to acquire with retry. Returns null if all attempts fail.
    /// </summary>
    public static async Task<IDistributedLock?> TryAcquireWithRetryAsync(
        this IDistributedLockService service,
        string                       resource,
        string                       owner,
        DistributedLockOptions       options,
        CancellationToken            ct = default)
    {
        for (var attempt = 0; attempt <= options.RetryCount; attempt++)
        {
            var handle = await service.TryAcquireAsync(
                resource, owner, options.DefaultExpiry, ct);
            if (handle is not null) return handle;

            if (attempt < options.RetryCount)
                await Task.Delay(options.RetryDelay, ct);
        }
        return null;
    }
}
```

Current callers (`SyncJob`, `RetryJob`, `PurgeJob`) do **not** retry — they skip the tick if the lock is held. `TryAcquireAsync` directly (no retry helper) is the correct pattern for them.

---

## Testing

### Unit tests — SqlDistributedLockService

**Project:** `tests/MSOSync.Persistence.Tests` (or `tests/MSOSync.Tests.Unit`)

Tests use `AppDbContext` with EF Core `UseInMemoryDatabase` or a SQLite provider for isolation where raw SQL compatibility is not required, and LocalDB for the integration tests below.

| Test | Description |
|---|---|
| `TryAcquireAsync_ReturnsHandle_WhenLockFree` | Row with `lock_owner = NULL` → returns non-null handle |
| `TryAcquireAsync_ReturnsNull_WhenLockHeld` | Row with valid unexpired owner → returns null |
| `TryAcquireAsync_Acquires_WhenLockExpired` | Row with `lock_expiry` in past → returns handle (stale steal) |
| `TryAcquireAsync_Acquires_WhenLegacyStaleLock` | Row with `lock_expiry = NULL` and `lock_time` 11 min ago → returns handle |
| `RenewAsync_ReturnsTrue_WhenOwnerMatches` | Extends expiry for matching owner |
| `RenewAsync_ReturnsFalse_WhenOwnerMismatch` | Different owner → false |
| `ReleaseAsync_ClearsRow_WhenOwnerMatches` | After release, `lock_owner` is NULL |
| `ReleaseAsync_NoOp_WhenOwnerMismatch` | Different owner → row unchanged |
| `IsHeldAsync_ReturnsTrue_WhenActiveOwner` | Unexpired lock → true |
| `IsHeldAsync_ReturnsFalse_WhenExpired` | Expired lock → false |
| `DisposeAsync_ReleasesLock` | Dispose on handle calls ReleaseAsync |

**Note:** The raw SQL UPDATE statements in `SqlDistributedLockService` cannot run against EF InMemory. Use SQL Server LocalDB for all SQL-level tests.

### Unit tests — RedisDistributedLockService

**Project:** `tests/MSOSync.Infrastructure.Tests` (or `tests/MSOSync.Tests.Unit`)

Use `Moq` to mock `IConnectionMultiplexer` and `IDatabase`.

| Test | Description |
|---|---|
| `TryAcquireAsync_ReturnsHandle_WhenSetNxSucceeds` | `StringSetAsync` returns true → handle returned |
| `TryAcquireAsync_ReturnsNull_WhenSetNxFails` | `StringSetAsync` returns false → null |
| `RenewAsync_ReturnsTrue_WhenLuaReturns1` | Lua evaluate returns 1 → true |
| `RenewAsync_ReturnsFalse_WhenLuaReturns0` | Lua evaluate returns 0 → false |
| `ReleaseAsync_InvokesLuaScript` | Verify script evaluated with correct keys/args |
| `IsHeldAsync_ReturnsTrue_WhenKeyExists` | `KeyExistsAsync` returns true |
| `DisposeAsync_CallsReleaseAsync` | Dispose invokes release Lua script |

### Integration test — real SQL (LocalDB)

**Project:** `tests/MSOSync.Persistence.IntegrationTests`

```csharp
[Collection("Database")]
public sealed class SqlDistributedLockIntegrationTests : IAsyncLifetime
{
    // Spin up real LocalDB AppDbContext with M035 migration applied.
    // Seed: INSERT INTO sync_lock (lock_name, lock_scope)
    //       VALUES ('TEST_LOCK', 0)
    // so the row exists.

    [Fact]
    public async Task TwoCallers_OnlyOneAcquires()
    {
        // Arrange: two SqlDistributedLockService instances sharing same DB
        // Act: both call TryAcquireAsync concurrently
        // Assert: exactly one returns non-null
    }

    [Fact]
    public async Task ExpiredLock_StolenBySecondCaller()
    {
        // Arrange: acquire with 100ms expiry, wait 200ms
        // Act: second caller acquires
        // Assert: second caller gets the lock
    }

    [Fact]
    public async Task Dispose_ReleasesLock_AllowingReacquisition()
    {
        // Acquire, dispose, re-acquire same resource same owner
    }
}
```

### Worker smoke tests — migration correctness

`SyncJob`, `RetryJob`, `PurgeJob` already have `RunTickAsync` / `RunPurgeAsync` unit tests. After migration, existing tests continue to pass — the `IDatabaseLockProvider` mock is replaced with an `IDistributedLockService` mock returning `null` or a mock `IDistributedLock`. No new test files needed for the scheduler layer.

---

## Global Constraints

1. **No new migrations beyond M035.** M035 adds only `lock_expiry datetime2(7) NULL` to `sync_lock`. No other tables change.
2. **Backward compatibility during migration window.** Rows with `lock_expiry = NULL` are still handled by the legacy 10-minute stale threshold in the SQL WHERE clause. This means mixed deployments (old code writes, new code reads) are safe.
3. **`IDistributedLockService` lives in `MSOSync.Common`.** No EF or Redis types leak into `MSOSync.Common`.
4. **Redis provider is optional.** The codebase compiles and runs with `Provider=Sql` and no StackExchange.Redis package. Redis types are only referenced in the Infrastructure project.
5. **Non-blocking acquisition.** `TryAcquireAsync` never spins or delays internally. It executes one atomic operation and returns.
6. **Owner string format preserved.** All callers continue to produce `$"{Environment.MachineName}:{Environment.ProcessId}"`. The interface does not enforce this format — it is a convention.
7. **`LockNames.cs` unchanged.** The three string constants (`SYNC_ENGINE`, `RETRY_ENGINE`, `PURGE_ENGINE`) are the resource identifiers for both providers.
8. **`LockAdminService.DeleteLockAsync` is unaffected.** Admin deletion directly manipulates the EF entity. It does not go through `IDistributedLockService.ReleaseAsync` because it is an administrative override, not a normal release.
9. **All existing lock semantics preserved.** The SQL provider's compare-and-swap behaviour is semantically identical to the existing `DatabaseLockProvider` with added expiry precision.
10. **xUnit only.** No MSTest or NUnit. All test classes follow the existing `[Fact]` / `[Collection("Database")]` pattern in the solution.
