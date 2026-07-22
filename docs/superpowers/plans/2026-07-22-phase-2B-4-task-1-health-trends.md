# Task 1 — Cluster Health Trends

Part of [Phase 2B.4 Master Plan](2026-07-22-phase-2B-4-master.md). Deliver `ClusterHealthTrendService`, a new `GET /api/v1/cluster/health-trends` endpoint on `ClusterController`, and `HealthTrendsPage.tsx`.

## Files

**Create (backend):**
- `src/MSOSync.Metadata/Operations/Cluster/HealthTrends/IClusterHealthTrendService.cs`
- `src/MSOSync.Metadata/Operations/Cluster/HealthTrends/Dtos/ClusterHealthTrendDto.cs`
- `src/MSOSync.Metadata/Operations/Cluster/HealthTrends/ClusterHealthTrendService.cs`
- `src/MSOSync.Api/Dtos/Cluster/GetHealthTrendsRequest.cs`
- `src/MSOSync.Api/Validators/GetHealthTrendsRequestValidator.cs`

**Modify (backend):**
- `src/MSOSync.Api/Controllers/ClusterController.cs` — add `IClusterHealthTrendService` param + new endpoint
- `src/MSOSync.Metadata/MetadataServiceExtensions.cs` — register service

**Create (tests):**
- `tests/MSOSync.MetadataTests/Operations/ClusterHealthTrendServiceTests.cs`

**Modify (frontend):**
- `src/MSOSync.Frontend/src/shared/types/cluster.ts` — add 3 interfaces
- `src/MSOSync.Frontend/src/shared/api/cluster.ts` — extend `clusterKeys`, add `getHealthTrends()`
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add nav entry + icon import
- `src/MSOSync.Frontend/src/app/router.tsx` — add route + import

**Create (frontend):**
- `src/MSOSync.Frontend/src/shared/hooks/useHealthTrends.ts`
- `src/MSOSync.Frontend/src/features/operations/cluster/HealthTrendsPage.tsx`
- `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/HealthTrendsPage.test.tsx`

**Modify (signalR):**
- `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts` — add invalidation for health-trends

## Interfaces

**Produces (consumed by Task 4):**
```csharp
// IClusterHealthTrendService.cs
Task<ClusterHealthTrendDto> GetTrendsAsync(string window, string? nodeId, CancellationToken ct);
```

---

- [ ] **Step 1: Write failing unit tests**

