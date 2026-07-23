# Task 1 — ISchedulerLock + ISchedulerLockFactory + SchedulerJobGuard + Unit Tests

**Phase:** 2D.3
**File:** `2026-07-23-phase-2D-3-task-1-scheduler-lock.md`
**Depends on:** Nothing (foundational interfaces)

---

## Overview

Define and implement the distributed-lock abstractions used by every scheduler job:
- `ISchedulerLock` — represents a held lock for one job tick (supports heartbeat renewal via internal Task)
- `ISchedulerLockFactory` — tries to acquire a named lock, returns `null` on contention
- `SchedulerLockOptions` — POCO configuration (TTL, renewal interval, prefix)
- `SchedulerLockImpl` (internal) — concrete implementation; starts a renewal `Task` on construction
- `SchedulerLockFactory` (internal) — creates `SchedulerLockImpl` instances
- `SchedulerJobGuard` — static helper that wraps any job lambda with acquire / renew / release

This task does **not** modify any existing job classes. It only adds new files to `MSOSync.Scheduler`.

---

## Step 1 — Create `SchedulerLockOptions`

**File:** `src/MSOSync.Scheduler/SchedulerLockOptions.cs`

- [ ] Create the file:

```csharp
namespace MSOSync.Scheduler;

public sealed class SchedulerLockOptions
{
    public const string Section = "Scheduler:Lock";

    /// <summary>Lock TTL in seconds. Default 120. Must be >= 3x RenewalIntervalSeconds.</summary>
    public int TtlSeconds { get; init; } = 120;

    /// <summary>How often to renew the lock while a job is running (seconds). Default 10.</summary>
    public int RenewalIntervalSeconds { get; init; } = 10;

    /// <summary>Prefix prepended to every job lock name. Default "scheduler:".</summary>
    public string LockPrefix { get; init; } = "scheduler:";
}
```

---

## Step 2 — Create `ISchedulerLock`

**File:** `src/MSOSync.Scheduler/ISchedulerLock.cs`

- [ ] Create the file:

```csharp
namespace MSOSync.Scheduler;

/// <summary>
/// Represents an acquired distributed scheduler lock for one job tick.
/// Disposal releases the lock immediately and cancels the renewal loop.
/// </summary>
public interface ISchedulerLock : IAsyncDisposable
{
    /// <summary>Name of the job this lock was acquired for.</summary>
    string JobName { get; }

    /// <summary>UTC timestamp when the lock was acquired.</summary>
    DateTimeOffset AcquiredAt { get; }

    /// <summary>Identity of the instance holding the lock ("MachineName:PID").</summary>
    string Owner { get; }
}
```

---

## Step 3 — Create `ISchedulerLockFactory`

**File:** `src/MSOSync.Scheduler/ISchedulerLockFactory.cs`

- [ ] Create the file:

```csharp
namespace MSOSync.Scheduler;

/// <summary>
/// Creates distributed scheduler lock instances.
/// Returns null if another instance already holds the lock.
/// </summary>
public interface ISchedulerLockFactory
{
    /// <summary>
    /// Attempts to acquire "scheduler:{jobName}" lock.
    /// Returns a live <see cref="ISchedulerLock"/> (with renewal loop started) on success,
    /// or null if the lock is held by another instance.
    /// </summary>
    Task<ISchedulerLock?> TryAcquireAsync(string jobName, CancellationToken ct);
}
```

---

## Step 4 — Create `SchedulerLockImpl` (internal)

**File:** `src/MSOSync.Scheduler/Internal/SchedulerLockImpl.cs`

- [ ] Create directory `src/MSOSync.Scheduler/Internal/` (new folder).
- [ ] Create the file:

