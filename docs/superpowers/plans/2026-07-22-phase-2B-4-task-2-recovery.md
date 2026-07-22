# Task 2 — Disaster Recovery Dashboard

Part of [Phase 2B.4 Master Plan](2026-07-22-phase-2B-4-master.md). Deliver `RecoveryDashboardQueryService`, a new `GET /api/v1/cluster/recovery` endpoint on `ClusterController`, and `RecoveryDashboardPage.tsx`.

## Files

**Create (backend):**
- `src/MSOSync.Metadata/Operations/Cluster/Recovery/IRecoveryDashboardQueryService.cs`
- `src/MSOSync.Metadata/Operations/Cluster/Recovery/Dtos/RecoveryDashboardDto.cs`
- `src/MSOSync.Metadata/Operations/Cluster/Recovery/RecoveryDashboardQueryService.cs`

**Modify (backend):**
- `src/MSOSync.Api/Controllers/ClusterController.cs` — add `IRecoveryDashboardQueryService` param + new endpoint
- `src/MSOSync.Metadata/MetadataServiceExtensions.cs` — register service

**Create (tests):**
- `tests/MSOSync.MetadataTests/Operations/RecoveryDashboardQueryServiceTests.cs`

**Modify (frontend):**
- `src/MSOSync.Frontend/src/shared/types/cluster.ts` — add recovery DTOs
- `src/MSOSync.Frontend/src/shared/api/cluster.ts` — add `clusterKeys.recovery`, `getRecoveryDashboard()`
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add nav entry + icon import
- `src/MSOSync.Frontend/src/app/router.tsx` — add route + import
- `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts` — invalidate recovery on lifecycle change

**Create (frontend):**
- `src/MSOSync.Frontend/src/shared/hooks/useRecoveryDashboard.ts`
- `src/MSOSync.Frontend/src/features/operations/cluster/RecoveryDashboardPage.tsx`
- `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/RecoveryDashboardPage.test.tsx`

## Interfaces

**Consumes from Task 1 (if done first):** ClusterController already has `IClusterHealthTrendService` param.
If Task 1 has NOT been done, ClusterController still has only `IClusterSummaryQueryService` — add both `IClusterHealthTrendService` and `IRecoveryDashboardQueryService` params if they're missing.

**Produces (consumed by Task 4):**
```csharp
Task<RecoveryDashboardDto> GetRecoveryDashboardAsync(CancellationToken ct);
```

---

- [ ] **Step 1: Write failing unit tests**

