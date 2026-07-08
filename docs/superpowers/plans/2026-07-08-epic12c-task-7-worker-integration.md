# Epic 12C Task 7: Wire IWorkerStatusRegistry into All 6 Background Workers

**Goal:** Make every background worker report its execution health to `IWorkerStatusRegistry`. This gives the System Administration Center live visibility into worker state, tick history, and failure counts.

**Prerequisites:** Task 6 must be complete. `IWorkerStatusRegistry` is registered as a singleton in `Program.cs`.

---

## Step 1: Identify the exact file paths for each worker

- [ ] Confirm the following files exist. If any path differs, locate the correct path with a file search.

| Worker | Expected path |
|--------|---------------|
| ExportJobWorker | `src/MSOSync.App/Workers/ExportJobWorker.cs` |
| ExportCleanupWorker | `src/MSOSync.App/Workers/ExportCleanupWorker.cs` |
| HeartbeatWorker | `src/MSOSync.Scheduler/Workers/HeartbeatWorker.cs` |
| ProbeWorker | `src/MSOSync.Scheduler/Workers/ProbeWorker.cs` |
| DecommissionWorker | `src/MSOSync.Scheduler/Workers/DecommissionWorker.cs` |
| AdminBootstrapper | `src/MSOSync.App/Workers/AdminBootstrapper.cs` |

---

## Step 2: Check for circular dependency before modifying Scheduler workers

`IWorkerStatusRegistry` lives in `MSOSync.App`. The Scheduler workers live in `MSOSync.Scheduler`.

- [ ] Open `src/MSOSync.Scheduler/MSOSync.Scheduler.csproj`
- [ ] Check whether a `<ProjectReference>` to `MSOSync.App` already exists

**If it already exists:** proceed to Step 3.

**If it does not exist:** check whether `MSOSync.App` references `MSOSync.Scheduler`. If so, moving the interface to `MSOSync.Common` is required to avoid a circular dependency:

Option A — No circular dependency, add the reference:
```xml
<ItemGroup>
  <ProjectReference Include="..\MSOSync.App\MSOSync.App.csproj" />
</ItemGroup>
```

Option B — Circular dependency exists, move interface to MSOSync.Common:
- [ ] Move `IWorkerStatusRegistry.cs`, `WorkerStatusDto.cs`, and the enum types from `src/MSOSync.App/Workers/` to `src/MSOSync.Common/Workers/`
- [ ] Update the namespace in those files from `MSOSync.App.Workers` to `MSOSync.Common.Workers`
- [ ] Update `WorkerStatusRegistry.cs` in `MSOSync.App` to use `using MSOSync.Common.Workers;`
- [ ] Update `Program.cs` registrations to use `MSOSync.Common.Workers`
- [ ] Ensure `MSOSync.Scheduler.csproj` already references `MSOSync.Common` (it should)

**For the rest of this task, use whichever namespace contains `IWorkerStatusRegistry` after this decision.**

---

## Step 3: Update ExportJobWorker.cs

- [ ] Open `src/MSOSync.App/Workers/ExportJobWorker.cs`
- [ ] Add `IWorkerStatusRegistry registry` to the primary constructor. The constructor currently looks like:

```csharp
public sealed class ExportJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ExportOptions> opts,
    ILogger<ExportJobWorker> logger) : BackgroundService
```

Change it to:

```csharp
public sealed class ExportJobWorker(
    IServiceScopeFactory scopeFactory,
    IOptions<ExportOptions> opts,
    ILogger<ExportJobWorker> logger,
    IWorkerStatusRegistry registry) : BackgroundService
```

- [ ] Override `StartAsync` to call `Register`. Add the following method above `ExecuteAsync`:

```csharp
public override async Task StartAsync(CancellationToken cancellationToken)
{
    registry.Register(nameof(ExportJobWorker), TimeSpan.FromSeconds(5));
    await base.StartAsync(cancellationToken);
}
```

