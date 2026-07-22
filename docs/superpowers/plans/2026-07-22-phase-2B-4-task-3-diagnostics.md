# Task 3 — Cluster Diagnostics

Part of [Phase 2B.4 Master Plan](2026-07-22-phase-2B-4-master.md). Deliver `ClusterDiagnosticsQueryService`, a new `GET /api/v1/cluster/diagnostics` endpoint on `ClusterController`, and `ClusterDiagnosticsPage.tsx`.

## Files

**Create (backend):**
- `src/MSOSync.Metadata/Operations/Cluster/Diagnostics/IClusterDiagnosticsQueryService.cs`
- `src/MSOSync.Metadata/Operations/Cluster/Diagnostics/Dtos/ClusterDiagnosticsDto.cs`
- `src/MSOSync.Metadata/Operations/Cluster/Diagnostics/ClusterDiagnosticsQueryService.cs`

**Modify (backend):**
- `src/MSOSync.Api/Controllers/ClusterController.cs` — add `IClusterDiagnosticsQueryService` param + new endpoint
- `src/MSOSync.Metadata/MetadataServiceExtensions.cs` — register service

**Create (tests):**
- `tests/MSOSync.MetadataTests/Operations/ClusterDiagnosticsQueryServiceTests.cs`

**Modify (frontend):**
- `src/MSOSync.Frontend/src/shared/types/cluster.ts` — add diagnostics DTOs
- `src/MSOSync.Frontend/src/shared/api/cluster.ts` — add `clusterKeys.diagnostics`, `getClusterDiagnostics()`
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add nav entry + icon import
- `src/MSOSync.Frontend/src/app/router.tsx` — add route + import

**Create (frontend):**
- `src/MSOSync.Frontend/src/shared/hooks/useClusterDiagnostics.ts`
- `src/MSOSync.Frontend/src/features/operations/cluster/ClusterDiagnosticsPage.tsx`
- `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/ClusterDiagnosticsPage.test.tsx`

## Key Entity Notes

- `SyncLock` is `[GlobalEntity]` — no tenant filter. `LockOwner != null && LockTime != null` = active lock.
- `SyncRuntimeStats` is `[TenantScoped]`. Returns empty list gracefully when table is empty.
- `SyncOperation.Status` strings: `"Running"`, `"Pending"`, `"Completed"`, `"Failed"`.
- `SyncRuntimeStats.HeapUsed`/`HeapMax` = bytes (long?); divide by 1,048,576 to get MB.
- `SyncRuntimeStats.UptimeMs` = milliseconds (long?); divide by 3,600,000 to get hours.
- `SyncRuntimeStats.CpuPercent` = decimal?; cast to double for DTO.
- `SyncRuntimeStats.CreateTime` = DateTime? (nullable); fall back to `DateTime.UtcNow` if null.

## Interfaces

**Produces (consumed by Task 4):**
```csharp
Task<ClusterDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct);
```

---

- [ ] **Step 1: Write failing unit tests**