Create `tests/MSOSync.MetadataTests/Operations/RecoveryDashboardQueryServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Recovery;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using FluentAssertions;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class RecoveryDashboardQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly RecoveryDashboardQueryService _svc;

    public RecoveryDashboardQueryServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts);
        _svc = new RecoveryDashboardQueryService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetRecoveryDashboardAsync_NoRecoveryNodes_ReturnsEmptyActiveList()
    {
        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.ActiveRecoveries.Should().BeEmpty();
        result.Summary.ActiveCount.Should().Be(0);
        result.Summary.AvgRtoMinutes.Should().BeNull();
        result.Summary.MaxRtoMinutes.Should().BeNull();
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_NodeInRecovery_AppearsInActiveList()
    {
        var tenantId = Guid.NewGuid();
        var recoveryStart = DateTimeOffset.UtcNow.AddMinutes(-30);

        _db.Nodes.Add(new SyncNode
        {
            NodeId         = "rec-node-1",
            GroupId        = "grp",
            SyncUrl        = "http://rec.local",
            LifecycleState = NodeLifecycleState.Recovery,
            TenantId       = tenantId,
        });
        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory
        {
            NodeId     = "rec-node-1",
            FromState  = NodeLifecycleState.Active,
            ToState    = NodeLifecycleState.Recovery,
            Trigger    = LifecycleTrigger.System,
            OccurredAt = recoveryStart,
            TenantId   = tenantId,
        });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.ActiveRecoveries.Should().HaveCount(1);
        result.ActiveRecoveries[0].NodeId.Should().Be("rec-node-1");
        result.ActiveRecoveries[0].RecoveryStartedAt.Should().BeCloseTo(recoveryStart.UtcDateTime, TimeSpan.FromSeconds(1));
        result.ActiveRecoveries[0].ElapsedMinutes.Should().BeGreaterThan(25);
        result.Summary.ActiveCount.Should().Be(1);
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_CompletedRecovery_AppearsInCompleted()
    {
        var tenantId      = Guid.NewGuid();
        var recoveryStart = DateTimeOffset.UtcNow.AddDays(-2);
        var restored      = recoveryStart.AddMinutes(45);

        // A node now in Active, but had Recovery → Active transition recently
        _db.Nodes.Add(new SyncNode
        {
            NodeId         = "rec-node-2",
            GroupId        = "grp",
            SyncUrl        = "http://rec2.local",
            LifecycleState = NodeLifecycleState.Active,
            TenantId       = tenantId,
        });
        _db.NodeLifecycleHistories.AddRange(
            new SyncNodeLifecycleHistory { NodeId = "rec-node-2", FromState = NodeLifecycleState.Active,   ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, OccurredAt = recoveryStart, TenantId = tenantId },
            new SyncNodeLifecycleHistory { NodeId = "rec-node-2", FromState = NodeLifecycleState.Recovery, ToState = NodeLifecycleState.Active,   Trigger = LifecycleTrigger.Manual, OccurredAt = restored,      TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.RecentCompletedRecoveries.Should().HaveCount(1);
        result.RecentCompletedRecoveries[0].NodeId.Should().Be("rec-node-2");
        result.RecentCompletedRecoveries[0].RtoMinutes.Should().BeApproximately(45.0, 1.0);
        result.Summary.AvgRtoMinutes.Should().NotBeNull();
        result.Summary.MaxRtoMinutes.Should().BeApproximately(45.0, 1.0);
        result.Summary.CompletedLast30Days.Should().Be(1);
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_CompletedOlderThan30Days_NotInSummaryCount()
    {
        var tenantId = Guid.NewGuid();
        var recoveryStart = DateTimeOffset.UtcNow.AddDays(-35);
        var restored      = recoveryStart.AddMinutes(60);

        _db.Nodes.Add(new SyncNode { NodeId = "old-rec", GroupId = "grp", SyncUrl = "http://old.local", LifecycleState = NodeLifecycleState.Active, TenantId = tenantId });
        _db.NodeLifecycleHistories.AddRange(
            new SyncNodeLifecycleHistory { NodeId = "old-rec", FromState = NodeLifecycleState.Active,   ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, OccurredAt = recoveryStart, TenantId = tenantId },
            new SyncNodeLifecycleHistory { NodeId = "old-rec", FromState = NodeLifecycleState.Recovery, ToState = NodeLifecycleState.Active,   Trigger = LifecycleTrigger.Manual, OccurredAt = restored,      TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        result.Summary.CompletedLast30Days.Should().Be(0);
        result.RecentCompletedRecoveries.Should().BeEmpty();
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_NoCompletedRecoveries_NullAvgAndMax()
    {
        var result = await _svc.GetRecoveryDashboardAsync(default);
        result.Summary.AvgRtoMinutes.Should().BeNull();
        result.Summary.MaxRtoMinutes.Should().BeNull();
    }

    [Fact]
    public async Task GetRecoveryDashboardAsync_AssociatedReplayOps_LinkedToRecoveryNode()
    {
        var tenantId      = Guid.NewGuid();
        var recoveryStart = DateTimeOffset.UtcNow.AddHours(-2);

        _db.Nodes.Add(new SyncNode { NodeId = "rec-replay", GroupId = "grp", SyncUrl = "http://rr.local", LifecycleState = NodeLifecycleState.Recovery, TenantId = tenantId });
        _db.NodeLifecycleHistories.Add(new SyncNodeLifecycleHistory { NodeId = "rec-replay", FromState = NodeLifecycleState.Active, ToState = NodeLifecycleState.Recovery, Trigger = LifecycleTrigger.System, OccurredAt = recoveryStart, TenantId = tenantId });

        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId   = opId,
            OperationType = "BatchReplay",
            Status        = "Running",
            StartedAt     = recoveryStart.AddMinutes(5).UtcDateTime,
            TenantId      = tenantId,
        });
        _db.ReplayRequests.Add(new SyncReplayRequest { OperationId = opId, NodeId = "rec-replay", ReplayMode = "FailedDelivery", TenantId = tenantId });
        _db.ReplayItems.Add(new SyncReplayItem { OperationId = opId, NodeId = "rec-replay", Status = "Completed", TenantId = tenantId });
        _db.ReplayItems.Add(new SyncReplayItem { OperationId = opId, NodeId = "rec-replay", Status = "Pending",   TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetRecoveryDashboardAsync(default);

        var active = result.ActiveRecoveries.FirstOrDefault(r => r.NodeId == "rec-replay");
        active.Should().NotBeNull();
        active!.AssociatedReplayOps.Should().HaveCount(1);
        active.AssociatedReplayOps[0].OperationId.Should().Be(opId);
        active.AssociatedReplayOps[0].ItemsTotal.Should().Be(2);
        active.AssociatedReplayOps[0].ItemsDone.Should().Be(1);
    }
}
```