- [ ] Find the inner loop body inside `ExecuteAsync`. It will look something like:

```csharp
try { await ProcessNextJobAsync(stoppingToken); }
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
catch (Exception ex) { logger.LogError(ex, "..."); }
```

Replace it with:

```csharp
registry.RecordTickStart(nameof(ExportJobWorker));
try
{
    await ProcessNextJobAsync(stoppingToken);
    registry.RecordTickComplete(nameof(ExportJobWorker));
}
catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
{
    registry.RecordTickComplete(nameof(ExportJobWorker));
    break;
}
catch (Exception ex)
{
    registry.RecordTickFailed(nameof(ExportJobWorker), ex);
    logger.LogError(ex, "ExportJobWorker tick failed");
}
```

---

## Step 4: Update ExportCleanupWorker.cs

- [ ] Open `src/MSOSync.App/Workers/ExportCleanupWorker.cs`
- [ ] Add `IWorkerStatusRegistry registry` to the primary constructor (same pattern as Step 3)
- [ ] Add `StartAsync` override:

```csharp
public override async Task StartAsync(CancellationToken cancellationToken)
{
    registry.Register(nameof(ExportCleanupWorker), TimeSpan.FromHours(1));
    await base.StartAsync(cancellationToken);
}
```

- [ ] Wrap the inner work call with tick reporting (same pattern as Step 3, substituting `ExportCleanupWorker` for `ExportJobWorker`)

---

## Step 5: Update HeartbeatWorker.cs

- [ ] Open `src/MSOSync.Scheduler/Workers/HeartbeatWorker.cs`
- [ ] Add `IWorkerStatusRegistry registry` to the primary constructor
- [ ] The worker reads its interval from config (e.g., `IOptions<HeartbeatOptions> opts` or similar). Find the field that stores the heartbeat interval. It will be something like:

```csharp
TimeSpan.FromSeconds(_opts.Value.HeartbeatIntervalSeconds)
```

- [ ] Add the using directive at the top of the file:

```csharp
using MSOSync.App.Workers;   // or MSOSync.Common.Workers depending on Step 2
```

- [ ] Add `StartAsync` override, reading the interval from config. Default to 30 seconds if the config value is 0 or missing:

```csharp
public override async Task StartAsync(CancellationToken cancellationToken)
{
    var intervalSeconds = _opts.Value.HeartbeatIntervalSeconds > 0
        ? _opts.Value.HeartbeatIntervalSeconds
        : 30;
    registry.Register(nameof(HeartbeatWorker), TimeSpan.FromSeconds(intervalSeconds));
    await base.StartAsync(cancellationToken);
}
```

Replace `_opts.Value.HeartbeatIntervalSeconds` with the actual property name used in that file.

- [ ] Wrap the inner work call with tick reporting (same pattern as Step 3, substituting `HeartbeatWorker`)

---

## Step 6: Update ProbeWorker.cs

- [ ] Open `src/MSOSync.Scheduler/Workers/ProbeWorker.cs`
- [ ] Add `IWorkerStatusRegistry registry` to the primary constructor
- [ ] Add `StartAsync` override:

```csharp
public override async Task StartAsync(CancellationToken cancellationToken)
{
    registry.Register(nameof(ProbeWorker), TimeSpan.FromSeconds(60));
    await base.StartAsync(cancellationToken);
}
```

- [ ] Wrap the inner work call with tick reporting (same pattern as Step 3, substituting `ProbeWorker`)

---

## Step 7: Update DecommissionWorker.cs

- [ ] Open `src/MSOSync.Scheduler/Workers/DecommissionWorker.cs`
- [ ] Add `IWorkerStatusRegistry registry` to the primary constructor
- [ ] Add `StartAsync` override:

```csharp
public override async Task StartAsync(CancellationToken cancellationToken)
{
    registry.Register(nameof(DecommissionWorker), TimeSpan.FromSeconds(30));
    await base.StartAsync(cancellationToken);
}
```

