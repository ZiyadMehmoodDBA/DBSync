# Task 2 — ISchedulerHealthReporter + SchedulerHealthReporter + SchedulerHealthContributor

**Phase:** 2D.3
**File:** `2026-07-23-phase-2D-3-task-2-health-reporter.md`
**Depends on:** Task 1 (references `ISchedulerLock` — but only through method signatures, so can be developed in parallel with T1 given interface stubs exist)

---

## Overview

Implement in-process, per-job lock-state tracking for this instance:

- `SchedulerJobMode` enum — `Idle`, `Running`, `Standby`
- `SchedulerJobStatus` record — per-job snapshot
- `ISchedulerHealthReporter` — interface consumed by `SchedulerJobGuard` and the status endpoint
- `SchedulerHealthReporter` — `ConcurrentDictionary`-backed singleton; no persistence
- `SchedulerHealthContributor` — wires into the existing `ISystemHealthContributor` chain (already powering `/api/v1/system/health`)

---

## Step 1 — Create `SchedulerJobStatus.cs`

**File:** `src/MSOSync.Scheduler/SchedulerJobStatus.cs`

- [ ] Create the file:

```csharp
namespace MSOSync.Scheduler;

public enum SchedulerJobMode { Idle, Running, Standby }

/// <summary>
/// Snapshot of one job's scheduler lock state on this instance.
/// </summary>
public sealed record SchedulerJobStatus(
    string           JobName,
    SchedulerJobMode Mode,
    string?          LockOwner,
    DateTimeOffset?  LockedSince,
    DateTimeOffset   LastUpdated);
```

---

## Step 2 — Create `ISchedulerHealthReporter`

**File:** `src/MSOSync.Scheduler/ISchedulerHealthReporter.cs`

- [ ] Create the file:

```csharp
namespace MSOSync.Scheduler;

/// <summary>
/// Tracks per-job scheduler lock state for this running instance.
///
/// <list type="bullet">
/// <item><description>Running — this instance holds the lock; job is executing.</description></item>
/// <item><description>Standby — another instance holds the lock; this instance skipped the tick.</description></item>
/// <item><description>Idle — lock not held by anyone (between ticks or before first tick).</description></item>
/// </list>
/// </summary>
public interface ISchedulerHealthReporter
{
    /// <summary>Records that this instance acquired the lock and is running the job.</summary>
    void RecordRunning(string jobName, string owner, DateTimeOffset acquiredAt);

    /// <summary>Records that another instance holds the lock; this instance skipped this tick.</summary>
    void RecordStandby(string jobName);

    /// <summary>Records that the job completed and the lock was released (between ticks).</summary>
    void RecordIdle(string jobName);

    /// <summary>Returns status snapshots for all jobs seen so far on this instance.</summary>
    SchedulerJobStatus[] GetAll();

    /// <summary>Returns the status snapshot for a specific job, defaulting to Idle if unseen.</summary>
    SchedulerJobStatus GetOne(string jobName);
}
```

---

## Step 3 — Create `SchedulerHealthReporter`

**File:** `src/MSOSync.Scheduler/SchedulerHealthReporter.cs`

- [ ] Create the file:

```csharp
using System.Collections.Concurrent;

namespace MSOSync.Scheduler;

/// <summary>
/// Thread-safe singleton that tracks per-job scheduler lock state using
/// a <see cref="ConcurrentDictionary"/>. No external storage — state is
/// in-process only and resets on restart.
/// </summary>
public sealed class SchedulerHealthReporter : ISchedulerHealthReporter
{
    private readonly ConcurrentDictionary<string, SchedulerJobStatus> _statuses = new();

    public void RecordRunning(string jobName, string owner, DateTimeOffset acquiredAt)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Running, owner, acquiredAt, DateTimeOffset.UtcNow);

    public void RecordStandby(string jobName)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Standby, null, null, DateTimeOffset.UtcNow);

    public void RecordIdle(string jobName)
        => _statuses[jobName] = new SchedulerJobStatus(
            jobName, SchedulerJobMode.Idle, null, null, DateTimeOffset.UtcNow);

    public SchedulerJobStatus[] GetAll()
        => [.. _statuses.Values];

    public SchedulerJobStatus GetOne(string jobName)
        => _statuses.GetValueOrDefault(jobName)
           ?? new SchedulerJobStatus(jobName, SchedulerJobMode.Idle, null, null, DateTimeOffset.UtcNow);
}
```