- [ ] **Step 2: Run tests — expect failure (class not found)**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "RecoveryDashboardQueryServiceTests" --no-build 2>&1 | Select-String -Pattern "FAILED|PASSED|Error"
```

Expected: compile error — `RecoveryDashboardQueryService` not found.

- [ ] **Step 3: Create DTOs**

Create `src/MSOSync.Metadata/Operations/Cluster/Recovery/Dtos/RecoveryDashboardDto.cs`:

```csharp
namespace MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;

public sealed record RecoveryDashboardDto(
    RecoverySummaryDto                   Summary,
    IReadOnlyList<ActiveRecoveryDto>     ActiveRecoveries,
    IReadOnlyList<CompletedRecoveryDto>  RecentCompletedRecoveries);

public sealed record RecoverySummaryDto(
    int     ActiveCount,
    double? AvgRtoMinutes,
    double? MaxRtoMinutes,
    int     CompletedLast30Days);

public sealed record ActiveRecoveryDto(
    string                        NodeId,
    DateTime?                     FailureDetectedAt,
    DateTime                      RecoveryStartedAt,
    double                        ElapsedMinutes,
    IReadOnlyList<ReplayOpRefDto> AssociatedReplayOps);

public sealed record CompletedRecoveryDto(
    string    NodeId,
    DateTime? FailureDetectedAt,
    DateTime  RecoveryStartedAt,
    DateTime  RestoredAt,
    double    RtoMinutes);

public sealed record ReplayOpRefDto(
    Guid   OperationId,
    string Status,
    int    ItemsDone,
    int    ItemsTotal);
```

- [ ] **Step 4: Create interface**

Create `src/MSOSync.Metadata/Operations/Cluster/Recovery/IRecoveryDashboardQueryService.cs`:

```csharp
using MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;

namespace MSOSync.Metadata.Operations.Cluster.Recovery;

public interface IRecoveryDashboardQueryService
{
    Task<RecoveryDashboardDto> GetRecoveryDashboardAsync(CancellationToken ct);
}
```

- [ ] **Step 5: Implement service**

Create `src/MSOSync.Metadata/Operations/Cluster/Recovery/RecoveryDashboardQueryService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Recovery.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Cluster.Recovery;

