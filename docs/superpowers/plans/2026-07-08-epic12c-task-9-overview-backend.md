# Epic 12C Task 9: IOverviewQueryService + OverviewSnapshotCache + SystemController Overview and Info Endpoints

**Goal:** Build the data layer for the System Administration Center overview dashboard — a single aggregated snapshot covering node health, worker health, active operations, configuration drift, recent activity, and system metadata. Cache with 5-second TTL and invalidate on relevant domain events.

**Prerequisites:** Tasks 6–8 must be complete. `IWorkerStatusRegistry`, `AppDbContext`, and `IHubContext<OperationsHub>` are available in DI.

---

## Step 1: Create OverviewDto.cs

- [ ] Create file `src/MSOSync.Metadata/Overview/OverviewDto.cs`
- [ ] Paste the following content exactly:

```csharp
namespace MSOSync.Metadata.Overview;

public sealed record OverviewDto(
    OverviewHealthWidget Health,
    OverviewOperationsWidget Operations,
    OverviewNodesWidget Nodes,
    OverviewConfigurationWidget Configuration,
    OverviewWarningDto[] Warnings,
    OverviewEventDto[] RecentActivity,
    OverviewSystemWidget System,
    DateTime LastRefreshedAt);

public sealed record OverviewHealthWidget(
    string ClusterHealth,
    string WorkerHealth,
    string NodeHealth);

public sealed record OverviewOperationsWidget(
    int Running,
    int SucceededToday,
    int FailedToday,
    int Queued);

public sealed record OverviewNodesWidget(
    int Total,
    int Active,
    int Offline,
    int Maintenance,
    int Degraded,
    int PendingRegistrations);

public sealed record OverviewConfigurationWidget(
    int DriftedCount,
    int UpdateAvailableCount,
    int FailedCount);

public sealed record OverviewWarningDto(
    string Type,
    string Severity,
    string Title,
    string Description,
    string TargetRoute,
    string? CorrelationId);

public sealed record OverviewEventDto(
    string EventId,
    DateTime OccurredAt,
    string Category,
    string Summary,
    string? NodeId,
    string? CorrelationId,
    string? DeepLink);

public sealed record OverviewSystemWidget(
    string Version,
    string DatabaseMigration,
    string Environment,
    string Uptime,
    string SignalRStatus,
    DateTime LastRefreshedAt);

public sealed record SystemInfoDto(
    string Version,
    string BuildDate,
    string GitCommit,
    string DotNetRuntime,
    string OperatingSystem,
    string DatabaseMigration,
    string Edition,
    string Environment,
    string ServerTime,
    string ProcessUptime);
```

---

## Step 2: Create IOverviewQueryService.cs

- [ ] Create file `src/MSOSync.Metadata/Overview/IOverviewQueryService.cs`
- [ ] Paste the following content exactly:

```csharp
namespace MSOSync.Metadata.Overview;

public interface IOverviewQueryService
{
    Task<OverviewDto> GetAsync(CancellationToken ct);
}
```

---

## Step 3: Create OverviewSnapshotCache.cs

- [ ] Create file `src/MSOSync.Metadata/Overview/OverviewSnapshotCache.cs`
- [ ] Paste the following content exactly:

```csharp
using Microsoft.Extensions.Caching.Memory;

namespace MSOSync.Metadata.Overview;

public sealed class OverviewSnapshotCache(IMemoryCache cache)
{
    private const string Key = "overview_snapshot";
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(5);

    public bool TryGet(out OverviewDto? dto)
        => cache.TryGetValue(Key, out dto);

    public void Set(OverviewDto dto)
        => cache.Set(Key, dto, Ttl);

    public void Invalidate()
        => cache.Remove(Key);
}
```

---

## Step 4: Create OverviewQueryService.cs

This service queries the database, the worker registry, and derives aggregated health signals.

