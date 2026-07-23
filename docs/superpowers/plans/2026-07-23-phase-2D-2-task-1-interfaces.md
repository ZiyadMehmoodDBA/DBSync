# Task 1: IDistributedLockService + Interfaces + Options + Helper

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Create the public lock abstraction in `MSOSync.Common` — four files, zero EF/Redis deps.

**Files:**
- Create: `src/MSOSync.Common/Locks/IDistributedLock.cs`
- Create: `src/MSOSync.Common/Locks/IDistributedLockService.cs`
- Create: `src/MSOSync.Common/Locks/DistributedLockOptions.cs`
- Create: `src/MSOSync.Common/Locks/LockProviderType.cs`
- Create: `src/MSOSync.Common/Locks/DistributedLockHelper.cs`

**Interfaces:**
- Produces (consumed by Tasks 2, 3, 4):
  - `IDistributedLock` — `Resource: string`, `Owner: string`, `ExpiresAt: DateTimeOffset`, `DisposeAsync(): ValueTask`
  - `IDistributedLockService` — `TryAcquireAsync(resource, owner, expiry, ct): Task<IDistributedLock?>`, `RenewAsync(resource, owner, expiry, ct): Task<bool>`, `ReleaseAsync(resource, owner, ct): Task`, `IsHeldAsync(resource, ct): Task<bool>`
  - `DistributedLockOptions` — `Provider: string`, `DefaultExpiry: TimeSpan`, `RetryCount: int`, `RetryDelay: TimeSpan`, `SectionName: const string = "DistributedLocks"`
  - `LockProviderType` — `Sql = 0, Redis = 1`
  - `DistributedLockHelper.TryAcquireWithRetryAsync(this IDistributedLockService, string resource, string owner, DistributedLockOptions options, CancellationToken ct): Task<IDistributedLock?>`

---

- [ ] **Step 1: Create the `Locks/` folder by writing `IDistributedLock.cs`**

Create `src/MSOSync.Common/Locks/IDistributedLock.cs`:

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

- [ ] **Step 2: Write `IDistributedLockService.cs`**

Create `src/MSOSync.Common/Locks/IDistributedLockService.cs`:

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
    /// Release the lock. No-op if the lock is not held by owner.
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

- [ ] **Step 3: Write `LockProviderType.cs`**

Create `src/MSOSync.Common/Locks/LockProviderType.cs`:

```csharp
namespace MSOSync.Common.Locks;

public enum LockProviderType { Sql, Redis }
```

- [ ] **Step 4: Write `DistributedLockOptions.cs`**

Create `src/MSOSync.Common/Locks/DistributedLockOptions.cs`:

```csharp
namespace MSOSync.Common.Locks;

public sealed class DistributedLockOptions
{
    public const string SectionName = "DistributedLocks";

    /// <summary>"Sql" or "Redis". Defaults to "Sql".</summary>
    public string   Provider      { get; set; } = "Sql";

    /// <summary>Default TTL when callers use the convenience helpers.</summary>
    public TimeSpan DefaultExpiry { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Number of retry attempts for TryAcquireWithRetryAsync (not used by
    /// TryAcquireAsync itself). Default 3.
    /// </summary>
    public int      RetryCount    { get; set; } = 3;

    /// <summary>
    /// Delay between retry attempts. Default 200 ms.
    /// </summary>
    public TimeSpan RetryDelay    { get; set; } = TimeSpan.FromMilliseconds(200);
}
```

- [ ] **Step 5: Write `DistributedLockHelper.cs`**

Create `src/MSOSync.Common/Locks/DistributedLockHelper.cs`:

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

- [ ] **Step 6: Verify build**

Run from solution root:

```
dotnet build src/MSOSync.Common/MSOSync.Common.csproj
```

Expected output: `Build succeeded.` with 0 errors.

- [ ] **Step 7: Write unit tests for DistributedLockHelper**

In `tests/MSOSync.Tests/Lock/DistributedLockHelperTests.cs` (create the `Lock/` subfolder):

```csharp
using FluentAssertions;
using Moq;
using MSOSync.Common.Locks;
using Xunit;

namespace MSOSync.Tests.Lock;

public sealed class DistributedLockHelperTests
{
    private readonly Mock<IDistributedLockService> _service = new();
    private readonly Mock<IDistributedLock>        _handle  = new();

    private static DistributedLockOptions Options(int retryCount = 2) => new()
    {
        DefaultExpiry = TimeSpan.FromSeconds(10),
        RetryCount    = retryCount,
        RetryDelay    = TimeSpan.Zero   // no delay in tests
    };

    [Fact]
    public async Task Returns_handle_on_first_attempt()
    {
        _service.Setup(s => s.TryAcquireAsync(
                "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(_handle.Object);

        var result = await _service.Object.TryAcquireWithRetryAsync(
            "RES", "OWNER", Options(), CancellationToken.None);

        result.Should().NotBeNull();
        _service.Verify(s => s.TryAcquireAsync(
            "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Returns_handle_on_second_attempt()
    {
        var callCount = 0;
        _service.Setup(s => s.TryAcquireAsync(
                "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ++callCount == 2 ? _handle.Object : null);

        var result = await _service.Object.TryAcquireWithRetryAsync(
            "RES", "OWNER", Options(retryCount: 2), CancellationToken.None);

        result.Should().NotBeNull();
        _service.Verify(s => s.TryAcquireAsync(
            "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task Returns_null_when_all_attempts_fail()
    {
        _service.Setup(s => s.TryAcquireAsync(
                "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IDistributedLock?)null);

        var result = await _service.Object.TryAcquireWithRetryAsync(
            "RES", "OWNER", Options(retryCount: 2), CancellationToken.None);

        result.Should().BeNull();
        // retryCount=2 means attempt 0, 1, 2 → 3 total calls
        _service.Verify(s => s.TryAcquireAsync(
            "RES", "OWNER", TimeSpan.FromSeconds(10), It.IsAny<CancellationToken>()),
            Times.Exactly(3));
    }
}
```

- [ ] **Step 8: Run the tests**

```
dotnet test tests/MSOSync.Tests/MSOSync.Tests.csproj --filter "FullyQualifiedName~DistributedLockHelperTests" -v minimal
```

Expected: `Passed: 3, Failed: 0`

- [ ] **Step 9: Commit**

```
git add src/MSOSync.Common/Locks/IDistributedLock.cs
git add src/MSOSync.Common/Locks/IDistributedLockService.cs
git add src/MSOSync.Common/Locks/DistributedLockOptions.cs
git add src/MSOSync.Common/Locks/LockProviderType.cs
git add src/MSOSync.Common/Locks/DistributedLockHelper.cs
git add tests/MSOSync.Tests/Lock/DistributedLockHelperTests.cs
git commit -m "feat(2D.2-T1): add IDistributedLockService interfaces and DistributedLockHelper"
```