public sealed class RecoveryDashboardQueryService(AppDbContext db) : IRecoveryDashboardQueryService
{
    public async Task<RecoveryDashboardDto> GetRecoveryDashboardAsync(CancellationToken ct)
    {
        // 1. Nodes currently in Recovery lifecycle state
        var recoveryNodeIds = await db.Nodes
            .AsNoTracking()
            .Where(n => n.LifecycleState == NodeLifecycleState.Recovery)
            .Select(n => n.NodeId)
            .ToListAsync(ct);

        // 2. Most recent Recovery transition per active-recovery node
        var recoveryEntries = recoveryNodeIds.Any()
            ? await db.NodeLifecycleHistories
                .AsNoTracking()
                .Where(h => recoveryNodeIds.Contains(h.NodeId) && h.ToState == NodeLifecycleState.Recovery)
                .Select(h => new { h.NodeId, h.OccurredAt })
                .ToListAsync(ct)
            : [];

        var recoveryStartMap = recoveryEntries
            .GroupBy(h => h.NodeId)
            .ToDictionary(g => g.Key, g => g.Max(h => h.OccurredAt));

        // 3. FailureDetectedAt — last connectivity degradation before recovery start
        Dictionary<string, DateTimeOffset?> failureMap = new();
        if (recoveryNodeIds.Any())
        {
            var connHistory = await db.Set<SyncNodeConnectivityHistory>()
                .AsNoTracking()
                .Where(h => recoveryNodeIds.Contains(h.NodeId)
                         && (h.NewStatus == ConnectivityStatus.Degraded || h.NewStatus == ConnectivityStatus.Unreachable))
                .Select(h => new { h.NodeId, h.OccurredAt })
                .ToListAsync(ct);

            foreach (var nodeId in recoveryNodeIds)
            {
                var nodeStart = recoveryStartMap.TryGetValue(nodeId, out var rs) ? rs : DateTimeOffset.UtcNow;
                failureMap[nodeId] = connHistory
                    .Where(h => h.NodeId == nodeId && h.OccurredAt <= nodeStart)
                    .OrderByDescending(h => h.OccurredAt)
                    .Select(h => (DateTimeOffset?)h.OccurredAt)
                    .FirstOrDefault();
            }
        }

        // 4. Replay operations for recovery nodes (started after recovery began)
        var replayOpInfos = new List<(Guid OpId, string NodeId, string Status, DateTimeOffset StartedAt)>();
        if (recoveryNodeIds.Any())
        {
            var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
            var replayOps = await db.Operations
                .AsNoTracking()
                .Join(db.ReplayRequests, o => o.OperationId, r => r.OperationId,
                      (o, r) => new { o.OperationId, r.NodeId, o.Status, o.StartedAt })
                .Where(x => recoveryNodeIds.Contains(x.NodeId) && x.StartedAt >= thirtyDaysAgo)
                .ToListAsync(ct);

            replayOpInfos.AddRange(replayOps.Select(x =>
                (x.OperationId, x.NodeId, x.Status, new DateTimeOffset(x.StartedAt, TimeSpan.Zero))));
        }

        var replayOpIds = replayOpInfos.Select(x => x.OpId).ToList();
        var replayItemCounts = replayOpIds.Any()
            ? await db.ReplayItems
                .AsNoTracking()
                .Where(i => replayOpIds.Contains(i.OperationId))
                .GroupBy(i => i.OperationId)
                .Select(g => new
                {
                    OperationId = g.Key,
                    Total = g.Count(),
                    Done  = g.Count(i => i.Status == "Completed"),
                })
                .ToListAsync(ct)
            : [];

        // 5. Build active recovery list
        var utcNow = DateTimeOffset.UtcNow;
        var activeRecoveries = recoveryNodeIds.Select(nodeId =>
        {
            var recoveryStart = recoveryStartMap.TryGetValue(nodeId, out var rs) ? rs : utcNow;
            var failureAt     = failureMap.TryGetValue(nodeId, out var fa) ? fa : null;
            var elapsed       = (utcNow - recoveryStart).TotalMinutes;

            var nodeReplayOps = replayOpInfos
                .Where(x => x.NodeId == nodeId && x.StartedAt >= recoveryStart)
                .Select(x =>
                {
                    var counts = replayItemCounts.FirstOrDefault(c => c.OperationId == x.OpId);
                    return new ReplayOpRefDto(x.OpId, x.Status, counts?.Done ?? 0, counts?.Total ?? 0);
                })
                .ToList();

            return new ActiveRecoveryDto(
                nodeId,
                failureAt?.UtcDateTime,
                recoveryStart.UtcDateTime,
                Math.Round(elapsed, 2),
                nodeReplayOps);
        }).ToList();

        // 6. Completed recoveries (Recovery → Active in last 30 days)
        var thirtyDaysAgoDO  = DateTimeOffset.UtcNow.AddDays(-30);
        var activeTransitions = await db.NodeLifecycleHistories
            .AsNoTracking()
            .Where(h => h.ToState == NodeLifecycleState.Active && h.OccurredAt >= thirtyDaysAgoDO)
            .Select(h => new { h.NodeId, RestoredAt = h.OccurredAt })
            .ToListAsync(ct);

        var recoveredNodeIds = activeTransitions.Select(t => t.NodeId).Distinct().ToList();
        var completionRecoveryEntries = recoveredNodeIds.Any()
            ? await db.NodeLifecycleHistories
                .AsNoTracking()
                .Where(h => recoveredNodeIds.Contains(h.NodeId) && h.ToState == NodeLifecycleState.Recovery)
                .Select(h => new { h.NodeId, RecoveryStartedAt = h.OccurredAt })
                .ToListAsync(ct)
            : [];

        // Connectivity failures for completed recovery nodes
        var completedConnHistory = recoveredNodeIds.Any()
            ? await db.Set<SyncNodeConnectivityHistory>()
                .AsNoTracking()
                .Where(h => recoveredNodeIds.Contains(h.NodeId)
                         && (h.NewStatus == ConnectivityStatus.Degraded || h.NewStatus == ConnectivityStatus.Unreachable))
                .Select(h => new { h.NodeId, h.OccurredAt })
                .ToListAsync(ct)
            : [];

        var completedRecoveries = new List<CompletedRecoveryDto>();
        foreach (var nodeId in recoveredNodeIds)
        {
            // Most recent restoration for this node
            var latestRestore = activeTransitions
                .Where(t => t.NodeId == nodeId)
                .OrderByDescending(t => t.RestoredAt)
                .FirstOrDefault();
            if (latestRestore is null) continue;

            // Most recent Recovery entry before restoration
            var recoveryEntry = completionRecoveryEntries
                .Where(h => h.NodeId == nodeId && h.RecoveryStartedAt <= latestRestore.RestoredAt)
                .OrderByDescending(h => h.RecoveryStartedAt)
                .FirstOrDefault();
            if (recoveryEntry is null) continue;

            var failureAt = completedConnHistory
                .Where(h => h.NodeId == nodeId && h.OccurredAt <= recoveryEntry.RecoveryStartedAt)
                .OrderByDescending(h => h.OccurredAt)
                .Select(h => (DateTimeOffset?)h.OccurredAt)
                .FirstOrDefault();

            var rtoMinutes = (latestRestore.RestoredAt - recoveryEntry.RecoveryStartedAt).TotalMinutes;

            completedRecoveries.Add(new CompletedRecoveryDto(
                nodeId,
                failureAt?.UtcDateTime,
                recoveryEntry.RecoveryStartedAt.UtcDateTime,
                latestRestore.RestoredAt.UtcDateTime,
                Math.Round(rtoMinutes, 2)));
        }

        completedRecoveries = completedRecoveries
            .OrderByDescending(r => r.RestoredAt)
            .Take(50)
            .ToList();

        // 7. Summary
        var completedLast30 = completedRecoveries.Count;
        double? avgRto = completedRecoveries.Any() ? completedRecoveries.Average(r => r.RtoMinutes) : null;
        double? maxRto = completedRecoveries.Any() ? completedRecoveries.Max(r => r.RtoMinutes)     : null;

        return new RecoveryDashboardDto(
            new RecoverySummaryDto(activeRecoveries.Count, avgRto, maxRto, completedLast30),
            activeRecoveries,
            completedRecoveries);
    }
}
```

- [ ] **Step 6: Run tests — expect pass**

```powershell
dotnet build D:\MSOSync\src\MSOSync.Metadata\MSOSync.Metadata.csproj
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "RecoveryDashboardQueryServiceTests"
```

Expected: all 6 tests pass.

- [ ] **Step 7: Extend ClusterController**

Open `src/MSOSync.Api/Controllers/ClusterController.cs`. Add `IRecoveryDashboardQueryService` to the primary constructor and the new endpoint. The full file after this task (adjust based on what Task 1 may have already added):

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Cluster;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Cluster.Dtos;
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
    IRecoveryDashboardQueryService     recovery) : ControllerBase
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
}
```