Create `tests/MSOSync.MetadataTests/Operations/ClusterHealthTrendServiceTests.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.HealthTrends;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;
using FluentAssertions;
using Xunit;

namespace MSOSync.MetadataTests.Operations;

public sealed class ClusterHealthTrendServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ClusterHealthTrendService _svc;

    public ClusterHealthTrendServiceTests()
    {
        var opts = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(opts);
        _svc = new ClusterHealthTrendService(_db);
    }

    public void Dispose() => _db.Dispose();

    // If AppDbContext constructor requires more arguments (e.g. ICurrentTenantAccessor),
    // check the pattern in any existing test file under tests/MSOSync.MetadataTests/.

    [Theory]
    [InlineData("1h",  12)]
    [InlineData("6h",  12)]
    [InlineData("24h", 12)]
    [InlineData("7d",  14)]
    public async Task GetTrendsAsync_AllWindows_ReturnCorrectBucketCount(string window, int expected)
    {
        var result = await _svc.GetTrendsAsync(window, null, default);
        result.BucketCount.Should().Be(expected);
        result.Buckets.Should().HaveCount(expected);
        result.Window.Should().Be(window);
    }

    [Fact]
    public async Task GetTrendsAsync_NoHistory_AllBucketsZeroAndNodeStatsEmpty()
    {
        var result = await _svc.GetTrendsAsync("6h", null, default);
        result.Buckets.Should().AllSatisfy(b =>
        {
            b.ReachableCount.Should().Be(0);
            b.DegradedCount.Should().Be(0);
            b.UnreachableCount.Should().Be(0);
            b.TransitionCount.Should().Be(0);
        });
        result.NodeProbeStats.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendsAsync_NodeAllReachable_UptimePct100()
    {
        var tenantId = Guid.NewGuid();
        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "n1", PreviousStatus = ConnectivityStatus.Unknown, NewStatus = ConnectivityStatus.Reachable, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n1", PreviousStatus = ConnectivityStatus.Reachable, NewStatus = ConnectivityStatus.Reachable, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5),  TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        var nodeStat = result.NodeProbeStats.FirstOrDefault(n => n.NodeId == "n1");
        nodeStat.Should().NotBeNull();
        nodeStat!.UptimePct.Should().Be(100.0);
        nodeStat.ConsecutiveProbeFailures.Should().Be(0);
    }

    [Fact]
    public async Task GetTrendsAsync_NodeMixedStatus_UptimePct50()
    {
        var tenantId = Guid.NewGuid();
        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "n2", PreviousStatus = ConnectivityStatus.Unknown,     NewStatus = ConnectivityStatus.Reachable,    OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-20), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n2", PreviousStatus = ConnectivityStatus.Reachable,   NewStatus = ConnectivityStatus.Unreachable,  OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-15), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        var nodeStat = result.NodeProbeStats.FirstOrDefault(n => n.NodeId == "n2");
        nodeStat!.UptimePct.Should().Be(50.0);
        nodeStat.ConsecutiveProbeFailures.Should().Be(1);
        nodeStat.ConnectivityStatus.Should().Be("Unreachable");
    }

    [Fact]
    public async Task GetTrendsAsync_NodeIdFilter_ScopesToSingleNode()
    {
        var tenantId = Guid.NewGuid();
        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "nA", NewStatus = ConnectivityStatus.Reachable, OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "nB", NewStatus = ConnectivityStatus.Degraded,  OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-5), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", "nA", default);

        result.NodeProbeStats.Should().HaveCount(1);
        result.NodeProbeStats[0].NodeId.Should().Be("nA");
    }

    [Fact]
    public async Task GetTrendsAsync_OldHistoryOutsideWindow_Excluded()
    {
        var tenantId = Guid.NewGuid();
        _db.Set<SyncNodeConnectivityHistory>().Add(
            new SyncNodeConnectivityHistory { NodeId = "old", NewStatus = ConnectivityStatus.Unreachable, OccurredAt = DateTimeOffset.UtcNow.AddHours(-3), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        result.NodeProbeStats.Should().BeEmpty();
    }

    [Fact]
    public async Task GetTrendsAsync_ConsecutiveFailures_CountedFromMostRecent()
    {
        var tenantId = Guid.NewGuid();
        // Reachable, then 2 consecutive failures
        _db.Set<SyncNodeConnectivityHistory>().AddRange(
            new SyncNodeConnectivityHistory { NodeId = "n3", NewStatus = ConnectivityStatus.Reachable,    OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-30), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n3", NewStatus = ConnectivityStatus.Degraded,     OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-20), TenantId = tenantId },
            new SyncNodeConnectivityHistory { NodeId = "n3", NewStatus = ConnectivityStatus.Unreachable,  OccurredAt = DateTimeOffset.UtcNow.AddMinutes(-10), TenantId = tenantId });
        await _db.SaveChangesAsync();

        var result = await _svc.GetTrendsAsync("1h", null, default);

        var stat = result.NodeProbeStats.First(n => n.NodeId == "n3");
        stat.ConsecutiveProbeFailures.Should().Be(2);
    }
}
```

- [ ] **Step 2: Run tests — expect failure (class not found)**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "ClusterHealthTrendServiceTests" --no-build 2>&1 | Select-String -Pattern "FAILED|PASSED|Error"
```

Expected: compile error — `ClusterHealthTrendService` not found.

- [ ] **Step 3: Create DTOs**

Create `src/MSOSync.Metadata/Operations/Cluster/HealthTrends/Dtos/ClusterHealthTrendDto.cs`:

```csharp
namespace MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;

public sealed record ClusterHealthTrendDto(
    string                             Window,
    int                                BucketCount,
    IReadOnlyList<HealthBucketDto>     Buckets,
    IReadOnlyList<NodeProbeStatsDto>   NodeProbeStats);

public sealed record HealthBucketDto(
    DateTime BucketStart,
    int      ReachableCount,
    int      DegradedCount,
    int      UnreachableCount,
    int      TotalNodes,
    int      TransitionCount);

public sealed record NodeProbeStatsDto(
    string NodeId,
    string ConnectivityStatus,
    int?   LastProbeLatencyMs,       // Always null — SyncNodeConnectivityHistory has no latency field
    int    ConsecutiveProbeFailures,
    double UptimePct);
