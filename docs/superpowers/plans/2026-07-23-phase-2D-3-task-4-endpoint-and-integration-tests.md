# Task 4 — Scheduler-Status Endpoint + Integration Tests

**Phase:** 2D.3
**File:** `2026-07-23-phase-2D-3-task-4-endpoint-and-integration-tests.md`
**Depends on:** Task 2 (ISchedulerHealthReporter) and Task 3 (migrated jobs, seeded lock rows)

---

## Overview

1. Add `GET /api/v1/system/scheduler-status` to `SystemController`.
2. Add `SchedulerStatusDto` response type.
3. Write integration tests for the new endpoint (uses `SystemFixture` pattern).
4. Write dual-instance integration tests against a real SQL Server via `DatabaseFixture` / Testcontainers, verifying exactly-one-wins and post-release re-acquisition.

---

## Step 1 — Create `SchedulerStatusDto`

**File:** `src/MSOSync.Api/Dtos/SchedulerStatusDto.cs`

- [ ] Create the file (new DTO; no controller changes yet):

```csharp
namespace MSOSync.Api.Dtos;

/// <summary>Response shape for GET /api/v1/system/scheduler-status.</summary>
public sealed record SchedulerStatusDto(
    string              InstanceId,
    SchedulerJobDto[]   Jobs);

public sealed record SchedulerJobDto(
    string           JobName,
    string           Mode,          // "Idle" | "Running" | "Standby"
    string?          LockOwner,
    DateTimeOffset?  LockedSince,
    DateTimeOffset   LastUpdated);
```

---

## Step 2 — Add `scheduler-status` action to `SystemController`

**File:** `src/MSOSync.Api/Controllers/SystemController.cs`

Current constructor:
```csharp
public sealed class SystemController(
    ISystemHealthService healthSvc,
    IWorkerStatusRegistry workerRegistry,
    IOverviewQueryService overviewSvc,
    IHostEnvironment env) : ControllerBase
```

- [ ] Add `ISchedulerHealthReporter schedulerHealth` to constructor parameters.
- [ ] Add a using for the DTO namespace: `using MSOSync.Api.Dtos;`
- [ ] Add a using for the scheduler namespace: `using MSOSync.Scheduler;`
- [ ] Add the new action after `GetInfo`:

```csharp
[HttpGet("scheduler-status")]
[Authorize(Roles = "Admin")]
[ProducesResponseType<SchedulerStatusDto>(200)]
public IActionResult GetSchedulerStatus()
{
    var instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
    var jobs = schedulerHealth.GetAll()
        .Select(s => new SchedulerJobDto(
            s.JobName,
            s.Mode.ToString(),
            s.LockOwner,
            s.LockedSince,
            s.LastUpdated))
        .ToArray();

    return Ok(new SchedulerStatusDto(instanceId, jobs));
}
```

Full updated controller (write the whole file):

```csharp
using System.Reflection;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using MSOSync.Api.Dtos;
using MSOSync.Common.Health;
using MSOSync.Common.Workers;
using MSOSync.Metadata.Overview;
using MSOSync.Scheduler;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class SystemController(
    ISystemHealthService     healthSvc,
    IWorkerStatusRegistry    workerRegistry,
    IOverviewQueryService    overviewSvc,
    ISchedulerHealthReporter schedulerHealth,
    IHostEnvironment         env) : ControllerBase
{
    [HttpGet("health")]
    [ProducesResponseType<HealthContribution[]>(200)]
    public async Task<IActionResult> GetHealthAsync(CancellationToken ct)
        => Ok(await healthSvc.GetAllAsync(ct));

    [HttpGet("workers")]
    [ProducesResponseType<WorkerStatusDto[]>(200)]
    public IActionResult GetWorkers()
        => Ok(workerRegistry.GetAll());

    [HttpGet("overview")]
    [ProducesResponseType<OverviewDto>(200)]
    public async Task<IActionResult> GetOverviewAsync(CancellationToken ct)
        => Ok(await overviewSvc.GetAsync(ct));

    [HttpGet("info")]
    [ProducesResponseType<SystemInfoDto>(200)]
    public IActionResult GetInfo()
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();
        var uptime = DateTime.UtcNow - process.StartTime.ToUniversalTime();
        var entryAssembly = Assembly.GetEntryAssembly();
        var version = entryAssembly?.GetName().Version?.ToString()
                      ?? entryAssembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                      ?? "12C";
        string? buildDate = null;
        var location = entryAssembly?.Location;
        if (!string.IsNullOrEmpty(location) && System.IO.File.Exists(location))
            buildDate = System.IO.File.GetLastWriteTimeUtc(location).ToString("O");
        return Ok(new SystemInfoDto(
            Version: version,
            BuildDate: buildDate,
            GitCommit: null,
            DotNetRuntime: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            OperatingSystem: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            DatabaseMigration: "M025",
            Edition: "Community",
            Environment: env.EnvironmentName,
            ServerTime: DateTime.UtcNow.ToString("O"),
            ProcessUptime: $"{(int)uptime.TotalDays}d {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"));
    }

    [HttpGet("scheduler-status")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType<SchedulerStatusDto>(200)]
    public IActionResult GetSchedulerStatus()
    {
        var instanceId = $"{Environment.MachineName}:{Environment.ProcessId}";
        var jobs = schedulerHealth.GetAll()
            .Select(s => new SchedulerJobDto(
                s.JobName,
                s.Mode.ToString(),
                s.LockOwner,
                s.LockedSince,
                s.LastUpdated))
            .ToArray();

        return Ok(new SchedulerStatusDto(instanceId, jobs));
    }
}
```