```csharp
using Microsoft.Extensions.Logging;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Concrete scheduler lock. Starts a heartbeat renewal Task immediately
/// after construction so the lock never goes stale while work is running.
/// </summary>
internal sealed class SchedulerLockImpl : ISchedulerLock
{
    private readonly IDatabaseLockProvider       _lockProvider;
    private readonly SchedulerLockOptions        _options;
    private readonly ILogger                     _logger;
    private readonly CancellationTokenSource     _renewalCts = new();
    private readonly Task                        _renewalTask;

    public string          JobName    { get; }
    public DateTimeOffset  AcquiredAt { get; } = DateTimeOffset.UtcNow;
    public string          Owner      { get; } =
        $"{Environment.MachineName}:{Environment.ProcessId}";

    internal SchedulerLockImpl(
        string                jobName,
        IDatabaseLockProvider lockProvider,
        SchedulerLockOptions  options,
        ILogger               logger)
    {
        JobName       = jobName;
        _lockProvider = lockProvider;
        _options      = options;
        _logger       = logger;
        _renewalTask  = RunRenewalLoopAsync(_renewalCts.Token);
    }

    private async Task RunRenewalLoopAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(_options.RenewalIntervalSeconds);

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, ct);
                var lockName = $"{_options.LockPrefix}{JobName}";
                await _lockProvider.RenewAsync(lockName, Owner, ct);
                _logger.LogDebug(
                    "SchedulerLock: renewed {JobName} (owner={Owner})",
                    JobName, Owner);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // Non-fatal: log warning; renewal retries next interval.
                // If renewals fail until TTL expires the lock will be stolen — safe fallback.
                _logger.LogWarning(ex,
                    "SchedulerLock: renewal failed for {JobName} — lock may expire if this persists",
                    JobName);
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // 1. Stop the renewal loop.
        await _renewalCts.CancelAsync();
        try { await _renewalTask.ConfigureAwait(false); }
        catch { /* swallow OperationCanceledException */ }
        _renewalCts.Dispose();

        // 2. Release the lock row immediately (do not pass a cancellation token —
        //    release must complete even during application shutdown).
        var lockName = $"{_options.LockPrefix}{JobName}";
        await _lockProvider.ReleaseAsync(lockName, Owner).ConfigureAwait(false);
    }
}
```

---

## Step 5 — Create `SchedulerLockFactory` (internal)

**File:** `src/MSOSync.Scheduler/Internal/SchedulerLockFactory.cs`

- [ ] Create the file:

```csharp
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MSOSync.Persistence.Lock;

namespace MSOSync.Scheduler.Internal;

/// <summary>
/// Acquires per-job scheduler locks against the SQL <see cref="IDatabaseLockProvider"/>.
/// The lock name used is "{LockPrefix}{jobName}" (e.g., "scheduler:SyncJob").
/// </summary>
internal sealed class SchedulerLockFactory(
    IDatabaseLockProvider        lockProvider,
    IOptions<SchedulerLockOptions> options,
    ILogger<SchedulerLockFactory>  logger) : ISchedulerLockFactory
{
    private readonly SchedulerLockOptions _options = options.Value;

    public async Task<ISchedulerLock?> TryAcquireAsync(string jobName, CancellationToken ct)
    {
        var lockName = $"{_options.LockPrefix}{jobName}";
        var lease    = await lockProvider.TryAcquireAsync(lockName, ct);

        if (lease is null)
        {
            logger.LogDebug(
                "SchedulerLockFactory: lock '{LockName}' is held — skipping",
                lockName);
            return null;
        }

        // Dispose the raw lease immediately — SchedulerLockImpl owns lifecycle from here.
        // (TryAcquireAsync sets the DB row; SchedulerLockImpl handles renewal and release.)
        await lease.DisposeAsync();

        var owner = $"{Environment.MachineName}:{Environment.ProcessId}";
        logger.LogDebug(
            "SchedulerLockFactory: acquired '{LockName}' (owner={Owner})",
            lockName, owner);

        return new SchedulerLockImpl(jobName, lockProvider, _options, logger);
    }
}
```

---

## Step 6 — Create `SchedulerJobGuard`

**File:** `src/MSOSync.Scheduler/SchedulerJobGuard.cs`