Create `tests/MSOSync.MetadataTests/Operations/ClusterDiagnosticsQueryServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Diagnostics;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using FluentAssertions;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class ClusterDiagnosticsQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ClusterDiagnosticsQueryService _svc;

    public ClusterDiagnosticsQueryServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts);
        _svc = new ClusterDiagnosticsQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetDiagnosticsAsync_EmptyDb_ReturnsEmptyListsWithoutError()
    {
        var result = await _svc.GetDiagnosticsAsync(default);

        result.RuntimeStats.Should().BeEmpty();
        result.ActiveLocks.Should().BeEmpty();
        result.SlowOperations.Should().BeEmpty();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_RuntimeStats_ConvertsBytesToMb()
    {
        var tenantId = Guid.NewGuid();
        _db.Set<SyncRuntimeStats>().Add(new SyncRuntimeStats
        {
            StatId      = 1,
            HeapUsed    = 104_857_600L, // 100 MB
            HeapMax     = 524_288_000L, // 500 MB
            CpuPercent  = 25.5m,
            ThreadCount = 40,
            GcCount     = 1234L,
            UptimeMs    = 7_200_000L,   // 2 hours
            CreateTime  = DateTime.UtcNow,
            TenantId    = tenantId,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.RuntimeStats.Should().HaveCount(1);
        var s = result.RuntimeStats[0];
        s.HeapUsedMb.Should().BeApproximately(100.0, 0.01);
        s.HeapMaxMb.Should().BeApproximately(500.0, 0.01);
        s.CpuPercent.Should().BeApproximately(25.5, 0.01);
        s.UptimeHours.Should().BeApproximately(2.0, 0.01);
        s.ThreadCount.Should().Be(40);
        s.GcCount.Should().Be(1234L);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_RuntimeStats_LimitedTo50MostRecent()
    {
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 60; i++)
        {
            _db.Set<SyncRuntimeStats>().Add(new SyncRuntimeStats
            {
                StatId     = i + 1,
                CreateTime = DateTime.UtcNow.AddMinutes(-i),
                TenantId   = tenantId,
            });
        }
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.RuntimeStats.Should().HaveCount(50);
        // Most recent first
        result.RuntimeStats[0].StatId.Should().Be(1);
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ActiveLock_IsStale_WhenOlderThan5Minutes()
    {
        _db.Set<SyncLock>().Add(new SyncLock
        {
            LockName  = "sync-lock-1",
            LockOwner = "worker-a",
            LockTime  = DateTime.UtcNow.AddMinutes(-10), // 10 min old = stale
            Scope     = LockScope.Platform,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.ActiveLocks.Should().HaveCount(1);
        result.ActiveLocks[0].LockName.Should().Be("sync-lock-1");
        result.ActiveLocks[0].LockOwner.Should().Be("worker-a");
        result.ActiveLocks[0].AgeSeconds.Should().BeGreaterThan(300);
        result.ActiveLocks[0].IsStale.Should().BeTrue();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_ActiveLock_IsNotStale_WhenFresh()
    {
        _db.Set<SyncLock>().Add(new SyncLock
        {
            LockName  = "fresh-lock",
            LockOwner = "worker-b",
            LockTime  = DateTime.UtcNow.AddSeconds(-30),
            Scope     = LockScope.Platform,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.ActiveLocks[0].IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SlowOps_OnlyRunningAndPending()
    {
        var tenantId = Guid.NewGuid();
        _db.Operations.AddRange(
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export", Status = "Running",   StartedAt = DateTime.UtcNow.AddMinutes(-5), TenantId = tenantId },
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Rollout", Status = "Pending",  StartedAt = DateTime.UtcNow.AddMinutes(-2), TenantId = tenantId },
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export", Status = "Completed", StartedAt = DateTime.UtcNow.AddMinutes(-1), TenantId = tenantId },
            new SyncOperation { OperationId = Guid.NewGuid(), OperationType = "Export", Status = "Failed",    StartedAt = DateTime.UtcNow.AddMinutes(-1), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.SlowOperations.Should().HaveCount(2);
        result.SlowOperations.Should().AllSatisfy(op =>
            (op.Status == "Running" || op.Status == "Pending").Should().BeTrue());
    }

    [Fact]
    public async Task GetDiagnosticsAsync_SlowOps_LimitedTo20OrderedByStartedAtAsc()
    {
        var tenantId = Guid.NewGuid();
        for (var i = 0; i < 25; i++)
        {
            _db.Operations.Add(new SyncOperation
            {
                OperationId   = Guid.NewGuid(),
                OperationType = "Export",
                Status        = "Running",
                StartedAt     = DateTime.UtcNow.AddMinutes(-(25 - i)),
                TenantId      = tenantId,
            });
        }
        await _db.SaveChangesAsync();

        var result = await _svc.GetDiagnosticsAsync(default);

        result.SlowOperations.Should().HaveCount(20);
        // Ordered by StartedAt ASC = oldest first (longest running)
        result.SlowOperations.Should().BeInAscendingOrder(op => op.DurationMinutes, because: "oldest = longest running");
    }
}
```

- [ ] **Step 2: Run tests — expect failure (class not found)**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "ClusterDiagnosticsQueryServiceTests" --no-build 2>&1 | Select-String -Pattern "FAILED|PASSED|Error"
```

Expected: compile error — `ClusterDiagnosticsQueryService` not found.

- [ ] **Step 3: Create DTOs**

Create `src/MSOSync.Metadata/Operations/Cluster/Diagnostics/Dtos/ClusterDiagnosticsDto.cs`:

```csharp
namespace MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;