```

- [ ] **Step 4: Create interface**

Create `src/MSOSync.Metadata/Operations/Cluster/HealthTrends/IClusterHealthTrendService.cs`:

```csharp
using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;

namespace MSOSync.Metadata.Operations.Cluster.HealthTrends;

public interface IClusterHealthTrendService
{
    Task<ClusterHealthTrendDto> GetTrendsAsync(string window, string? nodeId, CancellationToken ct);
}
```

- [ ] **Step 5: Implement service**

Create `src/MSOSync.Metadata/Operations/Cluster/HealthTrends/ClusterHealthTrendService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;
using MSOSync.Persistence;
using MSOSync.Persistence.Entities;

namespace MSOSync.Metadata.Operations.Cluster.HealthTrends;

public sealed class ClusterHealthTrendService(AppDbContext db) : IClusterHealthTrendService
{
    public async Task<ClusterHealthTrendDto> GetTrendsAsync(string window, string? nodeId, CancellationToken ct)
    {
        var (windowSpan, bucketSize, bucketCount) = window switch
        {
            "1h"  => (TimeSpan.FromHours(1),  TimeSpan.FromMinutes(5),  12),
            "6h"  => (TimeSpan.FromHours(6),  TimeSpan.FromMinutes(30), 12),
            "24h" => (TimeSpan.FromHours(24), TimeSpan.FromHours(2),    12),
            "7d"  => (TimeSpan.FromDays(7),   TimeSpan.FromHours(12),   14),
            _     => throw new ArgumentException($"Unknown window: {window}", nameof(window))
        };

        var from = DateTimeOffset.UtcNow - windowSpan;

        var query = db.Set<SyncNodeConnectivityHistory>()
            .AsNoTracking()
            .Where(h => h.OccurredAt >= from);

        if (nodeId is not null)
            query = query.Where(h => h.NodeId == nodeId);

        var history = await query
            .OrderBy(h => h.OccurredAt)
            .Select(h => new { h.NodeId, h.NewStatus, h.OccurredAt })
            .ToListAsync(ct);

        // Group by node, sorted chronologically
        var byNode = history
            .GroupBy(h => h.NodeId)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.OccurredAt).ToList());

        // Build buckets
        var buckets = new List<HealthBucketDto>(bucketCount);
        for (var i = 0; i < bucketCount; i++)
        {
            var bucketStart = from + bucketSize * i;
            var bucketEnd   = bucketStart + bucketSize;

            int reachable = 0, degraded = 0, unreachable = 0, transitions = 0;

            foreach (var (_, entries) in byNode)
            {
                // Node's state at end of this bucket = most recent entry with OccurredAt < bucketEnd
                var last = entries.LastOrDefault(e => e.OccurredAt < bucketEnd);
                if (last is not null)
                {
                    switch (last.NewStatus)
                    {
                        case ConnectivityStatus.Reachable:   reachable++;   break;
                        case ConnectivityStatus.Degraded:    degraded++;    break;
                        case ConnectivityStatus.Unreachable: unreachable++; break;
                    }
                }
                transitions += entries.Count(e => e.OccurredAt >= bucketStart && e.OccurredAt < bucketEnd);
            }

            buckets.Add(new HealthBucketDto(
                bucketStart.UtcDateTime,
                reachable,
                degraded,
                unreachable,
                reachable + degraded + unreachable,
                transitions));
        }

        // Per-node probe stats
        var nodeStats = byNode.Select(kvp =>
        {
            var entries    = kvp.Value;
            var mostRecent = entries.Last();

            var consecutive = 0;
            foreach (var e in entries.AsEnumerable().Reverse())
            {
                if (e.NewStatus != ConnectivityStatus.Reachable) consecutive++;
                else break;
            }

            var uptimePct = entries.Count > 0
                ? Math.Round((double)entries.Count(e => e.NewStatus == ConnectivityStatus.Reachable) / entries.Count * 100.0, 2)
                : 100.0;

            return new NodeProbeStatsDto(
                kvp.Key,
                mostRecent.NewStatus.ToString(),
                null,   // No latency field in SyncNodeConnectivityHistory
                consecutive,
                uptimePct);
        }).ToList();

        return new ClusterHealthTrendDto(window, bucketCount, buckets, nodeStats);
    }
}
```

- [ ] **Step 6: Run tests — expect pass**

```powershell
dotnet build D:\MSOSync\src\MSOSync.Metadata\MSOSync.Metadata.csproj
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "ClusterHealthTrendServiceTests"
```

Expected: all 8 tests pass.

- [ ] **Step 7: Create request DTO + validator**

Create `src/MSOSync.Api/Dtos/Cluster/GetHealthTrendsRequest.cs`:

```csharp
namespace MSOSync.Api.Dtos.Cluster;