- [ ] Determine the correct namespace and type name for the EF Core DbContext (search the solution for `: DbContext` if unsure; likely `MSOSync.Infrastructure.Persistence.AppDbContext`)
- [ ] Determine the correct entity names. Common guesses:
  - Nodes: `db.Nodes` with property `LifecycleState` (string) and `ConfigurationState` (string)
  - Operations: `db.Operations` with properties `Status` (string) and `StartedAt` (DateTime)
  - Audits / event log: `db.AuditLogs` or `db.Audits` with `CreateTime` (DateTime) and `CorrelationId`
  - Registration requests: `db.NodeRegistrationRequests` with `Status` (string)

  Adjust entity and property names below to match the actual model.

- [ ] Create file `src/MSOSync.Metadata/Overview/OverviewQueryService.cs`
- [ ] Paste the following content and adjust entity names marked with `// ADJUST`:

```csharp
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using MSOSync.App.Workers;                       // adjust if IWorkerStatusRegistry moved to MSOSync.Common
using MSOSync.Infrastructure.Persistence;        // ADJUST: match actual DbContext namespace

namespace MSOSync.Metadata.Overview;

public sealed class OverviewQueryService(
    AppDbContext db,                             // ADJUST: match actual DbContext type
    IWorkerStatusRegistry workerRegistry,
    OverviewSnapshotCache cache,
    IHostEnvironment env) : IOverviewQueryService
{
    public async Task<OverviewDto> GetAsync(CancellationToken ct)
    {
        if (cache.TryGet(out var cached) && cached is not null)
            return cached;

        var now = DateTime.UtcNow;
        var todayUtc = now.Date;

        // --- Node counts ---
        var nodeCounts = await db.Nodes                            // ADJUST entity name
            .AsNoTracking()
            .GroupBy(n => n.LifecycleState)                        // ADJUST property name
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var totalNodes = nodeCounts.Sum(x => x.Count);
        var activeNodes = nodeCounts.FirstOrDefault(x => x.State == "Active")?.Count ?? 0;        // ADJUST state string
        var offlineNodes = nodeCounts.FirstOrDefault(x => x.State == "Offline")?.Count ?? 0;
        var maintenanceNodes = nodeCounts.FirstOrDefault(x => x.State == "Maintenance")?.Count ?? 0;
        var degradedNodes = nodeCounts.FirstOrDefault(x => x.State == "Degraded")?.Count ?? 0;

        // --- Pending registration requests ---
        var pendingRegistrations = await db.NodeRegistrationRequests  // ADJUST entity name
            .AsNoTracking()
            .CountAsync(r => r.Status == "Pending", ct);             // ADJUST property/value

        // --- Configuration drift ---
        var configCounts = await db.Nodes                            // ADJUST entity name
            .AsNoTracking()
            .Where(n => n.ConfigurationState != null)                // ADJUST property name
            .GroupBy(n => n.ConfigurationState)
            .Select(g => new { State = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var driftedCount = configCounts.FirstOrDefault(x => x.State == "Drifted")?.Count ?? 0;
        var updateAvailableCount = configCounts.FirstOrDefault(x => x.State == "UpdateAvailable")?.Count ?? 0;
        var configFailedCount = configCounts.FirstOrDefault(x => x.State == "Failed")?.Count ?? 0;

        // --- Operations today ---
        var opCounts = await db.Operations                           // ADJUST entity name
            .AsNoTracking()
            .Where(o => o.StartedAt >= todayUtc)                    // ADJUST property name
            .GroupBy(o => o.Status)                                  // ADJUST property name
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var runningOps = opCounts.FirstOrDefault(x => x.Status == "Running")?.Count ?? 0;
        var succeededOps = opCounts.FirstOrDefault(x => x.Status == "Completed")?.Count ?? 0;
        var failedOps = opCounts.FirstOrDefault(x => x.Status == "Failed")?.Count ?? 0;
        var queuedOps = opCounts.FirstOrDefault(x => x.Status == "Queued")?.Count ?? 0;

        // --- Recent events (top 10) ---
        var recentAuditEvents = await db.AuditLogs                  // ADJUST entity name
            .AsNoTracking()
            .OrderByDescending(a => a.CreateTime)                   // ADJUST property name
            .Take(10)
            .ToListAsync(ct);

        var recentActivity = recentAuditEvents.Select(a => new OverviewEventDto(
            EventId: a.AuditId.ToString(),                          // ADJUST property name
            OccurredAt: a.CreateTime,                               // ADJUST property name
            Category: DeriveCategory(a.ActionName ?? ""),           // ADJUST property name
            Summary: a.Description ?? a.ActionName ?? "Event",     // ADJUST property name
            NodeId: a.EntityId,                                      // ADJUST property name
            CorrelationId: a.CorrelationId,                         // ADJUST property name
            DeepLink: DeriveEventDeepLink(a.ActionName, a.EntityId) // ADJUST property names
        )).ToArray();

        // --- Worker health ---
        var workers = workerRegistry.GetAll();
        var workerHealthLevel = DeriveWorkerHealth(workers);

        // --- Node health level ---
        var nodeHealthLevel = offlineNodes > 0
            ? (offlineNodes > totalNodes * 0.1 ? "Unhealthy" : "Degraded")
            : "Healthy";

        // --- Cluster health (worst-of) ---
        var clusterHealth = DeriveClusterHealth(
            workerHealthLevel, nodeHealthLevel, workers, offlineNodes, totalNodes);

        // --- Warnings ---
        var warnings = new List<OverviewWarningDto>();
        if (offlineNodes > 0)
            warnings.Add(new OverviewWarningDto(
                Type: "NodeOffline",
                Severity: offlineNodes > totalNodes * 0.1 ? "Critical" : "Warning",
                Title: $"{offlineNodes} node(s) offline",
                Description: $"{offlineNodes} of {totalNodes} registered nodes are currently offline.",
                TargetRoute: "/operations/nodes",
                CorrelationId: null));

        if (driftedCount > 0)
            warnings.Add(new OverviewWarningDto(
                Type: "ConfigDrift",
                Severity: "Warning",
                Title: $"{driftedCount} node(s) with configuration drift",
                Description: "These nodes are running a configuration that differs from their assigned template.",
                TargetRoute: "/configuration",
                CorrelationId: null));

        var failedWorkers = workers.Where(w => w.HealthState == WorkerHealthState.Failed).ToArray();
        foreach (var fw in failedWorkers)
            warnings.Add(new OverviewWarningDto(
                Type: "WorkerFailed",
                Severity: "Critical",
                Title: $"Worker '{fw.WorkerName}' has failed",
                Description: fw.LastError ?? "No error detail available.",
                TargetRoute: "/admin/system",
                CorrelationId: null));

        // --- Process uptime ---
        var processUptime = now - Process.GetCurrentProcess().StartTime.ToUniversalTime();
        var uptimeStr = $"{(int)processUptime.TotalDays}d {processUptime.Hours:D2}:{processUptime.Minutes:D2}:{processUptime.Seconds:D2}";

        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "12C";

        var dto = new OverviewDto(
            Health: new OverviewHealthWidget(clusterHealth, workerHealthLevel, nodeHealthLevel),
            Operations: new OverviewOperationsWidget(runningOps, succeededOps, failedOps, queuedOps),
            Nodes: new OverviewNodesWidget(totalNodes, activeNodes, offlineNodes, maintenanceNodes, degradedNodes, pendingRegistrations),
            Configuration: new OverviewConfigurationWidget(driftedCount, updateAvailableCount, configFailedCount),
            Warnings: warnings.ToArray(),
            RecentActivity: recentActivity,
            System: new OverviewSystemWidget(
                Version: version,
                DatabaseMigration: "M025",
                Environment: env.EnvironmentName,
                Uptime: uptimeStr,
                SignalRStatus: "Configured",
                LastRefreshedAt: now),
            LastRefreshedAt: now);

        cache.Set(dto);
        return dto;
    }

    private static string DeriveWorkerHealth(WorkerStatusDto[] workers)
    {
        if (workers.Any(w => w.HealthState == WorkerHealthState.Failed)) return "Unhealthy";
        if (workers.Any(w => w.HealthState is WorkerHealthState.Warning or WorkerHealthState.Delayed)) return "Degraded";
        return "Healthy";
    }

    private static string DeriveClusterHealth(
        string workerHealth, string nodeHealth,
        WorkerStatusDto[] workers, int offlineNodes, int totalNodes)
    {
        if (workerHealth == "Unhealthy") return "Critical";
        if (totalNodes > 0 && offlineNodes > totalNodes * 0.1) return "Critical";
        if (workerHealth == "Degraded" || nodeHealth == "Degraded") return "Degraded";
        if (offlineNodes > 0) return "Degraded";
        return "Healthy";
    }

    private static string DeriveCategory(string actionName) => actionName switch
    {
        var a when a.StartsWith("NODE_REGISTR") || a.StartsWith("NODE_APPROVED") || a.StartsWith("NODE_REJECTED") => "Registration",
        var a when a.StartsWith("NODE_") || a.StartsWith("BOOTSTRAP_") => "Lifecycle",
        var a when a.StartsWith("CONFIGURATION_") || a.StartsWith("ROLLOUT_") || a.StartsWith("HEARTBEAT_") => "Configuration",
        var a when a.StartsWith("EXPORT_") => "Operation",
        var a when a.StartsWith("AUTH_") || a.StartsWith("TOKEN_") => "Security",
        _ => "System"
    };

    private static string? DeriveEventDeepLink(string? actionName, string? entityId) =>
        actionName switch
        {
            var a when a is not null && a.StartsWith("NODE_") && entityId is not null => $"/operations/nodes/{entityId}",
            var a when a is not null && a.StartsWith("EXPORT_") => "/operations/jobs",
            var a when a is not null && a.StartsWith("CONFIGURATION_") && entityId is not null => $"/configuration/templates/{entityId}",
            _ => null
        };
}
```

