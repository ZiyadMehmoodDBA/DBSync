# Epic 12C Task 8: ISystemHealthContributor + WorkerHealthCheck + Health Endpoints + Program.cs Wiring

**Goal:** Build a composable health-check system with per-subsystem contributors, expose ASP.NET Core `/health/live` and `/health/ready` endpoints, and add a `GET /api/v1/system/health` controller action that returns detailed contributor results.

**Prerequisites:** Tasks 6 and 7 must be complete. `IWorkerStatusRegistry` is available as a singleton.

---

## Step 1: Create ISystemHealthContributor.cs and HealthContribution.cs

- [ ] Create file `src/MSOSync.App/Health/ISystemHealthContributor.cs`
- [ ] Paste the following content exactly:

```csharp
namespace MSOSync.App.Health;

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

---

## Step 2: Create WorkerHealthContributor.cs

- [ ] Create file `src/MSOSync.App/Health/WorkerHealthContributor.cs`
- [ ] Paste the following content exactly:

```csharp
using MSOSync.App.Workers;

namespace MSOSync.App.Health;

public sealed class WorkerHealthContributor(IWorkerStatusRegistry registry)
    : ISystemHealthContributor
{
    public string Name => "Workers";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        var workers = registry.GetAll();
        var total = workers.Length;

        if (total == 0)
            return Task.FromResult(new HealthContribution(Name, "Healthy", "No workers registered", null));

        var failedCount = workers.Count(w => w.HealthState == WorkerHealthState.Failed);
        var degradedCount = workers.Count(w =>
            w.HealthState is WorkerHealthState.Warning or WorkerHealthState.Delayed);
        var healthyCount = total - failedCount - degradedCount;

        if (failedCount > 0)
            return Task.FromResult(new HealthContribution(
                Name, "Unhealthy",
                $"{failedCount}/{total} worker(s) failed",
                string.Join("; ", workers
                    .Where(w => w.HealthState == WorkerHealthState.Failed)
                    .Select(w => $"{w.WorkerName}: {w.LastError}"))));

        if (degradedCount > 0)
            return Task.FromResult(new HealthContribution(
                Name, "Degraded",
                $"{degradedCount}/{total} worker(s) degraded, {healthyCount}/{total} healthy",
                null));

        return Task.FromResult(new HealthContribution(
            Name, "Healthy",
            $"{total}/{total} workers healthy",
            null));
    }
}
```

---

## Step 3: Create DatabaseHealthContributor.cs

- [ ] Create file `src/MSOSync.App/Health/DatabaseHealthContributor.cs`
- [ ] Replace `AppDbContext` with the actual EF Core DbContext type used in this project (search for `: DbContext` if unsure — likely `MSOSync.Infrastructure.Persistence.AppDbContext` or similar).
- [ ] Paste the following content exactly (adjust namespace of AppDbContext if needed):

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Infrastructure.Persistence;  // adjust if DbContext is in a different namespace

namespace MSOSync.App.Health;

public sealed class DatabaseHealthContributor(AppDbContext db)
    : ISystemHealthContributor
{
    public string Name => "Database";

    public async Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(3));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var canConnect = await db.Database.CanConnectAsync(cts.Token);
            sw.Stop();

            if (!canConnect)
                return new HealthContribution(Name, "Unhealthy", "Database connection refused", null);

            return new HealthContribution(
                Name, "Healthy",
                $"Database reachable ({sw.ElapsedMilliseconds} ms)",
                null);
        }
        catch (OperationCanceledException)
        {
            return new HealthContribution(Name, "Unhealthy", "Database connection timed out (>3 s)", null);
        }
        catch (Exception ex)
        {
            return new HealthContribution(Name, "Unhealthy", "Database connection failed", ex.Message);
        }
    }
}
```

---

## Step 4: Create ApiHealthContributor.cs

- [ ] Create file `src/MSOSync.App/Health/ApiHealthContributor.cs`
- [ ] Paste the following content exactly:

```csharp
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace MSOSync.App.Health;

public sealed class ApiHealthContributor : ISystemHealthContributor
{
    public string Name => "API";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";
        var uptime = DateTime.UtcNow - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var detail = $"Version: {version} | Runtime: {RuntimeInformation.FrameworkDescription} | " +
                     $"Uptime: {uptime:d\\d\\ hh\\:mm\\:ss}";
        return Task.FromResult(new HealthContribution(Name, "Healthy", "API is running", detail));
    }
}
```

---