---

## Step 4 — Create `SchedulerHealthContributor`

**File:** `src/MSOSync.App/Health/SchedulerHealthContributor.cs`

`ISystemHealthContributor` in `MSOSync.Common.Health` has signature:

```csharp
public interface ISystemHealthContributor
{
    string Name { get; }
    Task<HealthContribution> GetAsync(CancellationToken ct);
}

public sealed record HealthContribution(
    string Name,
    string Level,      // "Healthy" | "Degraded" | "Unhealthy"
    string Summary,
    string? Detail = null);
```

- [ ] Create the file. Note `HealthContribution` uses positional record `(Name, Level, Summary, Detail)`:

```csharp
using MSOSync.Common.Health;
using MSOSync.Scheduler;

namespace MSOSync.App.Health;

/// <summary>
/// Contributes scheduler lock state to the /api/v1/system/health aggregator.
/// An instance where all jobs are Standby is healthy — peer instance is the active scheduler.
/// </summary>
public sealed class SchedulerHealthContributor(ISchedulerHealthReporter reporter)
    : ISystemHealthContributor
{
    public string Name => "Scheduler";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        var statuses    = reporter.GetAll();
        var standbyJobs = statuses.Where(s => s.Mode == SchedulerJobMode.Standby).ToArray();
        var runningCount = statuses.Count(s => s.Mode == SchedulerJobMode.Running);

        string summary;
        if (statuses.Length == 0)
        {
            summary = "No scheduler jobs registered yet";
        }
        else if (standbyJobs.Length == statuses.Length)
        {
            summary = "This instance is scheduler standby — all jobs running on peer";
        }
        else
        {
            summary = $"{runningCount} job(s) active on this instance";
        }

        var detail = statuses.Length > 0
            ? string.Join("; ", statuses.Select(s =>
                $"{s.JobName}={s.Mode}" +
                (s.LockOwner is not null ? $"[{s.LockOwner}]" : string.Empty)))
            : null;

        return Task.FromResult(new HealthContribution(Name, "Healthy", summary, detail));
    }
}
```

---

## Step 5 — Register `SchedulerHealthContributor` in Program.cs

**File:** `src/MSOSync.App/Program.cs`

Find the block where other `ISystemHealthContributor` singletons are registered:

```csharp
// --- Epic 12C: System Health ---
builder.Services.AddSingleton<ISystemHealthService, SystemHealthService>();
builder.Services.AddSingleton<ISystemHealthContributor, WorkerHealthContributor>();
builder.Services.AddSingleton<ISystemHealthContributor, DatabaseHealthContributor>();
builder.Services.AddSingleton<ISystemHealthContributor, ApiHealthContributor>();
builder.Services.AddSingleton<ISystemHealthContributor, SignalRHealthContributor>();
```

- [ ] Add `SchedulerHealthContributor` registration immediately after `SignalRHealthContributor`:

```csharp
builder.Services.AddSingleton<ISystemHealthContributor, SchedulerHealthContributor>();
```

The `ISchedulerHealthReporter` singleton it depends on is registered by `AddSyncScheduler` in Task 3.

---

## Step 6 — Unit Tests: `SchedulerHealthReporterTests`

**File:** `tests/MSOSync.SchedulerTests/SchedulerHealthReporterTests.cs`

- [ ] Create the file:

```csharp
using FluentAssertions;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.SchedulerTests;

public sealed class SchedulerHealthReporterTests
{
    private readonly SchedulerHealthReporter _sut = new();

    [Fact]
    public void GetOne_Returns_Idle_For_Unseen_Job()
    {
        var status = _sut.GetOne("UnknownJob");

        status.Mode.Should().Be(SchedulerJobMode.Idle);
        status.LockOwner.Should().BeNull();
        status.LockedSince.Should().BeNull();
    }

    [Fact]
    public void RecordRunning_Updates_Mode_To_Running_With_Owner()
    {
        var now = DateTimeOffset.UtcNow;
        _sut.RecordRunning("SyncJob", "HOST:1234", now);

        var status = _sut.GetOne("SyncJob");
        status.Mode.Should().Be(SchedulerJobMode.Running);
        status.LockOwner.Should().Be("HOST:1234");
        status.LockedSince.Should().Be(now);
    }

    [Fact]
    public void RecordStandby_Updates_Mode_To_Standby_With_Null_Owner()
    {
        _sut.RecordStandby("PullJob");

        var status = _sut.GetOne("PullJob");
        status.Mode.Should().Be(SchedulerJobMode.Standby);
        status.LockOwner.Should().BeNull();
        status.LockedSince.Should().BeNull();
    }

    [Fact]
    public void RecordIdle_Updates_Mode_To_Idle()
    {
        _sut.RecordRunning("RetryJob", "HOST:99", DateTimeOffset.UtcNow);
        _sut.RecordIdle("RetryJob");

        var status = _sut.GetOne("RetryJob");
        status.Mode.Should().Be(SchedulerJobMode.Idle);
        status.LockOwner.Should().BeNull();
    }

    [Fact]
    public void GetAll_Returns_All_Registered_Jobs()
    {
        _sut.RecordRunning("SyncJob",  "HOST:1", DateTimeOffset.UtcNow);
        _sut.RecordStandby("PullJob");
        _sut.RecordIdle("PurgeJob");

        var all = _sut.GetAll();

        all.Should().HaveCount(3);
        all.Should().ContainSingle(s => s.JobName == "SyncJob"  && s.Mode == SchedulerJobMode.Running);
        all.Should().ContainSingle(s => s.JobName == "PullJob"  && s.Mode == SchedulerJobMode.Standby);
        all.Should().ContainSingle(s => s.JobName == "PurgeJob" && s.Mode == SchedulerJobMode.Idle);
    }

    [Fact]
    public void LastUpdated_Is_Populated_On_Every_Record_Call()
    {
        var before = DateTimeOffset.UtcNow.AddMilliseconds(-10);
        _sut.RecordRunning("SyncJob", "HOST:1", DateTimeOffset.UtcNow);
        var after = DateTimeOffset.UtcNow.AddMilliseconds(10);

        var status = _sut.GetOne("SyncJob");
        status.LastUpdated.Should().BeOnOrAfter(before).And.BeOnOrBefore(after);
    }

    [Fact]
    public void Concurrent_Updates_Do_Not_Throw()
    {
        // Simulate concurrent tick updates from multiple threads
        var tasks = Enumerable.Range(0, 50).Select(i => Task.Run(() =>
        {
            if (i % 3 == 0) _sut.RecordRunning("SyncJob", $"HOST:{i}", DateTimeOffset.UtcNow);
            else if (i % 3 == 1) _sut.RecordStandby("SyncJob");
            else _sut.RecordIdle("SyncJob");
        }));

        var act = async () => await Task.WhenAll(tasks);
        act.Should().NotThrowAsync();
    }
}
```

---

## Step 7 — Verify Build

- [ ] Run: `dotnet build src/MSOSync.Scheduler/MSOSync.Scheduler.csproj`
  - Expected: 0 errors.
- [ ] Run: `dotnet build src/MSOSync.App/MSOSync.App.csproj`
  - Note: Will warn that `ISchedulerHealthReporter` is not yet registered (DI registration happens in T3 `SyncSchedulerExtensions`). This is expected — the contributor will find its dependency once T3 wires it.
- [ ] Run: `dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj --filter "SchedulerHealthReporterTests"`
  - Expected: all pass.

---

## Acceptance Criteria

- `ISchedulerHealthReporter`, `SchedulerJobStatus`, `SchedulerJobMode` are public in `MSOSync.Scheduler`.
- `SchedulerHealthReporter` is public (registered as singleton) with `ConcurrentDictionary` backing — no locks beyond what `ConcurrentDictionary` provides.
- `SchedulerHealthContributor.Name` returns `"Scheduler"`.
- `SchedulerHealthContributor` always returns `Level = "Healthy"` — an all-standby instance is healthy, not degraded.
- `GetOne` for an unseen job returns `SchedulerJobMode.Idle` (not null, not an exception).
- All 6 unit tests pass.