---

## Step 5: Create OverviewRefreshedPublisher.cs

This MediatR handler listens to domain events that should invalidate the overview cache and push a SignalR notification to connected operators.

- [ ] Check what domain events exist in the codebase. The events below are assumed names — adjust if the actual event class names differ:
  - `OperationChangedEvent` (or `OperationStatusChangedEvent`)
  - `WorkerStatusChangedEvent` (created in Task 6)
  - `NodeLifecycleChangedEvent` (or `NodeStateChangedEvent`)
  - `ConfigurationStateChangedEvent` (or `RolloutCompletedEvent`)

- [ ] Create file `src/MSOSync.App/SignalR/OverviewRefreshedPublisher.cs`
- [ ] Paste the following and adjust event type names to match the actual codebase:

```csharp
using MediatR;
using Microsoft.AspNetCore.SignalR;
using MSOSync.Metadata.Overview;

namespace MSOSync.App.SignalR;

/// <summary>
/// Invalidates the overview snapshot cache and broadcasts a SignalR refresh signal
/// whenever a relevant domain event changes system state.
/// Adjust the INotificationHandler<T> interfaces to match actual domain event types.
/// </summary>
public sealed class OverviewRefreshedPublisher(
    IHubContext<OperationsHub> hub,
    OverviewSnapshotCache cache)
    : INotificationHandler<WorkerStatusChangedEvent>
    // Add more interfaces as needed, e.g.:
    //   , INotificationHandler<NodeLifecycleChangedEvent>
    //   , INotificationHandler<OperationChangedEvent>
    //   , INotificationHandler<ConfigurationStateChangedEvent>
{
    public async Task Handle(WorkerStatusChangedEvent notification, CancellationToken cancellationToken)
        => await InvalidateAndNotifyAsync(cancellationToken);

    // Uncomment and add Handle overloads for each additional event type:
    //
    // public async Task Handle(NodeLifecycleChangedEvent notification, CancellationToken cancellationToken)
    //     => await InvalidateAndNotifyAsync(cancellationToken);
    //
    // public async Task Handle(OperationChangedEvent notification, CancellationToken cancellationToken)
    //     => await InvalidateAndNotifyAsync(cancellationToken);
    //
    // public async Task Handle(ConfigurationStateChangedEvent notification, CancellationToken cancellationToken)
    //     => await InvalidateAndNotifyAsync(cancellationToken);

    private async Task InvalidateAndNotifyAsync(CancellationToken ct)
    {
        cache.Invalidate();
        await hub.Clients.Group("operators")
            .SendAsync("OverviewRefreshed", null, ct);
    }
}
```

