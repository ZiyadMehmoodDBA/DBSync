# Task 1 — Cluster Operations Dashboard

**Files:**
- Create: `src/MSOSync.Metadata/Operations/Cluster/Dtos/ClusterSummaryDto.cs`
- Create: `src/MSOSync.Metadata/Operations/Cluster/IClusterSummaryQueryService.cs`
- Create: `src/MSOSync.Metadata/Operations/Cluster/ClusterSummaryQueryService.cs`
- Create: `src/MSOSync.Api/Controllers/ClusterController.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Create: `tests/MSOSync.MetadataTests/Operations/Cluster/ClusterSummaryQueryServiceTests.cs`
- Create: `src/MSOSync.Frontend/src/shared/types/cluster.ts`
- Create: `src/MSOSync.Frontend/src/shared/api/cluster.ts`
- Create: `src/MSOSync.Frontend/src/shared/hooks/useClusterSummary.ts`
- Create: `src/MSOSync.Frontend/src/features/operations/cluster/ClusterPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/ClusterPage.test.tsx`
- Modify: `src/MSOSync.Frontend/src/app/router.tsx`
- Modify: `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`

**Interfaces:**
- Produces: `IClusterSummaryQueryService.GetSummaryAsync(CancellationToken) → Task<ClusterSummaryDto>`
- Produces: `GET /api/v1/cluster/summary → 200 ClusterSummaryDto`
- Produces: `clusterKeys.summary`, `useClusterSummary()` hook
- Produces: `/operations/cluster` route + nav item

---

- [ ] **Step 1: Create DTOs**

```csharp
// src/MSOSync.Metadata/Operations/Cluster/Dtos/ClusterSummaryDto.cs
namespace MSOSync.Metadata.Operations.Cluster.Dtos;

public sealed record ClusterSummaryDto(
    NodeStateCountsDto                       NodeStates,
    OperationCountsDto                       OperationCounts,
    IReadOnlyList<ActiveOperationSummaryDto> ActiveOperations,
    IReadOnlyList<RollingWaveSummaryDto>     ActiveRollingOps,
    IReadOnlyList<ReplayOperationSummaryDto> ActiveReplays,
    IReadOnlyList<NodeStateChangeDto>        RecentNodeChanges);

public sealed record NodeStateCountsDto(
    int Total, int Active, int Maintenance, int Draining, int Offline);

public sealed record OperationCountsDto(
    int Running, int Pending, int SucceededToday, int FailedToday);

public sealed record ActiveOperationSummaryDto(
    Guid     OperationId,
    string   Type,
    string   Status,
    string?  NodeId,
    int?     ProgressPercent,
    string?  ProgressMessage,
    DateTime StartedAt);

public sealed record RollingWaveSummaryDto(
    Guid   OperationId,
    string Mode,        // "RollingMaintenance" | "RollingUpgrade"
    string Status,
    int    CurrentWave,
    int    TotalWaves,
    int    NodesDone,
    int    NodesTotal,
    int    NodesFailed);

public sealed record ReplayOperationSummaryDto(
    Guid   OperationId,
    string ReplayMode,
    string Status,
    int    ItemsDone,
    int    ItemsTotal,
    int    ItemsFailed);

public sealed record NodeStateChangeDto(
    string        NodeId,
    string?       FromState,
    string        ToState,
    string        Trigger,
    DateTimeOffset OccurredAt);
```

- [ ] **Step 2: Create interface**

```csharp
// src/MSOSync.Metadata/Operations/Cluster/IClusterSummaryQueryService.cs
using MSOSync.Metadata.Operations.Cluster.Dtos;

namespace MSOSync.Metadata.Operations.Cluster;

public interface IClusterSummaryQueryService
{
    Task<ClusterSummaryDto> GetSummaryAsync(CancellationToken ct = default);
}
```

- [ ] **Step 3: Write failing unit tests**

```csharp
// tests/MSOSync.MetadataTests/Operations/Cluster/ClusterSummaryQueryServiceTests.cs
using FluentAssertions;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Persistence.Entities;
using MSOSync.Persistence.Entities.Lifecycle;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Cluster;

public sealed class ClusterSummaryQueryServiceTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContext.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetSummaryAsync_empty_db_returns_zero_counts()
    {
        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();
        result.NodeStates.Total.Should().Be(0);
        result.OperationCounts.Running.Should().Be(0);
        result.ActiveOperations.Should().BeEmpty();
        result.ActiveRollingOps.Should().BeEmpty();
        result.ActiveReplays.Should().BeEmpty();
        result.RecentNodeChanges.Should().BeEmpty();
    }

    [Fact]
    public async Task GetSummaryAsync_counts_active_nodes_correctly()
    {
        _db.Nodes.Add(new SyncNode { NodeId = "n1", NodeName = "n1",
            LifecycleState = NodeLifecycleState.Active, MaintenanceMode = false, TenantId = Guid.Empty });
        _db.Nodes.Add(new SyncNode { NodeId = "n2", NodeName = "n2",
            LifecycleState = NodeLifecycleState.Active, MaintenanceMode = true, TenantId = Guid.Empty });
        _db.Nodes.Add(new SyncNode { NodeId = "n3", NodeName = "n3",
            LifecycleState = NodeLifecycleState.Draining, MaintenanceMode = false, TenantId = Guid.Empty });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.NodeStates.Total.Should().Be(3);
        result.NodeStates.Active.Should().Be(1);
        result.NodeStates.Maintenance.Should().Be(1);
        result.NodeStates.Draining.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_returns_active_operations()
    {
        _db.Operations.Add(new SyncOperation
        {
            OperationId = Guid.NewGuid(), OperationType = "BatchReplay",
            Status = "Running", Source = "Worker",
            StartedAt = DateTime.UtcNow.AddMinutes(-5),
            CanCancel = true, CanRetry = false, TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.OperationCounts.Running.Should().Be(1);
        result.ActiveOperations.Should().HaveCount(1);
        result.ActiveOperations[0].Type.Should().Be("BatchReplay");
    }

    [Fact]
    public async Task GetSummaryAsync_rolling_ops_include_wave_progress()
    {
        var opId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "RollingMaintenance",
            Status = "Running", Source = "User",
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CanCancel = true, CanRetry = false, TenantId = Guid.Empty,
        });
        _db.OperationSteps.Add(new SyncOperationStep
        {
            StepId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", WaveNumber = 1, Status = "Completed",
            TenantId = Guid.Empty,
        });
        _db.OperationSteps.Add(new SyncOperationStep
        {
            StepId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n2", WaveNumber = 2, Status = "Running",
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.ActiveRollingOps.Should().HaveCount(1);
        result.ActiveRollingOps[0].NodesDone.Should().Be(1);
        result.ActiveRollingOps[0].NodesTotal.Should().Be(2);
        result.ActiveRollingOps[0].TotalWaves.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_replay_ops_include_item_progress()
    {
        var opId = Guid.NewGuid();
        var replayId = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = opId, OperationType = "BatchReplay",
            Status = "Running", Source = "User",
            StartedAt = DateTime.UtcNow.AddMinutes(-3),
            CanCancel = true, CanRetry = false, TenantId = Guid.Empty,
        });
        _db.ReplayRequests.Add(new SyncReplayRequest
        {
            ReplayId = replayId, OperationId = opId,
            NodeId = "n1", ReplayMode = "FailedDelivery",
            FromTime = DateTime.UtcNow.AddDays(-1),
            ToTime = DateTime.UtcNow, TenantId = Guid.Empty,
        });
        _db.ReplayItems.Add(new SyncReplayItem
        {
            ItemId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ChannelId = "ch1", EventCount = 5,
            Status = "Completed", TenantId = Guid.Empty,
        });
        _db.ReplayItems.Add(new SyncReplayItem
        {
            ItemId = Guid.NewGuid(), OperationId = opId,
            NodeId = "n1", ChannelId = "ch2", EventCount = 3,
            Status = "Failed", TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.ActiveReplays.Should().HaveCount(1);
        result.ActiveReplays[0].ItemsDone.Should().Be(1);
        result.ActiveReplays[0].ItemsFailed.Should().Be(1);
        result.ActiveReplays[0].ItemsTotal.Should().Be(2);
    }

    [Fact]
    public async Task GetSummaryAsync_recent_node_changes_within_15_minutes_only()
    {
        _db.NodeLifecycleHistory.Add(new SyncNodeLifecycleHistory
        {
            HistoryId = 1, NodeId = "n1",
            ToState = NodeLifecycleState.Active,
            Trigger = LifecycleTrigger.ManualApproval,
            Actor = "admin",
            OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),
            TenantId = Guid.Empty,
        });
        _db.NodeLifecycleHistory.Add(new SyncNodeLifecycleHistory
        {
            HistoryId = 2, NodeId = "n2",
            ToState = NodeLifecycleState.Draining,
            Trigger = LifecycleTrigger.ManualAction,
            Actor = "admin",
            OccurredAt = DateTimeOffset.UtcNow.AddHours(-2), // too old
            TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.RecentNodeChanges.Should().HaveCount(1);
        result.RecentNodeChanges[0].NodeId.Should().Be("n1");
    }

    [Fact]
    public async Task GetSummaryAsync_counts_operations_succeeded_today()
    {
        var todayMidnight = DateTime.UtcNow.Date;
        _db.Operations.Add(new SyncOperation
        {
            OperationId = Guid.NewGuid(), OperationType = "Export",
            Status = "Completed", Result = "Success", Source = "User",
            StartedAt = todayMidnight.AddHours(1),
            CompletedAt = todayMidnight.AddHours(2),
            CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
        });
        _db.Operations.Add(new SyncOperation
        {
            OperationId = Guid.NewGuid(), OperationType = "Export",
            Status = "Completed", Result = "Success", Source = "User",
            StartedAt = DateTime.UtcNow.AddDays(-2), // yesterday — excluded
            CompletedAt = DateTime.UtcNow.AddDays(-2).AddHours(1),
            CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.OperationCounts.SucceededToday.Should().Be(1);
    }

    [Fact]
    public async Task GetSummaryAsync_active_ops_capped_at_50()
    {
        for (var i = 0; i < 60; i++)
        {
            _db.Operations.Add(new SyncOperation
            {
                OperationId = Guid.NewGuid(), OperationType = "Export",
                Status = "Running", Source = "Worker",
                StartedAt = DateTime.UtcNow.AddMinutes(-i),
                CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
            });
        }
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.ActiveOperations.Count.Should().BeLessThanOrEqualTo(50);
    }

    [Fact]
    public async Task GetSummaryAsync_offline_nodes_bucket_decommissioned_and_others()
    {
        _db.Nodes.Add(new SyncNode { NodeId = "n1", NodeName = "n1",
            LifecycleState = NodeLifecycleState.Decommissioned, MaintenanceMode = false, TenantId = Guid.Empty });
        _db.Nodes.Add(new SyncNode { NodeId = "n2", NodeName = "n2",
            LifecycleState = NodeLifecycleState.PendingApproval, MaintenanceMode = false, TenantId = Guid.Empty });
        await _db.SaveChangesAsync();

        var svc = new ClusterSummaryQueryService(_db);
        var result = await svc.GetSummaryAsync();

        result.NodeStates.Offline.Should().Be(2);
    }
}
```

- [ ] **Step 4: Run tests — expect failures**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~ClusterSummaryQueryServiceTests" -v normal
```

Expected: compilation errors (ClusterSummaryQueryService doesn't exist yet).

- [ ] **Step 5: Implement `ClusterSummaryQueryService`**

```csharp
// src/MSOSync.Metadata/Operations/Cluster/ClusterSummaryQueryService.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities.Lifecycle;

namespace MSOSync.Metadata.Operations.Cluster;

public sealed class ClusterSummaryQueryService(AppDbContext db) : IClusterSummaryQueryService
{
    public async Task<ClusterSummaryDto> GetSummaryAsync(CancellationToken ct = default)
    {
        var nodeStatesTask      = QueryNodeStatesAsync(ct);
        var opCountsTask        = QueryOperationCountsAsync(ct);
        var activeOpsTask       = QueryActiveOperationsAsync(ct);
        var rollingTask         = QueryRollingOperationsAsync(ct);
        var replayTask          = QueryReplayOperationsAsync(ct);
        var lifecycleTask       = QueryRecentNodeChangesAsync(ct);

        await Task.WhenAll(nodeStatesTask, opCountsTask, activeOpsTask,
                           rollingTask, replayTask, lifecycleTask);

        return new ClusterSummaryDto(
            await nodeStatesTask,
            await opCountsTask,
            await activeOpsTask,
            await rollingTask,
            await replayTask,
            await lifecycleTask);
    }

    private async Task<NodeStateCountsDto> QueryNodeStatesAsync(CancellationToken ct)
    {
        var nodes = await db.Nodes
            .AsNoTracking()
            .Select(n => new { n.LifecycleState, n.MaintenanceMode })
            .ToListAsync(ct);

        var total       = nodes.Count;
        var maintenance = nodes.Count(n => n.MaintenanceMode);
        var active      = nodes.Count(n => n.LifecycleState == NodeLifecycleState.Active && !n.MaintenanceMode);
        var draining    = nodes.Count(n => n.LifecycleState == NodeLifecycleState.Draining);
        var offline     = nodes.Count(n =>
            !n.MaintenanceMode &&
            n.LifecycleState != NodeLifecycleState.Active &&
            n.LifecycleState != NodeLifecycleState.Draining);

        return new NodeStateCountsDto(total, active, maintenance, draining, offline);
    }

    private async Task<OperationCountsDto> QueryOperationCountsAsync(CancellationToken ct)
    {
        var todayUtc = DateTime.UtcNow.Date;
        var counts = await db.Operations
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Running        = g.Count(o => o.Status == "Running"),
                Pending        = g.Count(o => o.Status == "Pending"),
                SucceededToday = g.Count(o => o.Status == "Completed"
                    && o.Result == "Success"
                    && o.CompletedAt != null
                    && o.CompletedAt.Value >= todayUtc),
                FailedToday    = g.Count(o => o.Status == "Failed"
                    && o.CompletedAt != null
                    && o.CompletedAt.Value >= todayUtc),
            })
            .FirstOrDefaultAsync(ct);

        return counts is null
            ? new OperationCountsDto(0, 0, 0, 0)
            : new OperationCountsDto(counts.Running, counts.Pending,
                                     counts.SucceededToday, counts.FailedToday);
    }

    private async Task<IReadOnlyList<ActiveOperationSummaryDto>> QueryActiveOperationsAsync(
        CancellationToken ct)
    {
        return await db.Operations
            .AsNoTracking()
            .Where(o => o.Status == "Running" || o.Status == "Pending")
            .OrderByDescending(o => o.StartedAt)
            .Take(50)
            .Select(o => new ActiveOperationSummaryDto(
                o.OperationId, o.OperationType, o.Status,
                o.MetadataJson == null ? null : null, // NodeId from MetadataJson if present
                o.ProgressPercent, o.ProgressMessage, o.StartedAt))
            .ToListAsync(ct);
    }

    private async Task<IReadOnlyList<RollingWaveSummaryDto>> QueryRollingOperationsAsync(
        CancellationToken ct)
    {
        var ops = await db.Operations
            .AsNoTracking()
            .Where(o => (o.OperationType == "RollingMaintenance" || o.OperationType == "RollingUpgrade")
                     && (o.Status == "Running" || o.Status == "Pending"))
            .OrderByDescending(o => o.StartedAt)
            .Take(10)
            .Select(o => new { o.OperationId, o.OperationType, o.Status })
            .ToListAsync(ct);

        if (ops.Count == 0) return Array.Empty<RollingWaveSummaryDto>();

        var opIds = ops.Select(o => o.OperationId).ToList();
        var steps = await db.OperationSteps
            .AsNoTracking()
            .Where(s => opIds.Contains(s.OperationId))
            .Select(s => new { s.OperationId, s.WaveNumber, s.Status })
            .ToListAsync(ct);

        return ops.Select(o =>
        {
            var opSteps   = steps.Where(s => s.OperationId == o.OperationId).ToList();
            var done      = opSteps.Count(s => s.Status == "Completed");
            var failed    = opSteps.Count(s => s.Status == "Failed");
            var total     = opSteps.Count;
            var maxWave   = opSteps.Count > 0 ? opSteps.Max(s => s.WaveNumber) : 0;
            var curWave   = opSteps.Where(s => s.Status == "Running")
                                   .Select(s => s.WaveNumber)
                                   .DefaultIfEmpty(0).Max();

            return new RollingWaveSummaryDto(
                o.OperationId, o.OperationType, o.Status,
                curWave, maxWave, done, total, failed);
        }).ToList().AsReadOnly();
    }

    private async Task<IReadOnlyList<ReplayOperationSummaryDto>> QueryReplayOperationsAsync(
        CancellationToken ct)
    {
        var activeReplayOpIds = await db.Operations
            .AsNoTracking()
            .Where(o => o.OperationType == "BatchReplay"
                     && (o.Status == "Running" || o.Status == "Pending"))
            .OrderByDescending(o => o.StartedAt)
            .Take(10)
            .Select(o => o.OperationId)
            .ToListAsync(ct);

        if (activeReplayOpIds.Count == 0) return Array.Empty<ReplayOperationSummaryDto>();

        var requests = await db.ReplayRequests
            .AsNoTracking()
            .Where(r => activeReplayOpIds.Contains(r.OperationId))
            .Select(r => new { r.OperationId, r.ReplayMode })
            .ToListAsync(ct);

        var ops = await db.Operations
            .AsNoTracking()
            .Where(o => activeReplayOpIds.Contains(o.OperationId))
            .Select(o => new { o.OperationId, o.Status })
            .ToListAsync(ct);

        var itemCounts = await db.ReplayItems
            .AsNoTracking()
            .Where(i => activeReplayOpIds.Contains(i.OperationId))
            .GroupBy(i => i.OperationId)
            .Select(g => new
            {
                OperationId = g.Key,
                Total     = g.Count(),
                Done      = g.Count(i => i.Status == "Completed"),
                Failed    = g.Count(i => i.Status == "Failed"),
            })
            .ToListAsync(ct);

        return activeReplayOpIds
            .Select(id =>
            {
                var req    = requests.FirstOrDefault(r => r.OperationId == id);
                var op     = ops.FirstOrDefault(o => o.OperationId == id);
                var counts = itemCounts.FirstOrDefault(c => c.OperationId == id);
                return new ReplayOperationSummaryDto(
                    id,
                    req?.ReplayMode ?? "Unknown",
                    op?.Status ?? "Unknown",
                    counts?.Done ?? 0,
                    counts?.Total ?? 0,
                    counts?.Failed ?? 0);
            })
            .ToList()
            .AsReadOnly();
    }

    private async Task<IReadOnlyList<NodeStateChangeDto>> QueryRecentNodeChangesAsync(
        CancellationToken ct)
    {
        var cutoff = DateTimeOffset.UtcNow.AddMinutes(-15);
        return await db.NodeLifecycleHistory
            .AsNoTracking()
            .Where(h => h.OccurredAt >= cutoff)
            .OrderByDescending(h => h.OccurredAt)
            .Take(50)
            .Select(h => new NodeStateChangeDto(
                h.NodeId,
                h.FromState == null ? null : h.FromState.ToString(),
                h.ToState.ToString(),
                h.Trigger.ToString(),
                h.OccurredAt))
            .ToListAsync(ct);
    }
}
```

> **Note on `ActiveOperationSummaryDto.NodeId`:** `SyncOperation` has no `NodeId` column. Set it to `null` for now — the frontend shows NodeId from `ProgressMessage` or metadata if available.

- [ ] **Step 6: Run tests — expect pass**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~ClusterSummaryQueryServiceTests" -v normal
```

Expected: all 8 tests PASS.

- [ ] **Step 7: Create `ClusterController`**

```csharp
// src/MSOSync.Api/Controllers/ClusterController.cs
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Cluster.Dtos;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ClusterController(IClusterSummaryQueryService svc) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType(typeof(ClusterSummaryDto), 200)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
        => Ok(await svc.GetSummaryAsync(ct));
}
```

- [ ] **Step 8: Register in `MetadataServiceExtensions.cs`**

Find the Phase 2B.2 registration block (after `services.AddScoped<IReplayOperationQueryService,...>`). Add immediately after:

```csharp
        // Phase 2B.3 — Advanced Operations Analytics
        services.AddScoped<IClusterSummaryQueryService, ClusterSummaryQueryService>();
```

- [ ] **Step 9: Build backend**

```
dotnet build src/MSOSync.Api/MSOSync.Api.csproj
```

Expected: 0 errors, 0 warnings.

- [ ] **Step 10: Create TypeScript types**

```typescript
// src/MSOSync.Frontend/src/shared/types/cluster.ts
export interface NodeStateCountsDto {
  total: number;
  active: number;
  maintenance: number;
  draining: number;
  offline: number;
}

export interface OperationCountsDto {
  running: number;
  pending: number;
  succeededToday: number;
  failedToday: number;
}

export interface ActiveOperationSummaryDto {
  operationId: string;
  type: string;
  status: string;
  nodeId: string | null;
  progressPercent: number | null;
  progressMessage: string | null;
  startedAt: string;
}

export interface RollingWaveSummaryDto {
  operationId: string;
  mode: string;
  status: string;
  currentWave: number;
  totalWaves: number;
  nodesDone: number;
  nodesTotal: number;
  nodesFailed: number;
}

export interface ReplayOperationSummaryDto {
  operationId: string;
  replayMode: string;
  status: string;
  itemsDone: number;
  itemsTotal: number;
  itemsFailed: number;
}

export interface NodeStateChangeDto {
  nodeId: string;
  fromState: string | null;
  toState: string;
  trigger: string;
  occurredAt: string;
}

export interface ClusterSummaryDto {
  nodeStates: NodeStateCountsDto;
  operationCounts: OperationCountsDto;
  activeOperations: ActiveOperationSummaryDto[];
  activeRollingOps: RollingWaveSummaryDto[];
  activeReplays: ReplayOperationSummaryDto[];
  recentNodeChanges: NodeStateChangeDto[];
}
```

- [ ] **Step 11: Create API function**

```typescript
// src/MSOSync.Frontend/src/shared/api/cluster.ts
import client from './client';
import type { ClusterSummaryDto } from '../types/cluster';

export const clusterKeys = {
  summary: ['cluster', 'summary'] as const,
} as const;

export async function getClusterSummary(options?: { signal?: AbortSignal }): Promise<ClusterSummaryDto> {
  const { data } = await client.get<ClusterSummaryDto>('/cluster/summary', options);
  return data;
}
```

- [ ] **Step 12: Create hook**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useClusterSummary.ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { clusterKeys, getClusterSummary } from '../api/cluster';
import { useSignalRContext } from '../signalr/context';

export function useClusterSummary() {
  const qc = useQueryClient();
  const { connection } = useSignalRContext();

  useEffect(() => {
    if (!connection) return;
    const handler = () => void qc.invalidateQueries({ queryKey: clusterKeys.summary });
    connection.on('OperationChanged', handler);
    connection.on('NodeLifecycleChanged', handler);
    return () => {
      connection.off('OperationChanged', handler);
      connection.off('NodeLifecycleChanged', handler);
    };
  }, [connection, qc]);

  return useQuery({
    queryKey:       clusterKeys.summary,
    queryFn:        ({ signal }) => getClusterSummary({ signal }),
    staleTime:      10_000,
    gcTime:         60_000,
    refetchInterval: 15_000,
  });
}
```

- [ ] **Step 13: Create `ClusterPage.tsx`**

```tsx
// src/MSOSync.Frontend/src/features/operations/cluster/ClusterPage.tsx
import { useClusterSummary } from '@/shared/hooks/useClusterSummary';
import { Badge } from '@/components/ui/badge';
import { formatDistanceToNow } from 'date-fns';

function StatusBadge({ status }: { status: string }) {
  const color =
    status === 'Running'   ? 'bg-blue-100 text-blue-800' :
    status === 'Pending'   ? 'bg-yellow-100 text-yellow-800' :
    status === 'Completed' ? 'bg-green-100 text-green-800' :
    status === 'Failed'    ? 'bg-red-100 text-red-800' :
    'bg-gray-100 text-gray-700';
  return <span className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${color}`}>{status}</span>;
}

export default function ClusterPage() {
  const { data, isLoading, error } = useClusterSummary();

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Loading cluster summary…</div>;
  if (error || !data)  return <div className="p-6 text-sm text-destructive">Failed to load cluster summary.</div>;

  const { nodeStates, operationCounts, activeOperations, activeRollingOps, activeReplays, recentNodeChanges } = data;

  return (
    <div className="p-6 space-y-6">
      <h1 className="text-2xl font-semibold">Cluster Operations</h1>

      {/* 2×2 grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">

        {/* Node States */}
        <div className="rounded-lg border bg-card p-4 space-y-3">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">Node States</h2>
          <div className="flex flex-wrap gap-2">
            {[
              { label: 'Active',       count: nodeStates.active,      color: 'bg-green-100 text-green-800' },
              { label: 'Maintenance',  count: nodeStates.maintenance,  color: 'bg-yellow-100 text-yellow-800' },
              { label: 'Draining',     count: nodeStates.draining,     color: 'bg-orange-100 text-orange-800' },
              { label: 'Offline',      count: nodeStates.offline,      color: 'bg-gray-100 text-gray-700' },
            ].map(({ label, count, color }) => (
              <div key={label} className={`flex items-center gap-1.5 rounded px-3 py-1.5 ${color}`}>
                <span className="text-xl font-bold">{count}</span>
                <span className="text-xs font-medium">{label}</span>
              </div>
            ))}
          </div>
          <p className="text-xs text-muted-foreground">{nodeStates.total} total nodes</p>
        </div>

        {/* Active Operations */}
        <div className="rounded-lg border bg-card p-4 space-y-2">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
            Active Operations
            {activeOperations.length > 0 && (
              <span className="ml-2 text-foreground">({operationCounts.running} running, {operationCounts.pending} pending)</span>
            )}
          </h2>
          {activeOperations.length === 0 ? (
            <p className="text-sm text-muted-foreground">No active operations</p>
          ) : (
            <div className="space-y-2 max-h-48 overflow-y-auto">
              {activeOperations.map(op => (
                <div key={op.operationId} className="flex items-center justify-between text-sm">
                  <div className="flex items-center gap-2">
                    <StatusBadge status={op.status} />
                    <span className="font-medium">{op.type}</span>
                    {op.nodeId && <span className="text-muted-foreground text-xs">{op.nodeId}</span>}
                  </div>
                  <span className="text-xs text-muted-foreground">
                    {formatDistanceToNow(new Date(op.startedAt), { addSuffix: true })}
                  </span>
                </div>
              ))}
            </div>
          )}
          <div className="flex gap-3 text-xs text-muted-foreground pt-1 border-t">
            <span>✓ {operationCounts.succeededToday} today</span>
            <span>✗ {operationCounts.failedToday} failed</span>
          </div>
        </div>

        {/* Rolling Operations */}
        <div className="rounded-lg border bg-card p-4 space-y-2">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">Rolling Operations</h2>
          {activeRollingOps.length === 0 ? (
            <p className="text-sm text-muted-foreground">No active rolling operations</p>
          ) : (
            <div className="space-y-3">
              {activeRollingOps.map(op => (
                <div key={op.operationId} className="space-y-1">
                  <div className="flex items-center justify-between text-sm">
                    <div className="flex items-center gap-2">
                      <StatusBadge status={op.status} />
                      <span className="font-medium">{op.mode === 'RollingMaintenance' ? 'Maintenance' : 'Upgrade'}</span>
                    </div>
                    <span className="text-xs text-muted-foreground">
                      Wave {op.currentWave}/{op.totalWaves} · {op.nodesDone}/{op.nodesTotal} nodes
                      {op.nodesFailed > 0 && <span className="text-red-600 ml-1">({op.nodesFailed} failed)</span>}
                    </span>
                  </div>
                  <div className="w-full h-1.5 bg-muted rounded-full overflow-hidden">
                    <div
                      className="h-full bg-blue-500 rounded-full transition-all"
                      style={{ width: op.nodesTotal > 0 ? `${(op.nodesDone / op.nodesTotal) * 100}%` : '0%' }}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>

        {/* Replay Operations */}
        <div className="rounded-lg border bg-card p-4 space-y-2">
          <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">Replay Operations</h2>
          {activeReplays.length === 0 ? (
            <p className="text-sm text-muted-foreground">No active replay operations</p>
          ) : (
            <div className="space-y-3">
              {activeReplays.map(op => (
                <div key={op.operationId} className="space-y-1">
                  <div className="flex items-center justify-between text-sm">
                    <div className="flex items-center gap-2">
                      <StatusBadge status={op.status} />
                      <span className="font-medium">{op.replayMode}</span>
                    </div>
                    <span className="text-xs text-muted-foreground">
                      {op.itemsDone}/{op.itemsTotal} items
                      {op.itemsFailed > 0 && <span className="text-red-600 ml-1">({op.itemsFailed} failed)</span>}
                    </span>
                  </div>
                  <div className="w-full h-1.5 bg-muted rounded-full overflow-hidden">
                    <div
                      className="h-full bg-indigo-500 rounded-full transition-all"
                      style={{ width: op.itemsTotal > 0 ? `${(op.itemsDone / op.itemsTotal) * 100}%` : '0%' }}
                    />
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      </div>

      {/* Recent Node Changes */}
      <div className="rounded-lg border bg-card p-4 space-y-2">
        <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide">
          Recent Node State Changes <span className="font-normal text-muted-foreground">(last 15 min)</span>
        </h2>
        {recentNodeChanges.length === 0 ? (
          <p className="text-sm text-muted-foreground">No node state changes in the last 15 minutes</p>
        ) : (
          <div className="flex gap-3 overflow-x-auto pb-1">
            {recentNodeChanges.map((change, i) => (
              <div key={i} className="flex-shrink-0 rounded border bg-muted/40 px-3 py-2 text-xs space-y-1 min-w-[140px]">
                <p className="font-semibold truncate">{change.nodeId}</p>
                <p className="text-muted-foreground">
                  {change.fromState ? `${change.fromState} → ` : ''}{change.toState}
                </p>
                <p className="text-muted-foreground">{change.trigger}</p>
                <p className="text-muted-foreground">
                  {formatDistanceToNow(new Date(change.occurredAt), { addSuffix: true })}
                </p>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
```

- [ ] **Step 14: Write frontend test**

```typescript
// src/MSOSync.Frontend/src/features/operations/cluster/__tests__/ClusterPage.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import ClusterPage from '../ClusterPage';
import * as clusterApi from '@/shared/api/cluster';
import type { ClusterSummaryDto } from '@/shared/types/cluster';

vi.mock('@/shared/api/cluster');
vi.mock('@/shared/signalr/context', () => ({
  useSignalRContext: () => ({ connection: null, connectionState: 'disconnected' }),
}));

const emptySummary: ClusterSummaryDto = {
  nodeStates: { total: 0, active: 0, maintenance: 0, draining: 0, offline: 0 },
  operationCounts: { running: 0, pending: 0, succeededToday: 0, failedToday: 0 },
  activeOperations: [],
  activeRollingOps: [],
  activeReplays: [],
  recentNodeChanges: [],
};

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('ClusterPage', () => {
  beforeEach(() => {
    vi.mocked(clusterApi.getClusterSummary).mockResolvedValue(emptySummary);
  });

  it('shows zero counts on empty data', async () => {
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('0 total nodes')).toBeInTheDocument();
  });

  it('shows no active operations message', async () => {
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('No active operations')).toBeInTheDocument();
  });

  it('shows no rolling operations message', async () => {
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('No active rolling operations')).toBeInTheDocument();
  });

  it('renders active operation when present', async () => {
    const summary = {
      ...emptySummary,
      operationCounts: { running: 1, pending: 0, succeededToday: 0, failedToday: 0 },
      activeOperations: [{
        operationId: 'op-1', type: 'BatchReplay', status: 'Running',
        nodeId: null, progressPercent: 42, progressMessage: 'Processing…',
        startedAt: new Date().toISOString(),
      }],
    };
    vi.mocked(clusterApi.getClusterSummary).mockResolvedValue(summary);
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('BatchReplay')).toBeInTheDocument();
  });

  it('shows recent node change strip', async () => {
    const summary = {
      ...emptySummary,
      recentNodeChanges: [{
        nodeId: 'node-abc', fromState: 'Active', toState: 'Maintenance',
        trigger: 'ManualAction', occurredAt: new Date().toISOString(),
      }],
    };
    vi.mocked(clusterApi.getClusterSummary).mockResolvedValue(summary);
    render(<ClusterPage />, { wrapper });
    expect(await screen.findByText('node-abc')).toBeInTheDocument();
  });
});
```

- [ ] **Step 15: Add route and nav item**

In `src/MSOSync.Frontend/src/app/router.tsx`, import `ClusterPage` and add the route. Find where `/operations/jobs` is defined and add after it:

```tsx
// Add import at top:
const ClusterPage = lazy(() => import('../features/operations/cluster/ClusterPage'));

// Add route in operations children (after jobs route):
{ path: 'cluster', element: <Suspense fallback={<PageLoader />}><ClusterPage /></Suspense> },
```

In `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`, add to the `'Operations'` group after the Jobs entry:

```tsx
// Add import at top of file:
import { Monitor } from 'lucide-react';

// In NAV_GROUPS, Operations items array, after Jobs:
{ label: 'Cluster',   path: '/operations/cluster',   icon: Monitor },
```

- [ ] **Step 16: Build frontend**

```
cd src/MSOSync.Frontend && npm run build
```

Expected: 0 TypeScript errors, build succeeds.

- [ ] **Step 17: Run frontend tests**

```
cd src/MSOSync.Frontend && npm test -- ClusterPage
```

Expected: 5 tests PASS.

- [ ] **Step 18: Run full solution build**

```
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: 0 errors.

- [ ] **Step 19: Commit**

```
git add src/MSOSync.Metadata/Operations/Cluster/
git add src/MSOSync.Api/Controllers/ClusterController.cs
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add tests/MSOSync.MetadataTests/Operations/Cluster/
git add src/MSOSync.Frontend/src/shared/types/cluster.ts
git add src/MSOSync.Frontend/src/shared/api/cluster.ts
git add src/MSOSync.Frontend/src/shared/hooks/useClusterSummary.ts
git add src/MSOSync.Frontend/src/features/operations/cluster/
git add src/MSOSync.Frontend/src/app/router.tsx
git add src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git commit -m "feat(2B.3-T1): Cluster Operations Dashboard — summary service, controller, ClusterPage"
```