This is a static utility — no instance state. Each job calls it in its tick loop.

- [ ] Create the file:

```csharp
using Microsoft.Extensions.Logging;

namespace MSOSync.Scheduler;

/// <summary>
/// Runs <paramref name="work"/> under a distributed scheduler lock for <paramref name="jobName"/>.
/// If the lock cannot be acquired (another instance holds it), logs at Debug and returns immediately.
/// Health state transitions: Running → (work executes) → Idle on the active instance;
/// Standby on instances that lose the lock acquisition race.
/// </summary>
public static class SchedulerJobGuard
{
    /// <summary>
    /// Tries to acquire the distributed scheduler lock for <paramref name="jobName"/>,
    /// runs <paramref name="work"/> if successful, then releases the lock.
    /// Standby instances skip <paramref name="work"/> entirely with no side effects.
    /// </summary>
    /// <param name="jobName">Job name — used as the lock key suffix (e.g., "SyncJob").</param>
    /// <param name="lockFactory">Factory that acquires the distributed lock.</param>
    /// <param name="health">Health reporter — records Running, Standby, and Idle transitions.</param>
    /// <param name="logger">Logger for debug/warning messages.</param>
    /// <param name="work">The job body. Receives the outer cancellation token.</param>
    /// <param name="ct">Cancellation token from the outer BackgroundService loop.</param>
    public static async Task RunAsync(
        string                        jobName,
        ISchedulerLockFactory         lockFactory,
        ISchedulerHealthReporter      health,
        ILogger                       logger,
        Func<CancellationToken, Task> work,
        CancellationToken             ct)
    {
        await using var schedulerLock = await lockFactory.TryAcquireAsync(jobName, ct);

        if (schedulerLock is null)
        {
            logger.LogDebug("{Job}: lock held by another instance — skipping tick", jobName);
            health.RecordStandby(jobName);
            return;
        }

        health.RecordRunning(jobName, schedulerLock.Owner, schedulerLock.AcquiredAt);
        logger.LogDebug(
            "{Job}: acquired scheduler lock (owner={Owner})", jobName, schedulerLock.Owner);

        try
        {
            await work(ct);
        }
        finally
        {
            health.RecordIdle(jobName);
            // IAsyncDisposable on schedulerLock cancels renewal and releases the DB row here.
        }
    }
}
```

---

## Step 7 — Unit Tests: `SchedulerLockFactoryTests`

**File:** `tests/MSOSync.SchedulerTests/SchedulerLockFactoryTests.cs`