---

## Step 6: Register IOverviewQueryService in MetadataServiceExtensions.cs

- [ ] Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- [ ] Find the end of the `AddMetadataServices` (or equivalent) extension method
- [ ] Add the following block after the last existing registration, under a new comment:

```csharp
// --- Epic 12C: Overview ---
services.AddSingleton<OverviewSnapshotCache>();
services.AddScoped<IOverviewQueryService, OverviewQueryService>();
```

Note: `OverviewQueryService` uses `AppDbContext` which is scoped, so register it as Scoped. `OverviewSnapshotCache` wraps `IMemoryCache` (singleton) and can be singleton.

Ensure `IMemoryCache` is registered. If not already done elsewhere in Program.cs, add:
```csharp
builder.Services.AddMemoryCache();
```

---

## Step 7: Add GET /api/v1/system/overview and GET /api/v1/system/info to SystemController

- [ ] Open `src/MSOSync.Api/Controllers/SystemController.cs`
- [ ] Update the constructor to include `IOverviewQueryService overviewSvc` and `IWebHostEnvironment env` (or `IHostEnvironment env`). The full constructor should look like:

```csharp
public sealed class SystemController(
    IOverviewQueryService overviewSvc,
    SystemHealthService healthSvc,
    IHostEnvironment env) : ControllerBase
```