## Step 5: Create SignalRHealthContributor.cs

- [ ] Create file `src/MSOSync.App/Health/SignalRHealthContributor.cs`
- [ ] Paste the following content exactly:

```csharp
namespace MSOSync.App.Health;

/// <summary>
/// SignalR health is reported as Healthy when the hub is configured.
/// Active connection counting requires hub tracking and is reserved for a future iteration.
/// </summary>
public sealed class SignalRHealthContributor : ISystemHealthContributor
{
    public string Name => "SignalR";

    public Task<HealthContribution> GetAsync(CancellationToken ct)
        => Task.FromResult(new HealthContribution(
            Name, "Healthy",
            "SignalR hub configured",
            "Active connection count tracking reserved for future iteration"));
}
```

---

## Step 6: Create SystemHealthService.cs

- [ ] Create file `src/MSOSync.App/Health/SystemHealthService.cs`
- [ ] Paste the following content exactly:

```csharp
namespace MSOSync.App.Health;

public sealed class SystemHealthService(IEnumerable<ISystemHealthContributor> contributors)
{
    public async Task<HealthContribution[]> GetAllAsync(CancellationToken ct)
    {
        var tasks = contributors
            .Select(c => c.GetAsync(ct))
            .ToArray();

        return await Task.WhenAll(tasks);
    }
}
```

---

## Step 7: Create WorkerHealthCheck.cs (ASP.NET Core IHealthCheck)

- [ ] Create file `src/MSOSync.App/Health/WorkerHealthCheck.cs`
- [ ] Paste the following content exactly:

```csharp
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.App.Workers;

namespace MSOSync.App.Health;

public sealed class WorkerHealthCheck(IWorkerStatusRegistry registry) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var workers = registry.GetAll();

        if (workers.Length == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No workers registered"));

        if (workers.Any(w => w.HealthState == WorkerHealthState.Failed))
            return Task.FromResult(HealthCheckResult.Unhealthy("One or more workers failed"));

        if (workers.Any(w => w.HealthState is WorkerHealthState.Warning or WorkerHealthState.Delayed))
            return Task.FromResult(HealthCheckResult.Degraded("One or more workers degraded"));

        return Task.FromResult(HealthCheckResult.Healthy($"{workers.Length} workers healthy"));
    }
}
```

---

## Step 8: Register all health services in Program.cs

- [ ] Open `src/MSOSync.App/Program.cs`
- [ ] Find the section where services are registered (before `var app = builder.Build()`)
- [ ] Add the following block at the end of the service registration section, after `IWorkerStatusRegistry` is registered:

```csharp
// --- Epic 12C: System Health ---
builder.Services.AddSingleton<SystemHealthService>();
builder.Services.AddSingleton<ISystemHealthContributor, WorkerHealthContributor>();
builder.Services.AddSingleton<ISystemHealthContributor, DatabaseHealthContributor>();
builder.Services.AddSingleton<ISystemHealthContributor, ApiHealthContributor>();
builder.Services.AddSingleton<ISystemHealthContributor, SignalRHealthContributor>();
builder.Services.AddHealthChecks()
    .AddCheck<WorkerHealthCheck>("workers");
```

Note: Check if a `PersistenceHealthCheck` already exists in the codebase. If it does, chain it:
```csharp
builder.Services.AddHealthChecks()
    .AddCheck<PersistenceHealthCheck>("database")
    .AddCheck<WorkerHealthCheck>("workers");
```

- [ ] Add using directives at the top of `Program.cs` if not already present:

```csharp
using MSOSync.App.Health;
```

---

## Step 9: Map health endpoints in Program.cs

- [ ] In `Program.cs`, after `app.MapControllers()` and before any fallback route, add:

```csharp
// Health endpoints
app.MapHealthChecks("/health/live", new Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions
{
    Predicate = _ => false,  // liveness: always returns UP without running checks
    ResponseWriter = async (ctx, _) =>
    {
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsync("{\"status\":\"UP\"}");
    }
});
app.MapHealthChecks("/health/ready");  // readiness: runs all registered IHealthCheck implementations
```

---

## Step 10: Add GET /api/v1/system/health to SystemController