public sealed record GetHealthTrendsRequest(string Window = "6h", string? NodeId = null);
```

Create `src/MSOSync.Api/Validators/GetHealthTrendsRequestValidator.cs`:

```csharp
using FluentValidation;
using MSOSync.Api.Dtos.Cluster;

namespace MSOSync.Api.Validators;

public sealed class GetHealthTrendsRequestValidator : AbstractValidator<GetHealthTrendsRequest>
{
    private static readonly HashSet<string> ValidWindows = ["1h", "6h", "24h", "7d"];

    public GetHealthTrendsRequestValidator()
    {
        RuleFor(r => r.Window)
            .Must(w => ValidWindows.Contains(w))
            .WithMessage("Window must be one of: 1h, 6h, 24h, 7d.");
    }
}
```

- [ ] **Step 8: Extend ClusterController**

Modify `src/MSOSync.Api/Controllers/ClusterController.cs`. Add `IClusterHealthTrendService` to the primary constructor and add the new endpoint:

```csharp
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MSOSync.Api.Dtos.Cluster;
using MSOSync.Metadata.Operations.Cluster;
using MSOSync.Metadata.Operations.Cluster.Dtos;
using MSOSync.Metadata.Operations.Cluster.HealthTrends;
using MSOSync.Metadata.Operations.Cluster.HealthTrends.Dtos;

namespace MSOSync.Api.Controllers;

[ApiController]
[Route("api/v1/cluster")]
[Authorize(Policy = "ViewerOrAbove")]
public sealed class ClusterController(
    IClusterSummaryQueryService summary,
    IClusterHealthTrendService  healthTrends,
    IValidator<GetHealthTrendsRequest> healthTrendsValidator) : ControllerBase
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
}
```

- [ ] **Step 9: Register in DI**

Open `src/MSOSync.Metadata/MetadataServiceExtensions.cs`. Add to the `// Phase 2B.3 — Advanced Operations Analytics` block (after existing registrations, before Epic 13):

```csharp
// Phase 2B.4 — Cluster Health, Recovery, Diagnostics
services.AddScoped<IClusterHealthTrendService, ClusterHealthTrendService>();
```

Also add the namespace using if not already present:
```csharp
using MSOSync.Metadata.Operations.Cluster.HealthTrends;
```

Also register the validator in `src/MSOSync.Api/Program.cs` (or wherever FluentValidation validators are registered — search for `AddScoped<IValidator<` to find the pattern):

```csharp
services.AddScoped<IValidator<GetHealthTrendsRequest>, GetHealthTrendsRequestValidator>();
```

- [ ] **Step 10: Build backend**

```powershell
dotnet build D:\MSOSync\MSOSync.sln
```

Expected: 0 errors.

- [ ] **Step 11: Add TypeScript types**

Open `src/MSOSync.Frontend/src/shared/types/cluster.ts`. Append at the end:

```typescript
// Phase 2B.4 — Cluster Health Trends
export interface HealthBucketDto {
  bucketStart: string;
  reachableCount: number;
  degradedCount: number;
  unreachableCount: number;
  totalNodes: number;
  transitionCount: number;
}

export interface NodeProbeStatsDto {
  nodeId: string;
  connectivityStatus: string;
  lastProbeLatencyMs: number | null;
  consecutiveProbeFailures: number;
  uptimePct: number;
}

export interface ClusterHealthTrendDto {
  window: string;
  bucketCount: number;
  buckets: HealthBucketDto[];
  nodeProbeStats: NodeProbeStatsDto[];
}
```

- [ ] **Step 12: Extend API module**

Open `src/MSOSync.Frontend/src/shared/api/cluster.ts`. Replace the existing `clusterKeys` const and add the new function:

```typescript
import client from './client';
import type { ClusterSummaryDto, ClusterHealthTrendDto } from '../types/cluster';

export const clusterKeys = {
  summary:      ['cluster', 'summary']                                              as const,
  healthTrends: (window: string, nodeId?: string) =>
                  ['cluster', 'health-trends', window, nodeId ?? null]              as const,
} as const;

export async function getClusterSummary(options?: { signal?: AbortSignal }): Promise<ClusterSummaryDto> {
  const { data } = await client.get<ClusterSummaryDto>('/cluster/summary', options);
  return data;
}

export async function getHealthTrends(
  window: string,
  nodeId?: string,
  options?: { signal?: AbortSignal },
): Promise<ClusterHealthTrendDto> {
  const params = new URLSearchParams({ window });
  if (nodeId) params.set('nodeId', nodeId);
  const { data } = await client.get<ClusterHealthTrendDto>(`/cluster/health-trends?${params}`, options);
  return data;
}
```

- [ ] **Step 13: Create hook**

Create `src/MSOSync.Frontend/src/shared/hooks/useHealthTrends.ts`:

```typescript
import { useQuery } from '@tanstack/react-query';
import { clusterKeys, getHealthTrends } from '../api/cluster';

export function useHealthTrends(window: string, nodeId?: string) {
  return useQuery({
    queryKey:  clusterKeys.healthTrends(window, nodeId),
    queryFn:   ({ signal }) => getHealthTrends(window, nodeId, { signal }),
    staleTime: 30_000,
    gcTime:    120_000,
  });
}
```

- [ ] **Step 14: Create HealthTrendsPage**

Create `src/MSOSync.Frontend/src/features/operations/cluster/HealthTrendsPage.tsx`:

```tsx
import { useState } from 'react';
import { AreaChart, Area, XAxis, YAxis, CartesianGrid, Tooltip, ResponsiveContainer, Legend } from 'recharts';
import { useHealthTrends } from '@/shared/hooks/useHealthTrends';
import type { NodeProbeStatsDto } from '@/shared/types/cluster';

const WINDOWS = ['1h', '6h', '24h', '7d'] as const;
type Window = typeof WINDOWS[number];

function ConnectivityBadge({ status }: { status: string }) {
  const color =
    status === 'Reachable'   ? 'bg-green-100 text-green-800' :
    status === 'Degraded'    ? 'bg-yellow-100 text-yellow-800' :
    status === 'Unreachable' ? 'bg-red-100 text-red-800' :
    'bg-gray-100 text-gray-700';
  return (
    <span className={`inline-flex items-center rounded px-2 py-0.5 text-xs font-medium ${color}`}>
      {status}
    </span>
  );
}

export default function HealthTrendsPage() {
  const [window, setWindow]   = useState<Window>('6h');
  const [nodeId, setNodeId]   = useState<string | undefined>(undefined);
  const { data, isLoading, error } = useHealthTrends(window, nodeId);

  const chartData = data?.buckets.map(b => ({
    time:        new Date(b.bucketStart).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
    Reachable:   b.reachableCount,
    Degraded:    b.degradedCount,
    Unreachable: b.unreachableCount,
  })) ?? [];

  const nodeOptions = data?.nodeProbeStats.map(n => n.nodeId) ?? [];

  return (
    <div className="p-6 space-y-6">
      <div className="flex items-center gap-4">
        <h1 className="text-2xl font-semibold">Cluster Health Trends</h1>
        <div className="flex gap-1 ml-auto">
          {WINDOWS.map(w => (
            <button
              key={w}
              onClick={() => setWindow(w)}
              className={`px-3 py-1 rounded text-sm font-medium transition-colors ${
                window === w
                  ? 'bg-neutral-900 text-white dark:bg-white dark:text-neutral-900'
                  : 'bg-neutral-100 text-neutral-600 hover:bg-neutral-200 dark:bg-neutral-800 dark:text-neutral-400'
              }`}
            >
              {w}
            </button>
          ))}
        </div>
        {nodeOptions.length > 0 && (
          <select
            value={nodeId ?? ''}
            onChange={e => setNodeId(e.target.value || undefined)}
            className="text-sm border rounded px-2 py-1 bg-background"
            aria-label="Filter by node"
          >
            <option value="">All nodes</option>
            {nodeOptions.map(n => <option key={n} value={n}>{n}</option>)}
          </select>
        )}
      </div>

      {isLoading && <div className="text-sm text-muted-foreground">Loading health trends…</div>}
      {error && <div className="text-sm text-destructive">Failed to load health trends.</div>}

      {data && (
        <>
          <div className="rounded-lg border bg-card p-4">
            <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide mb-4">
              Connectivity Over Time
            </h2>
            <ResponsiveContainer width="100%" height={220}>
              <AreaChart data={chartData} margin={{ top: 4, right: 8, left: 0, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" className="stroke-muted" />
                <XAxis dataKey="time" tick={{ fontSize: 11 }} />
                <YAxis allowDecimals={false} tick={{ fontSize: 11 }} />
                <Tooltip />
                <Legend />
                <Area type="monotone" dataKey="Reachable"   stackId="1" fill="#86efac" stroke="#22c55e" />
                <Area type="monotone" dataKey="Degraded"    stackId="1" fill="#fde68a" stroke="#f59e0b" />
                <Area type="monotone" dataKey="Unreachable" stackId="1" fill="#fca5a5" stroke="#ef4444" />
              </AreaChart>
            </ResponsiveContainer>
          </div>

          <div className="rounded-lg border bg-card p-4">
            <h2 className="text-sm font-semibold text-muted-foreground uppercase tracking-wide mb-3">
              Node Probe Stats
            </h2>
            {data.nodeProbeStats.length === 0 ? (
              <p className="text-sm text-muted-foreground">No connectivity data in this window.</p>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="text-left text-xs text-muted-foreground border-b">
                    <th className="pb-2 font-medium">Node</th>
                    <th className="pb-2 font-medium">Status</th>
                    <th className="pb-2 font-medium">Consecutive Failures</th>
                    <th className="pb-2 font-medium">Uptime %</th>
                  </tr>
                </thead>
                <tbody>
                  {data.nodeProbeStats.map((n: NodeProbeStatsDto) => (
                    <tr key={n.nodeId} className="border-b last:border-0">
                      <td className="py-2 font-mono text-xs">{n.nodeId}</td>
                      <td className="py-2"><ConnectivityBadge status={n.connectivityStatus} /></td>
                      <td className="py-2 text-center">{n.consecutiveProbeFailures}</td>
                      <td className="py-2">{n.uptimePct.toFixed(1)}%</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </>
      )}
    </div>
  );
}
```