- [ ] Create the file. Tests use Moq for `IDatabaseLockProvider` (no real DB needed).

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using MSOSync.Scheduler.Internal;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerLockFactoryTests
{
    private readonly Mock<IDatabaseLockProvider> _lockProvider = new();

    private ISchedulerLockFactory BuildFactory(int ttlSeconds = 120, int renewalSeconds = 10)
    {
        var options = Options.Create(new SchedulerLockOptions
        {
            TtlSeconds             = ttlSeconds,
            RenewalIntervalSeconds = renewalSeconds,
            LockPrefix             = "scheduler:"
        });
        return new SchedulerLockFactory(
            _lockProvider.Object,
            options,
            NullLogger<SchedulerLockFactory>.Instance);
    }

    [Fact]
    public async Task TryAcquireAsync_Returns_Null_When_Lock_Held()
    {
        _lockProvider
            .Setup(x => x.TryAcquireAsync("scheduler:SyncJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        var result = await BuildFactory().TryAcquireAsync("SyncJob", CancellationToken.None);

        result.Should().BeNull();
    }

    [Fact]
    public async Task TryAcquireAsync_Returns_Lock_When_Row_Free()
    {
        _lockProvider
            .Setup(x => x.TryAcquireAsync("scheduler:SyncJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<IAsyncDisposable>());
        // ReleaseAsync called by DisposeAsync (via factory internals), allow it
        _lockProvider
            .Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await using var result = await BuildFactory().TryAcquireAsync("SyncJob", CancellationToken.None);

        result.Should().NotBeNull();
        result!.JobName.Should().Be("SyncJob");
        result.Owner.Should().MatchRegex(@"^.+:\d+$"); // MachineName:PID format
        result.AcquiredAt.Should().BeCloseTo(DateTimeOffset.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task TryAcquireAsync_Prefixes_LockName_With_Scheduler_Colon()
    {
        _lockProvider
            .Setup(x => x.TryAcquireAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IAsyncDisposable?)null);

        await BuildFactory().TryAcquireAsync("PullJob", CancellationToken.None);

        _lockProvider.Verify(
            x => x.TryAcquireAsync("scheduler:PullJob", It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

---

## Step 8 — Unit Tests: `SchedulerLockImplTests`

**File:** `tests/MSOSync.SchedulerTests/SchedulerLockImplTests.cs`

- [ ] Create the file. Tests verify the renewal loop and disposal contract.

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using MSOSync.Scheduler.Internal;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerLockImplTests
{
    private readonly Mock<IDatabaseLockProvider> _lockProvider = new();

    private SchedulerLockImpl BuildLock(int renewalSeconds = 1)
    {
        var options = new SchedulerLockOptions
        {
            TtlSeconds             = renewalSeconds * 4,
            RenewalIntervalSeconds = renewalSeconds,
            LockPrefix             = "scheduler:"
        };

        _lockProvider
            .Setup(x => x.RenewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _lockProvider
            .Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        return new SchedulerLockImpl("SyncJob", _lockProvider.Object, options,
            NullLogger<SchedulerLockImpl>.Instance);
    }

    [Fact]
    public async Task Renewal_Calls_RenewAsync_At_Interval()
    {
        await using var lockImpl = BuildLock(renewalSeconds: 1);

        // Wait long enough for at least 2 renewals
        await Task.Delay(TimeSpan.FromSeconds(2.5));

        _lockProvider.Verify(
            x => x.RenewAsync("scheduler:SyncJob", lockImpl.Owner, It.IsAny<CancellationToken>()),
            Times.AtLeast(2));
    }

    [Fact]
    public async Task DisposeAsync_Cancels_Renewal_And_Calls_ReleaseAsync_Once()
    {
        var lockImpl = BuildLock(renewalSeconds: 60); // long interval so renewal doesn't fire

        await lockImpl.DisposeAsync();

        _lockProvider.Verify(
            x => x.ReleaseAsync("scheduler:SyncJob", lockImpl.Owner),
            Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_Does_Not_Throw_When_Release_Fails()
    {
        _lockProvider
            .Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ThrowsAsync(new InvalidOperationException("db gone"));
        _lockProvider
            .Setup(x => x.RenewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var lockImpl = new SchedulerLockImpl("PullJob", _lockProvider.Object,
            new SchedulerLockOptions { RenewalIntervalSeconds = 60, TtlSeconds = 120 },
            NullLogger<SchedulerLockImpl>.Instance);

        // Must not throw
        var act = async () => await lockImpl.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task Renewal_Failure_Does_Not_Cancel_Job()
    {
        _lockProvider
            .Setup(x => x.RenewAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"));
        _lockProvider
            .Setup(x => x.ReleaseAsync(It.IsAny<string>(), It.IsAny<string>()))
            .Returns(Task.CompletedTask);

        await using var lockImpl = new SchedulerLockImpl("RetryJob", _lockProvider.Object,
            new SchedulerLockOptions { RenewalIntervalSeconds = 1, TtlSeconds = 4 },
            NullLogger<SchedulerLockImpl>.Instance);

        // Wait past first renewal interval — renewal fails but lock impl stays alive
        await Task.Delay(TimeSpan.FromSeconds(1.5));

        // Lock is still alive (no exception propagated externally)
        lockImpl.JobName.Should().Be("RetryJob");
    }
}
```

---

## Step 9 — Unit Tests: `SchedulerJobGuardTests`

**File:** `tests/MSOSync.SchedulerTests/SchedulerJobGuardTests.cs`

- [ ] Create the file.

```csharp
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerJobGuardTests
{
    private readonly Mock<ISchedulerLockFactory>    _lockFactory = new();
    private readonly Mock<ISchedulerHealthReporter> _health      = new();

    [Fact]
    public async Task RunAsync_Skips_Work_When_Lock_Is_Null()
    {
        _lockFactory
            .Setup(x => x.TryAcquireAsync("SyncJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync((ISchedulerLock?)null);

        var workCalled = false;
        await SchedulerJobGuard.RunAsync(
            "SyncJob",
            _lockFactory.Object,
            _health.Object,
            NullLogger.Instance,
            _ => { workCalled = true; return Task.CompletedTask; },
            CancellationToken.None);

        workCalled.Should().BeFalse();
        _health.Verify(x => x.RecordStandby("SyncJob"), Times.Once);
        _health.Verify(
            x => x.RecordRunning(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<DateTimeOffset>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_Executes_Work_And_Calls_RecordRunning_Then_RecordIdle()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns("SyncJob");
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1234");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync("SyncJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);

        var workCalled = false;
        await SchedulerJobGuard.RunAsync(
            "SyncJob",
            _lockFactory.Object,
            _health.Object,
            NullLogger.Instance,
            _ => { workCalled = true; return Task.CompletedTask; },
            CancellationToken.None);

        workCalled.Should().BeTrue();
        _health.Verify(
            x => x.RecordRunning("SyncJob", "HOST:1234", It.IsAny<DateTimeOffset>()),
            Times.Once);
        _health.Verify(x => x.RecordIdle("SyncJob"), Times.Once);
        _health.Verify(x => x.RecordStandby(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_Calls_RecordIdle_Even_When_Work_Throws()
    {
        var fakeLock = new Mock<ISchedulerLock>();
        fakeLock.SetupGet(x => x.JobName).Returns("PurgeJob");
        fakeLock.SetupGet(x => x.Owner).Returns("HOST:1234");
        fakeLock.SetupGet(x => x.AcquiredAt).Returns(DateTimeOffset.UtcNow);
        fakeLock.Setup(x => x.DisposeAsync()).Returns(ValueTask.CompletedTask);

        _lockFactory
            .Setup(x => x.TryAcquireAsync("PurgeJob", It.IsAny<CancellationToken>()))
            .ReturnsAsync(fakeLock.Object);

        var act = async () => await SchedulerJobGuard.RunAsync(
            "PurgeJob",
            _lockFactory.Object,
            _health.Object,
            NullLogger.Instance,
            _ => throw new InvalidOperationException("boom"),
            CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        _health.Verify(x => x.RecordIdle("PurgeJob"), Times.Once);
    }
}
```

---

## Step 10 — Verify Build

- [ ] Run: `dotnet build src/MSOSync.Scheduler/MSOSync.Scheduler.csproj`
  - Expected: 0 errors.
- [ ] Run: `dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj --filter "SchedulerLockFactoryTests|SchedulerLockImplTests|SchedulerJobGuardTests"`
  - Expected: all new tests pass (timing-dependent `Renewal_Calls_RenewAsync_At_Interval` may need CI patience).

---

## Acceptance Criteria

- `ISchedulerLock`, `ISchedulerLockFactory`, `SchedulerLockOptions` are public types in `MSOSync.Scheduler` namespace.
- `SchedulerLockImpl` and `SchedulerLockFactory` are `internal` in `MSOSync.Scheduler.Internal`.
- `SchedulerJobGuard` is a `public static class` in `MSOSync.Scheduler`.
- `SchedulerLockImpl` starts a renewal `Task` immediately on construction, not lazily.
- `DisposeAsync` on `SchedulerLockImpl` always cancels renewal first, then calls `ReleaseAsync` (without passing a CancellationToken so release always completes at shutdown).
- All unit tests pass.
- No changes to existing job classes in this task.