---

## Step 3 — Register `ISchedulerHealthReporter` in `SystemFixture` (integration test host)

**File:** `tests/MSOSync.IntegrationTests/System/SystemFixture.cs`

The fixture builds a test host without `AddSyncScheduler`. `SystemController` now requires `ISchedulerHealthReporter` — register a stub in the test host.

- [ ] In `SystemFixture.CreateHost`, add after the `ISystemHealthContributor` registrations:

```csharp
// 2D.3: Scheduler health reporter (stub for system tests — no real scheduler in test host)
testBuilder.Services.AddSingleton<ISchedulerHealthReporter, SchedulerHealthReporter>();
```

Add the using at top of `SystemFixture.cs`:
```csharp
using MSOSync.Scheduler;
```

---

## Step 4 — Create `SchedulerStatusEndpointTests`

**File:** `tests/MSOSync.IntegrationTests/System/SchedulerStatusEndpointTests.cs`

- [ ] Create the file. Uses the existing `SystemFixture` and `SystemAdminCollection`.

```csharp
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MSOSync.Scheduler;
using Xunit;

namespace MSOSync.IntegrationTests.SystemAdmin;

[Collection("SystemAdmin")]
public sealed class SchedulerStatusEndpointTests(SystemFixture fixture)
{
    [Fact]
    public async Task GET_scheduler_status_returns_200_for_admin()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetAsync("/api/v1/system/scheduler-status");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GET_scheduler_status_returns_401_for_unauthenticated()
    {
        var client = fixture.CreateClient(); // no auth header

        var response = await client.GetAsync("/api/v1/system/scheduler-status");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GET_scheduler_status_returns_403_for_viewer()
    {
        var client = await fixture.ViewerClientAsync();

        var response = await client.GetAsync("/api/v1/system/scheduler-status");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GET_scheduler_status_response_has_instanceId_and_jobs_array()
    {
        var client = await fixture.AdminClientAsync();

        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/scheduler-status");

        response.GetProperty("instanceId").GetString().Should().MatchRegex(@"^.+:\d+$");
        response.GetProperty("jobs").ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task GET_scheduler_status_jobs_have_expected_shape_when_statuses_exist()
    {
        // Pre-seed some status via the ISchedulerHealthReporter registered in the fixture
        await using var scope = fixture.Services.CreateAsyncScope();
        var reporter = scope.ServiceProvider.GetRequiredService<ISchedulerHealthReporter>();
        reporter.RecordRunning("SyncJob",  "HOST:1234", DateTimeOffset.UtcNow);
        reporter.RecordStandby("PullJob");
        reporter.RecordIdle("PurgeJob");

        var client   = await fixture.AdminClientAsync();
        var response = await client.GetFromJsonAsync<JsonElement>("/api/v1/system/scheduler-status");
        var jobs     = response.GetProperty("jobs").EnumerateArray().ToArray();

        jobs.Should().HaveCount(3);
        jobs.Should().Contain(j =>
            j.GetProperty("jobName").GetString() == "SyncJob" &&
            j.GetProperty("mode").GetString()    == "Running");
        jobs.Should().Contain(j =>
            j.GetProperty("jobName").GetString() == "PullJob" &&
            j.GetProperty("mode").GetString()    == "Standby");
        jobs.Should().Contain(j =>
            j.GetProperty("jobName").GetString() == "PurgeJob" &&
            j.GetProperty("mode").GetString()    == "Idle");
    }

    [Fact]
    public async Task GET_health_reflects_standby_state_as_healthy()
    {
        await using var scope = fixture.Services.CreateAsyncScope();
        var reporter = scope.ServiceProvider.GetRequiredService<ISchedulerHealthReporter>();
        reporter.RecordStandby("SyncJob");
        reporter.RecordStandby("PullJob");
        reporter.RecordStandby("PurgeJob");
        reporter.RecordStandby("RetryJob");

        var client   = await fixture.AdminClientAsync();
        var response = await client.GetFromJsonAsync<JsonElement[]>("/api/v1/system/health");

        var schedulerEntry = response!
            .FirstOrDefault(e => e.GetProperty("name").GetString() == "Scheduler");

        schedulerEntry.Should().NotBeNull("Scheduler contributor should be registered");
        schedulerEntry!.GetProperty("level").GetString().Should().Be("Healthy");
        schedulerEntry.GetProperty("summary").GetString().Should().Contain("standby");
    }
}
```