- [ ] Wrap the inner work call with tick reporting (same pattern as Step 3, substituting `DecommissionWorker`)

---

## Step 8: Update AdminBootstrapper.cs (register only)

`AdminBootstrapper` runs once at startup. It does not have a recurring tick loop, so only `Register` is needed — no tick reporting.

- [ ] Open `src/MSOSync.App/Workers/AdminBootstrapper.cs`
- [ ] Add `IWorkerStatusRegistry registry` to the primary constructor
- [ ] At the END of `StartAsync` (after all bootstrap work is complete), add:

```csharp
registry.Register(nameof(AdminBootstrapper), TimeSpan.FromDays(365)); // one-shot; interval is irrelevant
registry.RecordTickStart(nameof(AdminBootstrapper), TickTrigger.Startup);
registry.RecordTickComplete(nameof(AdminBootstrapper));
```

This marks AdminBootstrapper as registered and immediately Idle so it appears in the dashboard.

---

## Step 9: Register IWorkerStatusRegistry in MSOSync.Scheduler's host (if it has its own Program.cs)

- [ ] Check whether `src/MSOSync.Scheduler/` has its own `Program.cs` or `Startup.cs` with its own DI container
- [ ] If yes, add the following registration in that file as well:

```csharp
builder.Services.AddSingleton<IWorkerStatusRegistry, WorkerStatusRegistry>();
```

(Or confirm it shares the DI container with `MSOSync.App` — in that case, no duplication is needed.)

---

## Step 10: Add using directives to Scheduler workers

- [ ] For each of `HeartbeatWorker.cs`, `ProbeWorker.cs`, `DecommissionWorker.cs`, add the correct using at the top of the file:

```csharp
using MSOSync.App.Workers;    // if Step 2 chose Option A
// OR
using MSOSync.Common.Workers; // if Step 2 chose Option B
```

---

## Step 11: Build the solution

- [ ] Run `dotnet build MSOSync.sln`
- [ ] Expect 0 errors. If there are "CS0246: The type or namespace name 'IWorkerStatusRegistry' could not be found" errors in Scheduler workers, the project reference from Step 2 was not added correctly — add it and rebuild.

---

## Step 12: Write and run integration test

- [ ] Open (or create) `tests/MSOSync.AppTests/Workers/WorkerRegistryIntegrationTests.cs`
- [ ] Add the following test:

```csharp
using MediatR;
using MSOSync.App.Workers;
using NSubstitute;
using Xunit;

namespace MSOSync.AppTests.Workers;

public sealed class WorkerRegistryIntegrationTests
{
    [Fact]
    public void GetAll_After_RegisteringTwoWorkers_ReturnsBoth()
    {
        var publisher = Substitute.For<IPublisher>();
        var registry = new WorkerStatusRegistry(publisher);

        registry.Register("WorkerAlpha", TimeSpan.FromSeconds(10));
        registry.Register("WorkerBeta", TimeSpan.FromMinutes(5));

        var all = registry.GetAll();

        Assert.Equal(2, all.Length);
        Assert.Contains(all, w => w.WorkerName == "WorkerAlpha" &&
                                   w.ExpectedInterval == TimeSpan.FromSeconds(10));
        Assert.Contains(all, w => w.WorkerName == "WorkerBeta" &&
                                   w.ExpectedInterval == TimeSpan.FromMinutes(5));
    }
}
```

- [ ] Run `dotnet test tests/MSOSync.AppTests/MSOSync.AppTests.csproj` — expect the integration test to pass alongside the unit tests from Task 6.

---

## Acceptance criteria

- All 6 workers compile with `IWorkerStatusRegistry` injected
- `GetAll()` returns an entry for every worker after startup
- No circular project reference is introduced
- The integration test passes: registering 2 workers → both visible in `GetAll()`
- `dotnet build MSOSync.sln` produces 0 errors