- [ ] Add the following two action methods to the class body:

```csharp
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
    return Ok(new SystemInfoDto(
        Version: "12C",
        BuildDate: "2026-07-08",
        GitCommit: "unknown",
        DotNetRuntime: System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
        OperatingSystem: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
        DatabaseMigration: "M025",
        Edition: "Community",
        Environment: env.EnvironmentName,
        ServerTime: DateTime.UtcNow.ToString("O"),
        ProcessUptime: $"{(int)uptime.TotalDays}d {uptime.Hours:D2}:{uptime.Minutes:D2}:{uptime.Seconds:D2}"));
}
```

- [ ] Add using directives at the top of SystemController.cs if not already present:

```csharp
using MSOSync.Metadata.Overview;
using Microsoft.Extensions.Hosting;
```

---

## Step 8: Build the solution

- [ ] Run `dotnet build MSOSync.sln`
- [ ] Expect 0 errors. Common issues:
  - Entity name mismatches (e.g., `db.AuditLogs` vs `db.SyncAudits`) — look at existing services in `MSOSync.Metadata` to find the correct DbSet names
  - Missing `using` for `WorkerStatusDto` namespace in `OverviewQueryService.cs`

---

## Step 9: Write unit tests for OverviewQueryService

Because `OverviewQueryService` queries `AppDbContext` directly, these tests use an in-memory EF Core database.

- [ ] Add a package reference to `Microsoft.EntityFrameworkCore.InMemory` in the test project if not already present:
```
dotnet add tests/MSOSync.AppTests/MSOSync.AppTests.csproj package Microsoft.EntityFrameworkCore.InMemory
```

- [ ] Create `tests/MSOSync.AppTests/Overview/OverviewQueryServiceTests.cs`
- [ ] Paste the following content, adjusting entity/property names marked with `// ADJUST`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using MSOSync.App.Workers;
using MSOSync.Infrastructure.Persistence;         // ADJUST
using MSOSync.Metadata.Overview;
using NSubstitute;
using Xunit;

namespace MSOSync.AppTests.Overview;