**Note:** If Task 1 was not completed, `IClusterHealthTrendService`, `GetHealthTrendsRequest`, and `IValidator<GetHealthTrendsRequest>` don't exist yet. In that case, add only `IRecoveryDashboardQueryService` to the existing simple controller and skip the health-trends using directives.

- [ ] **Step 8: Register in DI**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`. In the Phase 2B.4 block (add it if not there yet):

```csharp
// Phase 2B.4 — Cluster Health, Recovery, Diagnostics
services.AddScoped<IClusterHealthTrendService,     ClusterHealthTrendService>();     // Task 1
services.AddScoped<IRecoveryDashboardQueryService, RecoveryDashboardQueryService>(); // Task 2
```

Add namespace using:
```csharp
using MSOSync.Metadata.Operations.Cluster.Recovery;
```

- [ ] **Step 9: Build backend**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: 0 errors.

- [ ] **Step 10: Add TypeScript types**

Open `src/MSOSync.Frontend/src/shared/types/cluster.ts`. Append:

```typescript
// Phase 2B.4 — Recovery Dashboard
export interface ReplayOpRefDto {
  operationId: string;
  status: string;
  itemsDone: number;
  itemsTotal: number;
}

export interface ActiveRecoveryDto {
  nodeId: string;
  failureDetectedAt: string | null;
  recoveryStartedAt: string;
  elapsedMinutes: number;
  associatedReplayOps: ReplayOpRefDto[];
}