public sealed record ClusterDiagnosticsDto(
    IReadOnlyList<RuntimeStatsDto>   RuntimeStats,
    IReadOnlyList<ActiveLockDto>     ActiveLocks,
    IReadOnlyList<SlowOperationDto>  SlowOperations);

public sealed record RuntimeStatsDto(
    long      StatId,
    double?   HeapUsedMb,
    double?   HeapMaxMb,
    double?   CpuPercent,
    int?      ThreadCount,
    long?     GcCount,
    double?   UptimeHours,
    DateTime  CapturedAt);

public sealed record ActiveLockDto(
    string LockName,
    string LockOwner,
    double AgeSeconds,
    bool   IsStale);

public sealed record SlowOperationDto(
    Guid    OperationId,
    string  OperationType,
    string  Status,
    double  DurationMinutes,
    int?    ProgressPercent);
```

- [ ] **Step 4: Create interface**

Create `src/MSOSync.Metadata/Operations/Cluster/Diagnostics/IClusterDiagnosticsQueryService.cs`:

```csharp
using MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;

namespace MSOSync.Metadata.Operations.Cluster.Diagnostics;

public interface IClusterDiagnosticsQueryService
{
    Task<ClusterDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Implement service**

Create `src/MSOSync.Metadata/Operations/Cluster/Diagnostics/ClusterDiagnosticsQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Cluster.Diagnostics;

public sealed class ClusterDiagnosticsQueryService(AppDbContext db) : IClusterDiagnosticsQueryService
{
    private const double MbFactor    = 1.0 / 1_048_576;
    private const double HoursFactor = 1.0 / 3_600_000;

    public async Task<ClusterDiagnosticsDto> GetDiagnosticsAsync(CancellationToken ct)
    {
        var rawStats = await db.Set<SyncRuntimeStats>()
            .AsNoTracking()
            .OrderByDescending(s => s.CreateTime)
            .Take(50)
            .Select(s => new
            {
                s.StatId, s.HeapUsed, s.HeapMax, s.CpuPercent,
                s.ThreadCount, s.GcCount, s.UptimeMs, s.CreateTime,
            })
            .ToListAsync(ct);

        var stats = rawStats.Select(s => new RuntimeStatsDto(
            s.StatId,
            s.HeapUsed  is not null ? s.HeapUsed.Value  * MbFactor   : null,
            s.HeapMax   is not null ? s.HeapMax.Value   * MbFactor   : null,
            s.CpuPercent is not null ? (double)s.CpuPercent.Value    : null,
            s.ThreadCount,
            s.GcCount,
            s.UptimeMs  is not null ? s.UptimeMs.Value  * HoursFactor : null,
            s.CreateTime ?? DateTime.UtcNow)).ToList();

        // SyncLock is [GlobalEntity] — no tenant filter
        var rawLocks = await db.Set<SyncLock>()
            .AsNoTracking()
            .Where(l => l.LockOwner != null && l.LockTime != null)
            .OrderBy(l => l.LockTime)
            .Select(l => new { l.LockName, l.LockOwner, l.LockTime })
            .ToListAsync(ct);

        var now   = DateTime.UtcNow;
        var locks = rawLocks.Select(l =>
        {
            var age = (now - l.LockTime!.Value).TotalSeconds;
            return new ActiveLockDto(l.LockName, l.LockOwner!, age, age > 300);
        }).ToList();

        var rawOps = await db.Operations
            .AsNoTracking()
            .Where(op => op.Status == "Running" || op.Status == "Pending")
            .OrderBy(op => op.StartedAt)
            .Take(20)
            .Select(op => new
            {
                op.OperationId, op.OperationType, op.Status, op.StartedAt, op.ProgressPercent,
            })
            .ToListAsync(ct);

        var slowOps = rawOps.Select(op => new SlowOperationDto(
            op.OperationId,
            op.OperationType,
            op.Status,
            Math.Round((now - op.StartedAt).TotalMinutes, 2),
            op.ProgressPercent)).ToList();

        return new ClusterDiagnosticsDto(stats, locks, slowOps);
    }
}
```

- [ ] **Step 6: Run tests — expect pass**

```powershell
dotnet build D:\MSOSync\src\MSOSync.Metadata\MSOSync.Metadata.csproj
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "ClusterDiagnosticsQueryServiceTests"
```

Expected: all 6 tests pass.

- [ ] **Step 7: Extend ClusterController**

Open `src/MSOSync.Api/Controllers/ClusterController.cs`. Add `IClusterDiagnosticsQueryService` to the primary constructor and the new endpoint. Full file (adjust based on what Tasks 1–2 may have already added):

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Cluster;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Cluster.Dtos;
using MSOSync.Metadata.Operations.Cluster.Diagnostics;
using MSOSync.Metadata.Operations.Cluster.Diagnostics.Dtos;
using MSOSync.Metadata.Operations.Cluster.HealthTrends;
using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;
using MSOSync.Metadata.Operations.Cluster.Recovery;
using MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ClusterController(
    IClusterSummaryQueryService        summary,
    IClusterHealthTrendService         healthTrends,
    IValidator<GetHealthTrendsRequest> healthTrendsValidator,
    IRecoveryDashboardQueryService     recovery,
    IClusterDiagnosticsQueryService    diagnostics) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ClusterSummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await summary.GetSummaryAsync(ct));

    [HttpGet("health-trends")]
    [ProducesResponseType(typeof(ClusterHealthTrendDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetHealthTrends([FromQuery] GetHealthTrendsRequest req, CancellationToken ct)
    {
        var validation = await healthTrendsValidator.ValidateAsync(req, ct);
        if (!validation.IsValid)
            return ValidationProblem(validation.ToDictionary());

        return Ok(await healthTrends.GetTrendsAsync(req.Window, req.NodeId, ct));
    }

    [HttpGet("recovery")]
    [ProducesResponseType(typeof(RecoveryDashboardDto), 200)]
    public async Task<IActionResult> GetRecovery(CancellationToken ct)
        => Ok(await recovery.GetRecoveryDashboardAsync(ct));

    [HttpGet("diagnostics")]
    [ProducesResponseType(typeof(ClusterDiagnosticsDto), 200)]
    public async Task<IActionResult> GetDiagnostics(CancellationToken ct)
        => Ok(await diagnostics.GetDiagnosticsAsync(ct));
}
```

**Note:** If Tasks 1 and/or 2 were not completed, the missing services' using directives and constructor params will not exist. Add only what exists plus the new `IClusterDiagnosticsQueryService` parameter.

- [ ] **Step 8: Register in DI**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`. In the Phase 2B.4 block:

```csharp
// Phase 2B.4 — Cluster Health, Recovery, Diagnostics
services.AddScoped<IClusterHealthTrendService,      ClusterHealthTrendService>();      // Task 1
services.AddScoped<IRecoveryDashboardQueryService,  RecoveryDashboardQueryService>();  // Task 2
services.AddScoped<IClusterDiagnosticsQueryService, ClusterDiagnosticsQueryService>(); // Task 3
```

Add namespace using:
```csharp
using MSOSync.Metadata.Operations.Cluster.Diagnostics;
```

- [ ] **Step 9: Build backend**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: 0 errors.

- [ ] **Step 10: Add TypeScript types**

Open `src/MSOSync.Frontend/src/shared/types/cluster.ts`. Append:

```typescript
// Phase 2B.4 — Cluster Diagnostics
export interface RuntimeStatsDto {
  statId: number;
  heapUsedMb: number | null;
  heapMaxMb: number | null;
  cpuPercent: number | null;
  threadCount: number | null;
  gcCount: number | null;
  uptimeHours: number | null;
  capturedAt: string;
}

export interface ActiveLockDto {
  lockName: string;
  lockOwner: string;
  ageSeconds: number;
  isStale: boolean;
}

export interface SlowOperationDto {
  operationId: string;
  operationType: string;
  status: string;
  durationMinutes: number;
  progressPercent: number | null;
}

export interface ClusterDiagnosticsDto {
  runtimeStats: RuntimeStatsDto[];
  activeLocks: ActiveLockDto[];
  slowOperations: SlowOperationDto[];
}
```

- [ ] **Step 11: Extend API module**

Open `src/MSOSync.Frontend/src/shared/api/cluster.ts`. Add `diagnostics` key to `clusterKeys` and a new function:

```typescript
import type { ..., ClusterDiagnosticsDto } from '../types/cluster';

// Add to clusterKeys:
diagnostics: ['cluster', 'diagnostics'] as const,

// Add function:
export async function getClusterDiagnostics(options?: { signal?: AbortSignal }): Promise<ClusterDiagnosticsDto> {
  const { data } = await client.get<ClusterDiagnosticsDto>('/cluster/diagnostics', options);
  return data;
}
```

- [ ] **Step 12: Create hook**

Create `src/MSOSync.Frontend/src/shared/hooks/useClusterDiagnostics.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { clusterKeys, getClusterDiagnostics } from '../api/cluster';

export function useClusterDiagnostics() {
  return useQuery({
    queryKey:        clusterKeys.diagnostics,
    queryFn:         ({ signal }) => getClusterDiagnostics({ signal }),
    staleTime:       10_000,
    gcTime:          60_000,
    refetchInterval: 15_000,
  });
}
```

- [ ] **Step 13: Create ClusterDiagnosticsPage**

Create `src/MSOSync.Frontend/src/features/operations/cluster/ClusterDiagnosticsPage.tsx`:

```tsx
import { useState } from 'react';
import { useClusterDiagnostics } from '@/shared/hooks/useClusterDiagnostics';
import type { RuntimeStatsDto, ActiveLockDto, SlowOperationDto } from '@/shared/types/cluster';

function Panel({ title, children }: { title: string; children: React.ReactNode }) {
  const [open, setOpen] = useState(true);
  return (
    <div className="rounded-lg border bg-card">
      <button
        onClick={() => setOpen(!open)}
        className="w-full flex items-center justify-between p-4 text-left"
      >
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">{title}</h2>
        <span className="text-xs text-muted-foreground">{open ? '▲' : '▼'}</span>
      </button>
      {open && <div className="px-4 pb-4">{children}</div>}
    </div>
  );
}

function ProgressBar({ used, max }: { used: number | null; max: number | null }) {
  if (!used || !max || max === 0) return null;
  const pct = Math.min((used / max) * 100, 100);
  const color = pct > 90 ? 'bg-red-500' : pct > 70 ? 'bg-yellow-500' : 'bg-green-500';
  return (
    <div className="w-full h-1.5 bg-muted rounded-full overflow-hidden">
      <div className={`h-full rounded-full ${color}`} style={{ width: `${pct}%` }} />
    </div>
  );
}

export default function ClusterDiagnosticsPage() {
  const { data, isLoading, error } = useClusterDiagnostics();

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Loading diagnostics…</div>;
  if (error || !data) return <div className="p-6 text-sm text-destructive">Failed to load diagnostics.</div>;

  const { runtimeStats, activeLocks, slowOperations } = data;
  const latest = runtimeStats[0];

  return (
    <div className="p-6 space-y-4">
      <h1 className="text-2xl font-semibold">Cluster Diagnostics</h1>

      {/* Summary cards from latest stats */}
      {latest && (
        <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">Heap Used</p>
            <p className="font-semibold">{latest.heapUsedMb !== null ? `${latest.heapUsedMb.toFixed(1)} MB` : '—'}</p>
            <ProgressBar used={latest.heapUsedMb} max={latest.heapMaxMb} />
          </div>
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">CPU</p>
            <p className="font-semibold">{latest.cpuPercent !== null ? `${latest.cpuPercent.toFixed(1)}%` : '—'}</p>
          </div>
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">Threads</p>
            <p className="font-semibold">{latest.threadCount ?? '—'}</p>
          </div>
          <div className="rounded-lg border bg-card p-3 space-y-1">
            <p className="text-xs text-muted-foreground">Uptime (h)</p>
            <p className="font-semibold">{latest.uptimeHours !== null ? latest.uptimeHours.toFixed(2) : '—'}</p>
          </div>
        </div>
      )}

      <Panel title={`Runtime Stats (last ${runtimeStats.length})`}>
        {runtimeStats.length === 0 ? (
          <p className="text-sm text-muted-foreground">No runtime stats available.</p>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-xs">
              <thead>
                <tr className="text-left text-muted-foreground border-b">
                  <th className="pb-2 font-medium">Captured</th>
                  <th className="pb-2 font-medium">Heap (MB)</th>
                  <th className="pb-2 font-medium">CPU %</th>
                  <th className="pb-2 font-medium">Threads</th>
                  <th className="pb-2 font-medium">GC Count</th>
                  <th className="pb-2 font-medium">Uptime (h)</th>
                </tr>
              </thead>
              <tbody>
                {runtimeStats.map((s: RuntimeStatsDto) => (
                  <tr key={s.statId} className="border-b last:border-0">
                    <td className="py-1.5">{new Date(s.capturedAt).toLocaleTimeString()}</td>
                    <td className="py-1.5">{s.heapUsedMb !== null ? `${s.heapUsedMb.toFixed(1)} / ${s.heapMaxMb?.toFixed(1) ?? '?'}` : '—'}</td>
                    <td className="py-1.5">{s.cpuPercent !== null ? `${s.cpuPercent.toFixed(1)}%` : '—'}</td>
                    <td className="py-1.5">{s.threadCount ?? '—'}</td>
                    <td className="py-1.5">{s.gcCount ?? '—'}</td>
                    <td className="py-1.5">{s.uptimeHours !== null ? s.uptimeHours.toFixed(2) : '—'}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </Panel>

      <Panel title={`Active Locks (${activeLocks.length})`}>
        {activeLocks.length === 0 ? (
          <p className="text-sm text-muted-foreground">No active locks.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Lock Name</th>
                <th className="pb-2 font-medium">Owner</th>
                <th className="pb-2 font-medium">Age (s)</th>
                <th className="pb-2 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {activeLocks.map((l: ActiveLockDto) => (
                <tr key={l.lockName} className={`border-b last:border-0 ${l.isStale ? 'bg-red-50 dark:bg-red-950/20' : ''}`}>
                  <td className="py-2 font-mono text-xs">{l.lockName}</td>
                  <td className="py-2 text-xs">{l.lockOwner}</td>
                  <td className="py-2">{l.ageSeconds.toFixed(0)}</td>
                  <td className="py-2">
                    {l.isStale
                      ? <span className="inline-flex rounded px-2 py-0.5 text-xs font-medium bg-red-100 text-red-800">Stale</span>
                      : <span className="inline-flex rounded px-2 py-0.5 text-xs font-medium bg-green-100 text-green-800">Active</span>}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>

      <Panel title={`Slow Operations (${slowOperations.length})`}>
        {slowOperations.length === 0 ? (
          <p className="text-sm text-muted-foreground">No running or pending operations.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Type</th>
                <th className="pb-2 font-medium">Status</th>
                <th className="pb-2 font-medium">Duration (min)</th>
                <th className="pb-2 font-medium">Progress</th>
              </tr>
            </thead>
            <tbody>
              {slowOperations.map((op: SlowOperationDto) => (
                <tr key={op.operationId} className="border-b last:border-0">
                  <td className="py-2">{op.operationType}</td>
                  <td className="py-2">
                    <span className={`inline-flex rounded px-2 py-0.5 text-xs font-medium ${
                      op.status === 'Running' ? 'bg-blue-100 text-blue-800' : 'bg-yellow-100 text-yellow-800'
                    }`}>{op.status}</span>
                  </td>
                  <td className="py-2 font-semibold">{op.durationMinutes.toFixed(1)}</td>
                  <td className="py-2">
                    {op.progressPercent !== null ? (
                      <div className="flex items-center gap-2">
                        <div className="w-20 h-1.5 bg-muted rounded-full overflow-hidden">
                          <div className="h-full bg-blue-500 rounded-full" style={{ width: `${op.progressPercent}%` }} />
                        </div>
                        <span className="text-xs">{op.progressPercent}%</span>
                      </div>
                    ) : '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </Panel>
    </div>
  );
}
```

- [ ] **Step 14: Add nav entry and route**

Open `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`.

Add `Gauge` to the lucide-react import:
```typescript
import { ..., Gauge } from 'lucide-react';
```

In `NAV_GROUPS` Operations group, add after `Recovery` (or after `Cluster` if Tasks 1–2 not done):
```typescript
{ label: 'Diagnostics', path: '/operations/cluster/diagnostics', icon: Gauge },
```

Open `src/MSOSync.Frontend/src/app/router.tsx`. Add import:
```typescript
import ClusterDiagnosticsPage from '../features/operations/cluster/ClusterDiagnosticsPage';
```

Add route:
```typescript
{ path: 'operations/cluster/diagnostics', element: <ClusterDiagnosticsPage /> },
```

- [ ] **Step 15: Write frontend unit tests**

Create `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/ClusterDiagnosticsPage.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import ClusterDiagnosticsPage from '../ClusterDiagnosticsPage';

vi.mock('@/shared/hooks/useClusterDiagnostics', () => ({
  useClusterDiagnostics: vi.fn(),
}));

import { useClusterDiagnostics } from '@/shared/hooks/useClusterDiagnostics';

const emptyData = { runtimeStats: [], activeLocks: [], slowOperations: [] };

describe('ClusterDiagnosticsPage', () => {
  it('renders empty states for all panels', () => {
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data: emptyData, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText(/no runtime stats available/i)).toBeTruthy();
    expect(screen.getByText(/no active locks/i)).toBeTruthy();
    expect(screen.getByText(/no running or pending operations/i)).toBeTruthy();
  });

  it('renders stale lock row highlighted', () => {
    const data = {
      ...emptyData,
      activeLocks: [{ lockName: 'stale-lock', lockOwner: 'w1', ageSeconds: 400, isStale: true }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText('Stale')).toBeTruthy();
    expect(screen.getByText('stale-lock')).toBeTruthy();
  });

  it('renders fresh lock as Active', () => {
    const data = {
      ...emptyData,
      activeLocks: [{ lockName: 'fresh-lock', lockOwner: 'w2', ageSeconds: 30, isStale: false }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText('Active')).toBeTruthy();
  });

  it('renders slow op progress bar', () => {
    const data = {
      ...emptyData,
      slowOperations: [{ operationId: 'op-1', operationType: 'Export', status: 'Running', durationMinutes: 12.5, progressPercent: 60 }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText('Export')).toBeTruthy();
    expect(screen.getByText('60%')).toBeTruthy();
  });

  it('renders runtime stats summary card with latest entry', () => {
    const data = {
      ...emptyData,
      runtimeStats: [{ statId: 1, heapUsedMb: 256, heapMaxMb: 512, cpuPercent: 33.3, threadCount: 50, gcCount: 100, uptimeHours: 4.5, capturedAt: new Date().toISOString() }],
    };
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText(/256\.0 MB/)).toBeTruthy();
  });

  it('renders error state', () => {
    (useClusterDiagnostics as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') });
    render(<ClusterDiagnosticsPage />);
    expect(screen.getByText(/failed to load diagnostics/i)).toBeTruthy();
  });
});
```

- [ ] **Step 16: Build and verify**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "ClusterDiagnosticsQueryServiceTests"
cd D:\MSOSync\src\MSOSync.Frontend; npm run build
```

Expected: unit tests pass, 0 TypeScript errors.

- [ ] **Step 17: Commit**

```powershell
git add `
  src/MSOSync.Metadata/Operations/Cluster/Diagnostics/ `
  src/MSOSync.Api/Controllers/ClusterController.cs `
  src/MSOSync.Metadata/MetadataServiceExtensions.cs `
  tests/MSOSync.MetadataTests/Operations/ClusterDiagnosticsQueryServiceTests.cs `
  src/MSOSync.Frontend/src/shared/types/cluster.ts `
  src/MSOSync.Frontend/src/shared/api/cluster.ts `
  src/MSOSync.Frontend/src/shared/hooks/useClusterDiagnostics.ts `
  src/MSOSync.Frontend/src/features/operations/cluster/ClusterDiagnosticsPage.tsx `
  src/MSOSync.Frontend/src/features/operations/cluster/__tests__/ClusterDiagnosticsPage.test.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx `
  src/MSOSync.Frontend/src/app/router.tsx

git commit -m "feat(2B.4-T3): Cluster Diagnostics — service, endpoint, page, unit tests"
```