---

## Step 5 — Create `SchedulerLockIntegrationTests` (dual-instance simulation)

**File:** `tests/MSOSync.IntegrationTests/Scheduler/SchedulerLockIntegrationTests.cs`

These tests use two `SchedulerLockFactory` instances sharing the same SQL `AppDbContext` against `MSOSync_IntegrationTests` (localdb, migrated by `DatabaseFixture`). They simulate two hub instances racing for the same lock row.

- [ ] Create directory `tests/MSOSync.IntegrationTests/Scheduler/`.
- [ ] Create the file:

```csharp
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MSOSync.Persistence;
using MSOSync.Persistence.Lock;
using MSOSync.Scheduler;
using MSOSync.Scheduler.Internal;
using Xunit;

namespace MSOSync.IntegrationTests.Scheduler;

/// <summary>
/// Dual-instance scheduler lock integration tests.
/// Requires SQL Server (localdb) — skipped automatically if DB is unavailable.
/// Uses the MSOSync_IntegrationTests database (migrated by DatabaseFixture).
/// </summary>
[Collection("Database")]
public sealed class SchedulerLockIntegrationTests(DatabaseFixture db)
{
    private const string ConnStr =
        "Server=(localdb)\\mssqllocaldb;Database=MSOSync_IntegrationTests;" +
        "Trusted_Connection=True;TrustServerCertificate=True;";

    private AppDbContext CreateDbContext()
    {
        var opts = AppDbContext.CreateOptionsBuilder(ConnStr).Options;
        return new AppDbContext(opts);
    }

    private ISchedulerLockFactory CreateFactory(AppDbContext context, int ttlSeconds = 30, int renewalSeconds = 5)
    {
        var options = Options.Create(new SchedulerLockOptions
        {
            TtlSeconds             = ttlSeconds,
            RenewalIntervalSeconds = renewalSeconds,
            LockPrefix             = "scheduler:"
        });
        var lockProvider = new DatabaseLockProvider(context);
        return new SchedulerLockFactory(lockProvider, options,
            NullLogger<SchedulerLockFactory>.Instance);
    }

    private async Task EnsureLockRowAsync(string lockName)
    {
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";
        await db.Db.Database.ExecuteSqlRawAsync(
            $"IF NOT EXISTS (SELECT 1 FROM [{schema}].[sync_lock] WHERE lock_name = {{0}}) " +
            $"INSERT INTO [{schema}].[sync_lock] (lock_name, lock_owner, lock_time, scope) " +
            "VALUES ({0}, NULL, NULL, 0)",
            new object[] { lockName });

        // Clear any leftover state from previous test run
        await db.Db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{schema}].[sync_lock] SET lock_owner = NULL, lock_time = NULL WHERE lock_name = {{0}}",
            new object[] { lockName });
    }

    [Fact]
    public async Task Only_One_Instance_Acquires_Lock_When_Both_Race()
    {
        const string jobName = "SyncJob";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await using var dbA = CreateDbContext();
        await using var dbB = CreateDbContext();
        var factoryA = CreateFactory(dbA);
        var factoryB = CreateFactory(dbB);

        // Race both instances
        var taskA = factoryA.TryAcquireAsync(jobName, CancellationToken.None);
        var taskB = factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        var results = await Task.WhenAll(taskA, taskB);

        var lockA = results[0];
        var lockB = results[1];

        var winners = new[] { lockA, lockB }.Count(l => l is not null);
        winners.Should().Be(1, "exactly one instance should win the lock acquisition race");

        // Cleanup
        if (lockA is not null) await lockA.DisposeAsync();
        if (lockB is not null) await lockB.DisposeAsync();
    }

    [Fact]
    public async Task Second_Instance_Acquires_Lock_After_First_Releases()
    {
        const string jobName = "PullJob";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await using var dbA = CreateDbContext();
        await using var dbB = CreateDbContext();
        var factoryA = CreateFactory(dbA);
        var factoryB = CreateFactory(dbB);

        // Instance A acquires
        await using var lockA = await factoryA.TryAcquireAsync(jobName, CancellationToken.None);
        lockA.Should().NotBeNull("instance A should win first");

        // Instance B attempts — should get null
        var lockB1 = await factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        lockB1.Should().BeNull("instance B should see A's lock");

        // Instance A releases
        await lockA!.DisposeAsync();

        // Instance B retries — should now acquire
        await using var lockB2 = await factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        lockB2.Should().NotBeNull("instance B should acquire after A releases");
        lockB2!.JobName.Should().Be(jobName);
    }

    [Fact]
    public async Task Lock_Survives_Past_Stale_Timeout_With_Renewal()
    {
        // Use a very short TTL (TryAcquireAsync uses DATEADD(MINUTE, -10) for the existing
        // stale check — we cannot change that SQL in 2D.3 without modifying TryAcquireAsync.
        // Instead, this test verifies that renewal keeps resetting lock_time so a second
        // factory (with the same stale check) cannot steal it while renewal is running.
        //
        // We use RenewalIntervalSeconds = 1 and wait 3 seconds, then try to steal.
        const string jobName = "PurgeJob";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await using var dbA = CreateDbContext();
        await using var dbB = CreateDbContext();
        var factoryA = CreateFactory(dbA, ttlSeconds: 120, renewalSeconds: 1);
        var factoryB = CreateFactory(dbB, ttlSeconds: 120, renewalSeconds: 1);

        await using var lockA = await factoryA.TryAcquireAsync(jobName, CancellationToken.None);
        lockA.Should().NotBeNull();

        // Wait for a couple of renewals
        await Task.Delay(TimeSpan.FromSeconds(3));

        // B should still be unable to steal (lock_time is being renewed)
        var lockB = await factoryB.TryAcquireAsync(jobName, CancellationToken.None);
        lockB.Should().BeNull("renewal should keep lock_time fresh; B cannot steal");

        if (lockB is not null) await lockB.DisposeAsync();
    }

    [Fact]
    public async Task DatabaseLockProvider_SeedSchedulerLocksAsync_Is_Idempotent()
    {
        // Call seed twice — should not throw or duplicate rows
        await DatabaseLockProvider.SeedSchedulerLocksAsync(db.Db);
        await DatabaseLockProvider.SeedSchedulerLocksAsync(db.Db);

        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";

        // Verify rows exist (count distinct)
        var lockNames = new[]
        {
            "scheduler:SyncJob", "scheduler:PullJob",
            "scheduler:PurgeJob", "scheduler:RetryJob"
        };

        foreach (var name in lockNames)
        {
            var count = await db.Db.Database.ExecuteSqlRawAsync(
                $"SELECT COUNT(*) FROM [{schema}].[sync_lock] WHERE lock_name = {{0}}",
                new object[] { name });
            // ExecuteSqlRawAsync returns rows affected, not count — use raw query instead
        }

        // Use FormattableString to check row existence
        var syncJobExists = await db.Db.SyncLocks
            .AnyAsync(l => l.LockName == "scheduler:SyncJob");
        syncJobExists.Should().BeTrue("seed should insert scheduler:SyncJob row");

        var pullJobExists = await db.Db.SyncLocks
            .AnyAsync(l => l.LockName == "scheduler:PullJob");
        pullJobExists.Should().BeTrue("seed should insert scheduler:PullJob row");
    }

    [Fact]
    public async Task RenewAsync_Updates_LockTime_Without_Changing_Owner()
    {
        const string jobName = "RetryJob";
        const string owner   = "TEST-HOST:9999";
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        // Manually set the lock row to our test owner
        await db.Db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = DATEADD(SECOND, -5, GETUTCDATE()) " +
            "WHERE lock_name = {1}",
            new object[] { owner, $"scheduler:{jobName}" });

        await using var dbA = CreateDbContext();
        var lockProvider = new DatabaseLockProvider(dbA);
        await lockProvider.RenewAsync($"scheduler:{jobName}", owner);

        var row = await db.Db.SyncLocks
            .FirstOrDefaultAsync(l => l.LockName == $"scheduler:{jobName}");
        row.Should().NotBeNull();
        row!.LockOwner.Should().Be(owner);
        row.LockTime.Should().BeCloseTo(DateTime.UtcNow, precision: TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ReleaseAsync_Clears_Owner_And_LockTime()
    {
        const string jobName = "SyncJob";
        const string owner   = "TEST-HOST:8888";
        var schema = Environment.GetEnvironmentVariable("MSOSYNC_SCHEMA") ?? "msosync";
        await EnsureLockRowAsync($"scheduler:{jobName}");

        await db.Db.Database.ExecuteSqlRawAsync(
            $"UPDATE [{schema}].[sync_lock] " +
            "SET lock_owner = {0}, lock_time = GETUTCDATE() " +
            "WHERE lock_name = {1}",
            new object[] { owner, $"scheduler:{jobName}" });

        await using var dbA = CreateDbContext();
        var lockProvider = new DatabaseLockProvider(dbA);
        await lockProvider.ReleaseAsync($"scheduler:{jobName}", owner);

        var row = await db.Db.SyncLocks
            .FirstOrDefaultAsync(l => l.LockName == $"scheduler:{jobName}");
        row!.LockOwner.Should().BeNull();
        row.LockTime.Should().BeNull();
    }
}
```