export interface CompletedRecoveryDto {
  nodeId: string;
  failureDetectedAt: string | null;
  recoveryStartedAt: string;
  restoredAt: string;
  rtoMinutes: number;
}

export interface RecoverySummaryDto {
  activeCount: number;
  avgRtoMinutes: number | null;
  maxRtoMinutes: number | null;
  completedLast30Days: number;
}

export interface RecoveryDashboardDto {
  summary: RecoverySummaryDto;
  activeRecoveries: ActiveRecoveryDto[];
  recentCompletedRecoveries: CompletedRecoveryDto[];
}
```

- [ ] **Step 11: Extend API module**

Open `src/MSOSync.Frontend/src/shared/api/cluster.ts`. Add `recovery` key to `clusterKeys` and a new function:

```typescript
import type { ClusterSummaryDto, ClusterHealthTrendDto, RecoveryDashboardDto } from '../types/cluster';

// Add to clusterKeys:
recovery: ['cluster', 'recovery'] as const,

// Add function:
export async function getRecoveryDashboard(options?: { signal?: AbortSignal }): Promise<RecoveryDashboardDto> {
  const { data } = await client.get<RecoveryDashboardDto>('/cluster/recovery', options);
  return data;
}
```

- [ ] **Step 12: Create hook**

Create `src/MSOSync.Frontend/src/shared/hooks/useRecoveryDashboard.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { clusterKeys, getRecoveryDashboard } from '../api/cluster';

export function useRecoveryDashboard() {
  return useQuery({
    queryKey:        clusterKeys.recovery,
    queryFn:         ({ signal }) => getRecoveryDashboard({ signal }),
    staleTime:       15_000,
    gcTime:          60_000,
    refetchInterval: 30_000,
  });
}
```

- [ ] **Step 13: Create RecoveryDashboardPage**

Create `src/MSOSync.Frontend/src/features/operations/cluster/RecoveryDashboardPage.tsx`:

```tsx
import { useRecoveryDashboard } from '@/shared/hooks/useRecoveryDashboard';
import type { ActiveRecoveryDto, CompletedRecoveryDto } from '@/shared/types/cluster';

function SummaryCard({ label, value }: { label: string; value: string | number }) {
  return (
    <div className="rounded-lg border bg-card p-4 space-y-1">
      <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">{label}</p>
      <p className="text-3xl font-bold">{value}</p>
    </div>
  );
}

function StatusChip({ status }: { status: string }) {
  const color =
    status === 'Running'   ? 'bg-blue-100 text-blue-800' :
    status === 'Completed' ? 'bg-green-100 text-green-800' :
    status === 'Failed'    ? 'bg-red-100 text-red-800' :
    'bg-gray-100 text-gray-700';
  return <span className={`inline-flex rounded px-2 py-0.5 text-xs font-medium ${color}`}>{status}</span>;
}