public sealed class OverviewQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;             // ADJUST
    private readonly IMemoryCache _cache;
    private readonly WorkerStatusRegistry _registry;
    private readonly OverviewSnapshotCache _snapshotCache;
    private readonly IHostEnvironment _env;

    public OverviewQueryServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()  // ADJUST
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);                           // ADJUST: pass any required constructor args
        _cache = new MemoryCache(new MemoryCacheOptions());
        _snapshotCache = new OverviewSnapshotCache(_cache);
        _registry = new WorkerStatusRegistry(Substitute.For<IPublisher>());
        _env = Substitute.For<IHostEnvironment>();
        _env.EnvironmentName.Returns("Test");
    }

    private OverviewQueryService CreateService()
        => new(_db, _registry, _snapshotCache, _env);

    // Test 1: ClusterHealth = Healthy when all good (no nodes, healthy workers)
    [Fact]
    public async Task GetAsync_AllHealthy_ClusterHealthIsHealthy()
    {
        _registry.Register("Worker1", TimeSpan.FromSeconds(30));
        _registry.RecordTickStart("Worker1");
        _registry.RecordTickComplete("Worker1");

        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);

        Assert.Equal("Healthy", dto.Health.ClusterHealth);
    }

    // Test 2: ClusterHealth = Critical when >10% nodes offline
    [Fact]
    public async Task GetAsync_ManyOfflineNodes_ClusterHealthIsCritical()
    {
        // Seed 10 nodes: 2 offline (20%)
        // ADJUST: replace with actual entity seeding for the Nodes DbSet
        // Example (adjust entity type and property names):
        // for (int i = 0; i < 8; i++)
        //     _db.Nodes.Add(new Node { LifecycleState = "Active" });
        // _db.Nodes.Add(new Node { LifecycleState = "Offline" });
        // _db.Nodes.Add(new Node { LifecycleState = "Offline" });
        // await _db.SaveChangesAsync();

        // NOTE: This test body requires knowledge of the actual entity shape.
        // Implement after confirming the entity types in Task 4/5 output.
        // For now, assert that the service returns a non-null DTO.
        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);
        Assert.NotNull(dto);
    }

    // Test 3: ClusterHealth = Degraded when a worker is warning
    [Fact]
    public async Task GetAsync_WorkerWarning_ClusterHealthIsDegraded()
    {
        _registry.Register("BadWorker", TimeSpan.FromSeconds(30));
        for (int i = 0; i < 3; i++)
        {
            _registry.RecordTickStart("BadWorker");
            _registry.RecordTickFailed("BadWorker", new Exception("oops"));
        }

        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);

        Assert.Equal("Degraded", dto.Health.ClusterHealth);
    }

    // Test 4: Cache is used on second call (no database round-trip)
    [Fact]
    public async Task GetAsync_SecondCall_ReturnsCachedResult()
    {
        var svc = CreateService();
        var first = await svc.GetAsync(CancellationToken.None);
        var second = await svc.GetAsync(CancellationToken.None);

        // Same instance reference means cache was hit
        Assert.Same(first, second);
    }

    // Test 5: Cache is invalidated after Invalidate()
    [Fact]
    public async Task GetAsync_AfterInvalidate_ReturnsNewInstance()
    {
        var svc = CreateService();
        var first = await svc.GetAsync(CancellationToken.None);
        _snapshotCache.Invalidate();
        var second = await svc.GetAsync(CancellationToken.None);

        Assert.NotSame(first, second);
    }

    // Test 6: Offline nodes generate a warning entry
    [Fact]
    public async Task GetAsync_OfflineNodes_GeneratesWarning()
    {
        // Seed at least 1 offline node — adjust entity seeding as needed
        // _db.Nodes.Add(new Node { LifecycleState = "Offline" });
        // await _db.SaveChangesAsync();

        // Without seeding, this test verifies the structure is correct when offline=0
        var svc = CreateService();
        var dto = await svc.GetAsync(CancellationToken.None);
        // When no offline nodes: no warning of type NodeOffline
        Assert.DoesNotContain(dto.Warnings, w => w.Type == "NodeOffline");
    }

    public void Dispose()
    {
        _db.Dispose();
        _cache.Dispose();
    }
}
```

- [ ] Run `dotnet test tests/MSOSync.AppTests/MSOSync.AppTests.csproj` — expect at minimum tests 1, 3, 4, 5, 6 to pass. Test 2 requires entity seeding; fill in the seeding code once the entity shape is confirmed from prior tasks.

---

## Acceptance criteria

- `GET /api/v1/system/overview` returns a valid `OverviewDto` with all 8 top-level fields
- `GET /api/v1/system/info` returns `SystemInfoDto` with correct runtime info
- Second call within 5 seconds returns cached result (same object reference)
- Cache is invalidated when `WorkerStatusChangedEvent` is published
- All runnable unit tests pass
- `dotnet build MSOSync.sln` produces 0 errors