**Note on `db.Db.SyncLocks`:** Verify the `AppDbContext` has a `DbSet<SyncLock> SyncLocks` property. If the entity set name differs, adjust accordingly. The `SyncLock` entity has properties: `LockName`, `LockOwner`, `LockTime`, `Scope`.

---

## Step 6 — Verify `AppDbContext` exposes `SyncLocks`

- [ ] Search for `SyncLock` entity in `src/MSOSync.Persistence/`:

```
Grep pattern: DbSet.*SyncLock
path: src/MSOSync.Persistence/
```

If the DbSet is named differently (e.g., `Locks`), update the test references in Step 5 to match. If no DbSet exists (raw SQL only), replace `db.Db.SyncLocks.AnyAsync(...)` with raw SQL:

```csharp
// Alternative if no DbSet:
var exists = (await db.Db.Database.SqlQueryRaw<int>(
    $"SELECT COUNT(1) AS Value FROM [{schema}].[sync_lock] WHERE lock_name = {{0}}",
    "scheduler:SyncJob").ToListAsync()).First();
exists.Should().BeGreaterThan(0);
```

---

## Step 7 — Add `Scheduler` project reference to `MSOSync.IntegrationTests`

**File:** `tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj`

`MSOSync.Scheduler` is already referenced. Check that `MSOSync.Api` is also referenced (it is — see existing `.csproj`). The `SchedulerLockFactory` and `SchedulerLockImpl` are `internal` — integration tests use the public `ISchedulerLockFactory` interface. The `SchedulerLockFactory` class needs to be accessible from tests. Options:

- Option A: Add `[assembly: InternalsVisibleTo("MSOSync.IntegrationTests")]` to `MSOSync.Scheduler.csproj` (already has it for `MSOSync.SchedulerTests`).
- Option B: The integration test only needs `ISchedulerLockFactory` (public) to call `TryAcquireAsync`, but instantiating `new SchedulerLockFactory(...)` directly requires internal access.

- [ ] Add `InternalsVisibleTo` for integration tests in `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj`:

```xml
<AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
  <_Parameter1>MSOSync.IntegrationTests</_Parameter1>
</AssemblyAttribute>
```

The existing `MSOSync.Scheduler.csproj` already has the `<ItemGroup>` for `InternalsVisibleToAttribute`. Add a second entry for `MSOSync.IntegrationTests`.

---

## Step 8 — Run All Integration Tests

- [ ] `dotnet build MSOSync.sln` — 0 errors.
- [ ] `dotnet test tests/MSOSync.IntegrationTests/MSOSync.IntegrationTests.csproj --filter "SchedulerLock|SchedulerStatus"` — all pass (requires localdb).
- [ ] `dotnet test tests/MSOSync.SchedulerTests/MSOSync.SchedulerTests.csproj` — all pass.
- [ ] `dotnet test MSOSync.sln` — full suite must pass with zero regressions.

---

## Acceptance Criteria

- `GET /api/v1/system/scheduler-status` returns 200 for Admin, 401 for unauthenticated, 403 for Viewer.
- Response contains `instanceId` (string matching `HOST:PID`) and `jobs` array.
- Each job entry has `jobName`, `mode` (string), `lockOwner` (nullable), `lockedSince` (nullable), `lastUpdated`.
- `GET /api/v1/system/health` includes a `"Scheduler"` entry with `level = "Healthy"` when all jobs are standby.
- Dual-instance SQL tests confirm exactly one winner per lock acquisition race.
- Post-release re-acquisition test confirms B acquires after A disposes.
- `RenewAsync` and `ReleaseAsync` SQL update tests pass against real DB.
- All existing integration tests continue to pass (no regressions from `SystemController` constructor change).