export default function RecoveryDashboardPage() {
  const { data, isLoading, error } = useRecoveryDashboard();

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Loading recovery dashboard…</div>;
  if (error || !data) return <div className="p-6 text-sm text-destructive">Failed to load recovery dashboard.</div>;

  const { summary, activeRecoveries, recentCompletedRecoveries } = data;

  return (
    <div className="p-6 space-y-6">
      <h1 className="text-2xl font-semibold">Disaster Recovery Dashboard</h1>

      {/* Summary cards */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <SummaryCard label="Active Recoveries"     value={summary.activeCount} />
        <SummaryCard label="Avg RTO (min)"         value={summary.avgRtoMinutes !== null ? summary.avgRtoMinutes.toFixed(1) : '—'} />
        <SummaryCard label="Max RTO (min)"         value={summary.maxRtoMinutes !== null ? summary.maxRtoMinutes.toFixed(1) : '—'} />
        <SummaryCard label="Completed (30d)"       value={summary.completedLast30Days} />
      </div>

      {/* Active recoveries */}
      <div className="rounded-lg border bg-card p-4 space-y-3">
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
          Active Recoveries ({activeRecoveries.length})
        </h2>
        {activeRecoveries.length === 0 ? (
          <p className="text-sm text-muted-foreground">No nodes currently in recovery.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Node</th>
                <th className="pb-2 font-medium">Recovery Started</th>
                <th className="pb-2 font-medium">Elapsed (min)</th>
                <th className="pb-2 font-medium">Replay Ops</th>
              </tr>
            </thead>
            <tbody>
              {activeRecoveries.map((r: ActiveRecoveryDto) => (
                <tr key={r.nodeId} className="border-b last:border-0">
                  <td className="py-2 font-mono text-xs">{r.nodeId}</td>
                  <td className="py-2 text-xs text-muted-foreground">
                    {new Date(r.recoveryStartedAt).toLocaleString()}
                  </td>
                  <td className="py-2">{r.elapsedMinutes.toFixed(1)}</td>
                  <td className="py-2">
                    {r.associatedReplayOps.length === 0 ? (
                      <span className="text-muted-foreground text-xs">none</span>
                    ) : (
                      <div className="flex gap-1 flex-wrap">
                        {r.associatedReplayOps.map(op => (
                          <StatusChip key={op.operationId} status={op.status} />
                        ))}
                      </div>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      {/* Completed recoveries */}
      <div className="rounded-lg border bg-card p-4 space-y-3">
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
          Recent Completed Recoveries (last 30 days)
        </h2>
        {recentCompletedRecoveries.length === 0 ? (
          <p className="text-sm text-muted-foreground">No completed recoveries in the last 30 days.</p>
        ) : (
          <table className="w-full text-sm">
            <thead>
              <tr className="text-left text-xs text-muted-foreground border-b">
                <th className="pb-2 font-medium">Node</th>
                <th className="pb-2 font-medium">Recovery Started</th>
                <th className="pb-2 font-medium">Restored At</th>
                <th className="pb-2 font-medium">RTO (min)</th>
              </tr>
            </thead>
            <tbody>
              {recentCompletedRecoveries.map((r: CompletedRecoveryDto) => (
                <tr key={`${r.nodeId}-${r.restoredAt}`} className="border-b last:border-0">
                  <td className="py-2 font-mono text-xs">{r.nodeId}</td>
                  <td className="py-2 text-xs text-muted-foreground">
                    {new Date(r.recoveryStartedAt).toLocaleString()}
                  </td>
                  <td className="py-2 text-xs text-muted-foreground">
                    {new Date(r.restoredAt).toLocaleString()}
                  </td>
                  <td className="py-2 font-semibold">{r.rtoMinutes.toFixed(1)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 14: Add nav entry and route**

Open `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`.

Add `ShieldAlert` to the lucide-react import:
```typescript
import { ..., ShieldAlert } from 'lucide-react';
```

In `NAV_GROUPS` Operations group, add after `Health Trends` (or after `Cluster` if Task 1 not done):
```typescript
{ label: 'Recovery', path: '/operations/cluster/recovery', icon: ShieldAlert },
```

Open `src/MSOSync.Frontend/src/app/router.tsx`. Add import:
```typescript
import RecoveryDashboardPage from '../features/operations/cluster/RecoveryDashboardPage';
```

Add route:
```typescript
{ path: 'operations/cluster/recovery', element: <RecoveryDashboardPage /> },
```

- [ ] **Step 15: Add SignalR invalidation**

Open `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`.

In the `NodeLifecycleChanged` case `Promise.all`, add:
```typescript
queryClient.invalidateQueries({ queryKey: clusterKeys.recovery }),
```

- [ ] **Step 16: Write frontend unit tests**

Create `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/RecoveryDashboardPage.test.tsx`:

```tsx
import { render, screen } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import RecoveryDashboardPage from '../RecoveryDashboardPage';

vi.mock('@/shared/hooks/useRecoveryDashboard', () => ({
  useRecoveryDashboard: vi.fn(),
}));

import { useRecoveryDashboard } from '@/shared/hooks/useRecoveryDashboard';

const emptyData = {
  summary: { activeCount: 0, avgRtoMinutes: null, maxRtoMinutes: null, completedLast30Days: 0 },
  activeRecoveries: [],
  recentCompletedRecoveries: [],
};

describe('RecoveryDashboardPage', () => {
  it('renders summary cards', () => {
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data: emptyData, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText('Active Recoveries')).toBeTruthy();
    expect(screen.getByText('Avg RTO (min)')).toBeTruthy();
    expect(screen.getByText('Completed (30d)')).toBeTruthy();
  });

  it('shows empty state when no active recoveries', () => {
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data: emptyData, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText(/no nodes currently in recovery/i)).toBeTruthy();
  });

  it('renders active recovery row with elapsed time', () => {
    const data = {
      ...emptyData,
      activeRecoveries: [{
        nodeId: 'node-x', failureDetectedAt: null,
        recoveryStartedAt: new Date(Date.now() - 30 * 60 * 1000).toISOString(),
        elapsedMinutes: 30.0, associatedReplayOps: [],
      }],
    };
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText('node-x')).toBeTruthy();
    expect(screen.getByText('30.0')).toBeTruthy();
  });

  it('renders replay op status chip for active recovery', () => {
    const data = {
      ...emptyData,
      activeRecoveries: [{
        nodeId: 'node-y', failureDetectedAt: null,
        recoveryStartedAt: new Date().toISOString(),
        elapsedMinutes: 5.0,
        associatedReplayOps: [{ operationId: 'op-1', status: 'Running', itemsDone: 1, itemsTotal: 5 }],
      }],
    };
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data, isLoading: false, error: null });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText('Running')).toBeTruthy();
  });

  it('renders error state', () => {
    (useRecoveryDashboard as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') });
    render(<RecoveryDashboardPage />);
    expect(screen.getByText(/failed to load recovery dashboard/i)).toBeTruthy();
  });
});
```

- [ ] **Step 17: Build and verify**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "RecoveryDashboardQueryServiceTests"
cd D:\MSOSync\src\MSOSync.Frontend; npm run build
```

Expected: unit tests pass, 0 TypeScript errors.

- [ ] **Step 18: Commit**

```powershell
git add `
  src/MSOSync.Metadata/Operations/Cluster/Recovery/ `
  src/MSOSync.Api/Controllers/ClusterController.cs `
  src/MSOSync.Metadata/MetadataServiceExtensions.cs `
  tests/MSOSync.MetadataTests/Operations/RecoveryDashboardQueryServiceTests.cs `
  src/MSOSync.Frontend/src/shared/types/cluster.ts `
  src/MSOSync.Frontend/src/shared/api/cluster.ts `
  src/MSOSync.Frontend/src/shared/hooks/useRecoveryDashboard.ts `
  src/MSOSync.Frontend/src/features/operations/cluster/RecoveryDashboardPage.tsx `
  src/MSOSync.Frontend/src/features/operations/cluster/__tests__/RecoveryDashboardPage.test.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx `
  src/MSOSync.Frontend/src/app/router.tsx `
  src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts

git commit -m "feat(2B.4-T2): Recovery Dashboard — service, endpoint, page, unit tests"
```