- [ ] **Step 15: Add nav entry and route**

Open `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`.

Add `TrendingUp` to the lucide-react import line (it already imports from `'lucide-react'`):
```typescript
import {
  // ... existing imports ...
  TrendingUp,
} from 'lucide-react';
```

In `NAV_GROUPS`, add to the `'Operations'` group after the `Cluster` entry:
```typescript
{ label: 'Health Trends', path: '/operations/cluster/health-trends', icon: TrendingUp },
```

Open `src/MSOSync.Frontend/src/app/router.tsx`.

Add import at the top alongside `ClusterPage`:
```typescript
import HealthTrendsPage from '../features/operations/cluster/HealthTrendsPage';
```

Add route inside the `AppLayout` children, after the `operations/cluster` route:
```typescript
{ path: 'operations/cluster/health-trends', element: <HealthTrendsPage /> },
```

- [ ] **Step 16: Add SignalR invalidation**

Open `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`.

In `case OperationsEventType.NodeLifecycleChanged:` block, add inside the `Promise.all`:
```typescript
queryClient.invalidateQueries({ queryKey: ['cluster', 'health-trends'] }),
```

Also add a new case for connectivity changes. Look for `invalidateNodeHealth` calls. In the `NodeHealthChanged` case (which calls `invalidateNodeHealth`), the existing function doesn't invalidate health-trends. Modify `invalidateNodeHealth`:

```typescript
async function invalidateNodeHealth(queryClient: QueryClient): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['nodes'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-graph'] }),
    queryClient.invalidateQueries({ queryKey: ['topology-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['metrics-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['dashboard-summary'] }),
    queryClient.invalidateQueries({ queryKey: ['cluster', 'health-trends'] }),
  ]);
}
```

- [ ] **Step 17: Write frontend unit tests**

Create `src/MSOSync.Frontend/src/features/operations/cluster/__tests__/HealthTrendsPage.test.tsx`:

```tsx
import { render, screen, fireEvent } from '@testing-library/react';
import { describe, it, expect, vi } from 'vitest';
import HealthTrendsPage from '../HealthTrendsPage';

vi.mock('@/shared/hooks/useHealthTrends', () => ({
  useHealthTrends: vi.fn(),
}));

import { useHealthTrends } from '@/shared/hooks/useHealthTrends';

const mockData = {
  window: '6h',
  bucketCount: 12,
  buckets: Array.from({ length: 12 }, (_, i) => ({
    bucketStart: new Date(Date.now() - (12 - i) * 30 * 60 * 1000).toISOString(),
    reachableCount: 3,
    degradedCount: 0,
    unreachableCount: 0,
    totalNodes: 3,
    transitionCount: 0,
  })),
  nodeProbeStats: [
    { nodeId: 'node-1', connectivityStatus: 'Reachable', lastProbeLatencyMs: null, consecutiveProbeFailures: 0, uptimePct: 100 },
    { nodeId: 'node-2', connectivityStatus: 'Degraded',  lastProbeLatencyMs: null, consecutiveProbeFailures: 1, uptimePct: 80  },
  ],
};

describe('HealthTrendsPage', () => {
  it('renders loading state', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: true, error: null });
    render(<HealthTrendsPage />);
    expect(screen.getByText(/loading health trends/i)).toBeTruthy();
  });

  it('renders chart and node stats table when data loaded', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: mockData, isLoading: false, error: null });
    render(<HealthTrendsPage />);
    expect(screen.getByText('node-1')).toBeTruthy();
    expect(screen.getByText('node-2')).toBeTruthy();
    expect(screen.getByText('Reachable')).toBeTruthy();
    expect(screen.getByText('Degraded')).toBeTruthy();
  });

  it('window selector buttons are rendered', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: mockData, isLoading: false, error: null });
    render(<HealthTrendsPage />);
    expect(screen.getByText('1h')).toBeTruthy();
    expect(screen.getByText('24h')).toBeTruthy();
    expect(screen.getByText('7d')).toBeTruthy();
  });

  it('renders empty state when no node probe stats', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({
      data: { ...mockData, nodeProbeStats: [] },
      isLoading: false,
      error: null,
    });
    render(<HealthTrendsPage />);
    expect(screen.getByText(/no connectivity data/i)).toBeTruthy();
  });

  it('renders error state', () => {
    (useHealthTrends as ReturnType<typeof vi.fn>).mockReturnValue({ data: undefined, isLoading: false, error: new Error('fail') });
    render(<HealthTrendsPage />);
    expect(screen.getByText(/failed to load health trends/i)).toBeTruthy();
  });
});
```

- [ ] **Step 18: Build and verify**

```powershell
dotnet test D:\MSOSync\tests\MSOSync.MetadataTests\MSOSync.MetadataTests.csproj --filter "ClusterHealthTrendServiceTests"
```

```powershell
cd D:\MSOSync\src\MSOSync.Frontend; npm run build
```

Expected: unit tests pass, 0 TypeScript errors.

- [ ] **Step 19: Commit**

```powershell
git add `
  src/MSOSync.Metadata/Operations/Cluster/HealthTrends/ `
  src/MSOSync.Api/Dtos/Cluster/GetHealthTrendsRequest.cs `
  src/MSOSync.Api/Validators/GetHealthTrendsRequestValidator.cs `
  src/MSOSync.Api/Controllers/ClusterController.cs `
  src/MSOSync.Metadata/MetadataServiceExtensions.cs `
  tests/MSOSync.MetadataTests/Operations/ClusterHealthTrendServiceTests.cs `
  src/MSOSync.Frontend/src/shared/types/cluster.ts `
  src/MSOSync.Frontend/src/shared/api/cluster.ts `
  src/MSOSync.Frontend/src/shared/hooks/useHealthTrends.ts `
  src/MSOSync.Frontend/src/features/operations/cluster/HealthTrendsPage.tsx `
  src/MSOSync.Frontend/src/features/operations/cluster/__tests__/HealthTrendsPage.test.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx `
  src/MSOSync.Frontend/src/app/router.tsx `
  src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts

git commit -m "feat(2B.4-T1): Cluster Health Trends — service, endpoint, page, unit tests"
```