- [ ] Open `src/MSOSync.Api/Controllers/SystemController.cs`
- [ ] Check if `SystemController` already exists. If it does not, create it at that path:

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.App.Health;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class SystemController(
    SystemHealthService healthSvc) : ControllerBase
{
    [HttpGet("health")]
    [ProducesResponseType<HealthContribution[]>(200)]
    public async Task<IActionResult> GetHealthAsync(CancellationToken ct)
        => Ok(await healthSvc.GetAllAsync(ct));
}
```

If `SystemController` already exists with other constructor parameters (e.g., `IOverviewQueryService`), add `SystemHealthService healthSvc` to the existing constructor, and add the `GetHealthAsync` method to the class body.

---

## Step 11: Build the solution

- [ ] Run `dotnet build MSOSync.sln`
- [ ] Expect 0 errors. Common issues:
  - `AppDbContext` namespace wrong in `DatabaseHealthContributor.cs` — fix the `using` directive
  - `OperationsHub` type not found — confirm the hub class name and namespace

---

## Step 12: Write unit tests for WorkerHealthCheck

- [ ] Open (or create) `tests/MSOSync.AppTests/Health/WorkerHealthCheckTests.cs`
- [ ] Paste the following content exactly:

```csharp
using MediatR;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using MSOSync.App.Health;
using MSOSync.App.Workers;
using NSubstitute;
using Xunit;

namespace MSOSync.AppTests.Health;

public sealed class WorkerHealthCheckTests
{
    private static WorkerStatusRegistry CreateRegistryWithWorkers(
        Action<WorkerStatusRegistry> configure,
        out IPublisher publisher)
    {
        publisher = Substitute.For<IPublisher>();
        var registry = new WorkerStatusRegistry(publisher);
        configure(registry);
        return registry;
    }

    // Test 1: All workers healthy => HealthCheckResult.Healthy
    [Fact]
    public async Task CheckHealthAsync_AllWorkersHealthy_ReturnsHealthy()
    {
        var registry = CreateRegistryWithWorkers(r =>
        {
            r.Register("Worker1", TimeSpan.FromSeconds(30));
            r.Register("Worker2", TimeSpan.FromSeconds(60));
            // Complete a tick so each worker is Idle (Healthy)
            r.RecordTickStart("Worker1"); r.RecordTickComplete("Worker1");
            r.RecordTickStart("Worker2"); r.RecordTickComplete("Worker2");
        }, out _);

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    // Test 2: One worker Warning => HealthCheckResult.Degraded
    [Fact]
    public async Task CheckHealthAsync_OneWorkerWarning_ReturnsDegraded()
    {
        var registry = CreateRegistryWithWorkers(r =>
        {
            r.Register("GoodWorker", TimeSpan.FromSeconds(30));
            r.Register("BadWorker", TimeSpan.FromSeconds(30));
            r.RecordTickStart("GoodWorker"); r.RecordTickComplete("GoodWorker");
            // 3 failures = Warning
            for (int i = 0; i < 3; i++)
            {
                r.RecordTickStart("BadWorker");
                r.RecordTickFailed("BadWorker", new Exception("error"));
            }
        }, out _);

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Degraded, result.Status);
    }

    // Test 3: One worker Failed => HealthCheckResult.Unhealthy
    [Fact]
    public async Task CheckHealthAsync_OneWorkerFailed_ReturnsUnhealthy()
    {
        var registry = CreateRegistryWithWorkers(r =>
        {
            r.Register("CriticalWorker", TimeSpan.FromSeconds(30));
            // 5 failures = Failed
            for (int i = 0; i < 5; i++)
            {
                r.RecordTickStart("CriticalWorker");
                r.RecordTickFailed("CriticalWorker", new Exception("fatal"));
            }
        }, out _);

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }

    // Test 4: Empty registry => HealthCheckResult.Healthy
    [Fact]
    public async Task CheckHealthAsync_EmptyRegistry_ReturnsHealthy()
    {
        var registry = CreateRegistryWithWorkers(_ => { }, out _);

        var check = new WorkerHealthCheck(registry);
        var result = await check.CheckHealthAsync(null!);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Contains("No workers", result.Description);
    }
}
```

- [ ] Run `dotnet test tests/MSOSync.AppTests/MSOSync.AppTests.csproj` — expect all 4 health check tests to pass alongside Task 6 tests.

---

## Acceptance criteria

- `/health/live` returns `{"status":"UP"}` with HTTP 200 (no checks run)
- `/health/ready` returns HTTP 200 when all checks pass, HTTP 503 when any check fails
- `GET /api/v1/system/health` returns `HealthContribution[]` with one entry per contributor
- All 4 `WorkerHealthCheckTests` pass
- `dotnet build MSOSync.sln` produces 0 errors
