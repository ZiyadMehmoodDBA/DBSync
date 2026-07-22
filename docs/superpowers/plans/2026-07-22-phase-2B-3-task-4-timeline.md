# Task 4 — Operations Timeline

**Files:**
- Create: `src/MSOSync.Metadata/Operations/Timeline/Dtos/OperationTimelineDto.cs`
- Create: `src/MSOSync.Metadata/Operations/Timeline/IOperationTimelineService.cs`
- Create: `src/MSOSync.Metadata/Operations/Timeline/OperationTimelineService.cs`
- Modify: `src/MSOSync.Metadata/MetadataServiceExtensions.cs`
- Modify: `src/MSOSync.Api/Controllers/OperationsController.cs`
- Create: `tests/MSOSync.MetadataTests/Operations/Timeline/OperationTimelineServiceTests.cs`
- Create: `src/MSOSync.Frontend/src/shared/types/timeline.ts`
- Create: `src/MSOSync.Frontend/src/shared/api/operationTimeline.ts`
- Create: `src/MSOSync.Frontend/src/shared/hooks/useOperationTimeline.ts`
- Create: `src/MSOSync.Frontend/src/features/operations/timeline/TimelinePage.tsx`
- Create: `src/MSOSync.Frontend/src/features/operations/timeline/__tests__/TimelinePage.test.tsx`
- Modify: `src/MSOSync.Frontend/src/app/router.tsx`
- Modify: `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`

**Interfaces:**
- Consumes: `SyncOperation` fields: `OperationId (Guid)`, `OperationType (string)`, `Status (string)`, `StartedAt (DateTime)`, `CompletedAt (DateTime?)`, `ProgressPercent (int?)`, `ProgressMessage (string?)`, `Summary (string?)`
- Produces: `IOperationTimelineService.GetTimelineAsync(DateTime from, DateTime to, string[]? types, int limit, CancellationToken) → Task<OperationTimelineDto>`
- Produces: `GET /api/v1/operations/timeline?from=&to=&types=&limit=200` → 200 `OperationTimelineDto` | 400
- Produces: `useOperationTimeline(from, to, types)` hook
- Produces: `/operations/timeline` route + nav item

---

- [ ] **Step 1: Create DTOs**

```csharp
// src/MSOSync.Metadata/Operations/Timeline/Dtos/OperationTimelineDto.cs
namespace MSOSync.Metadata.Operations.Timeline.Dtos;

public sealed record OperationTimelineDto(
    IReadOnlyList<OperationTimelineItemDto> Items,
    DateTime  From,
    DateTime  To,
    bool      HasMore,
    int       ReturnedCount);

public sealed record OperationTimelineItemDto(
    Guid      OperationId,
    string    Type,
    string    Status,
    string?   Label,
    DateTime  StartedAt,
    DateTime? CompletedAt,
    int?      ProgressPercent);
```

- [ ] **Step 2: Create interface**

```csharp
// src/MSOSync.Metadata/Operations/Timeline/IOperationTimelineService.cs
using MSOSync.Metadata.Operations.Timeline.Dtos;

namespace MSOSync.Metadata.Operations.Timeline;

public interface IOperationTimelineService
{
    Task<OperationTimelineDto> GetTimelineAsync(
        DateTime   from,
        DateTime   to,
        string[]?  types,
        int        limit,
        CancellationToken ct = default);
}
```

- [ ] **Step 3: Write failing unit tests**

```csharp
// tests/MSOSync.MetadataTests/Operations/Timeline/OperationTimelineServiceTests.cs
using FluentAssertions;
using MSOSync.Metadata.Operations.Timeline;
using MSOSync.Persistence.Entities;
using Xunit;

namespace MSOSync.MetadataTests.Operations.Timeline;

public sealed class OperationTimelineServiceTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContext.Create();

    public void Dispose() => _db.Dispose();

    private async Task<Guid> SeedOpAsync(
        string type, string status,
        DateTime startedAt, DateTime? completedAt = null)
    {
        var id = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId   = id,
            OperationType = type,
            Status        = status,
            Source        = "User",
            StartedAt     = startedAt,
            CompletedAt   = completedAt,
            CanCancel     = false,
            CanRetry      = false,
            TenantId      = Guid.Empty,
        });
        await _db.SaveChangesAsync();
        return id;
    }

    [Fact]
    public async Task GetTimelineAsync_returns_operations_in_range()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        await SeedOpAsync("Export", "Completed", DateTime.UtcNow.AddHours(-1), DateTime.UtcNow.AddMinutes(-30));
        await SeedOpAsync("Export", "Completed", DateTime.UtcNow.AddHours(-5)); // outside range

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 200);

        result.Items.Should().HaveCount(1);
    }

    [Fact]
    public async Task GetTimelineAsync_filters_by_type()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        await SeedOpAsync("Export",     "Completed", DateTime.UtcNow.AddHours(-1));
        await SeedOpAsync("BatchReplay","Running",   DateTime.UtcNow.AddMinutes(-30));

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, ["Export"], 200);

        result.Items.Should().HaveCount(1);
        result.Items[0].Type.Should().Be("Export");
    }

    [Fact]
    public async Task GetTimelineAsync_HasMore_true_when_exceeds_limit()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        for (var i = 0; i < 6; i++)
            await SeedOpAsync("Export", "Completed", DateTime.UtcNow.AddMinutes(-i - 1));

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 5);

        result.HasMore.Should().BeTrue();
        result.Items.Should().HaveCount(5);
        result.ReturnedCount.Should().Be(5);
    }

    [Fact]
    public async Task GetTimelineAsync_orders_by_startedAt_then_operationId()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        var t = DateTime.UtcNow.AddHours(-1);
        var id1 = await SeedOpAsync("Export", "Completed", t);
        var id2 = await SeedOpAsync("Export", "Completed", t); // same time, sort by id

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 200);

        var ids = result.Items.Select(i => i.OperationId).ToList();
        ids.Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task GetTimelineAsync_label_uses_progressMessage_then_summary_then_type()
    {
        var from = DateTime.UtcNow.AddHours(-2);
        var to   = DateTime.UtcNow;
        var id = Guid.NewGuid();
        _db.Operations.Add(new SyncOperation
        {
            OperationId = id, OperationType = "Export", Status = "Running",
            Source = "Worker", StartedAt = DateTime.UtcNow.AddMinutes(-10),
            ProgressMessage = "Processing batch 3 of 10",
            Summary = "Export job", CanCancel = false, CanRetry = false, TenantId = Guid.Empty,
        });
        await _db.SaveChangesAsync();

        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(from, to, null, 200);

        result.Items[0].Label.Should().Be("Processing batch 3 of 10");
    }

    [Fact]
    public async Task GetTimelineAsync_empty_db_returns_empty_result()
    {
        var svc = new OperationTimelineService(_db);
        var result = await svc.GetTimelineAsync(
            DateTime.UtcNow.AddDays(-1), DateTime.UtcNow, null, 200);
        result.Items.Should().BeEmpty();
        result.HasMore.Should().BeFalse();
    }
}
```

- [ ] **Step 4: Run tests — expect failures**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~OperationTimelineServiceTests" -v normal
```

Expected: compilation errors (service doesn't exist).

- [ ] **Step 5: Implement `OperationTimelineService`**

```csharp
// src/MSOSync.Metadata/Operations/Timeline/OperationTimelineService.cs
using Microsoft.EntityFrameworkCore;
using MSOSync.Metadata.Operations.Timeline.Dtos;
using MSOSync.Persistence;

namespace MSOSync.Metadata.Operations.Timeline;

public sealed class OperationTimelineService(AppDbContext db) : IOperationTimelineService
{
    public async Task<OperationTimelineDto> GetTimelineAsync(
        DateTime  from,
        DateTime  to,
        string[]? types,
        int       limit,
        CancellationToken ct = default)
    {
        var q = db.Operations
            .AsNoTracking()
            .Where(o => o.StartedAt >= from && o.StartedAt <= to);

        if (types is { Length: > 0 })
            q = q.Where(o => types.Contains(o.OperationType));

        // Fetch limit+1 to detect HasMore
        var fetchLimit = Math.Min(limit, 500) + 1;
        var rows = await q
            .OrderBy(o => o.StartedAt)
            .ThenBy(o => o.OperationId)
            .Take(fetchLimit)
            .Select(o => new OperationTimelineItemDto(
                o.OperationId,
                o.OperationType,
                o.Status,
                o.ProgressMessage ?? o.Summary ?? o.OperationType,
                o.StartedAt,
                o.CompletedAt,
                o.ProgressPercent))
            .ToListAsync(ct);

        var hasMore = rows.Count > limit;
        if (hasMore) rows = rows.Take(limit).ToList();

        return new OperationTimelineDto(
            Items:         rows.AsReadOnly(),
            From:          from,
            To:            to,
            HasMore:       hasMore,
            ReturnedCount: rows.Count);
    }
}
```

- [ ] **Step 6: Run tests — expect pass**

```
dotnet test tests/MSOSync.MetadataTests --filter "FullyQualifiedName~OperationTimelineServiceTests" -v normal
```

Expected: all 6 tests PASS.

- [ ] **Step 7: Register service and add timeline endpoint to `OperationsController`**

In `MetadataServiceExtensions.cs`, add to Phase 2B.3 block:
```csharp
        services.AddScoped<IOperationTimelineService, OperationTimelineService>();
```

Open `src/MSOSync.Api/Controllers/OperationsController.cs`.

Add to usings:
```csharp
using MSOSync.Metadata.Operations.Timeline;
using MSOSync.Metadata.Operations.Timeline.Dtos;
```

Add `IOperationTimelineService timelineSvc` to the primary constructor:
```csharp
public sealed class OperationsController(
    IOperationQueryService      queryService,
    IOperationService           operationService,
    IPermissionService          permissions,
    IOperationTimelineService   timelineSvc,
    OperationsPageSizeValidator pageSizeValidator) : ControllerBase
```

Add timeline endpoint method at end of class:
```csharp
    // GET /api/v1/operations/timeline?from=&to=&types=&limit=200
    [HttpGet("timeline")]
    [ProducesResponseType(typeof(OperationTimelineDto), 200)]
    [ProducesResponseType(typeof(ProblemDetails), 400)]
    public async Task<IActionResult> GetTimeline(
        [FromQuery] DateTime  from,
        [FromQuery] DateTime  to,
        [FromQuery] string?   types  = null,
        [FromQuery] int       limit  = 200,
        CancellationToken ct = default)
    {
        if (from >= to)
            return BadRequest(new ProblemDetails { Title = "from must be before to." });
        if ((to - from).TotalDays > 30)
            return BadRequest(new ProblemDetails { Title = "Range cannot exceed 30 days." });

        var typeArray = SplitCsv(types);
        var result    = await timelineSvc.GetTimelineAsync(from, to, typeArray, Math.Min(limit, 500), ct);
        return Ok(result);
    }
```

- [ ] **Step 8: Build backend**

```
dotnet build src/MSOSync.Api/MSOSync.Api.csproj
```

Expected: 0 errors.

- [ ] **Step 9: Create TypeScript types**

```typescript
// src/MSOSync.Frontend/src/shared/types/timeline.ts
export type OperationType =
  | 'Export' | 'Rollout' | 'Decommission' | 'Recovery'
  | 'RollingMaintenance' | 'RollingUpgrade' | 'BatchReplay';

export interface OperationTimelineItemDto {
  operationId:     string;
  type:            string;
  status:          string;
  label:           string | null;
  startedAt:       string;   // ISO UTC
  completedAt:     string | null; // ISO UTC, null = still running
  progressPercent: number | null;
}

export interface OperationTimelineDto {
  items:          OperationTimelineItemDto[];
  from:           string;
  to:             string;
  hasMore:        boolean;
  returnedCount:  number;
}
```

- [ ] **Step 10: Create API function**

```typescript
// src/MSOSync.Frontend/src/shared/api/operationTimeline.ts
import client from './client';
import type { OperationTimelineDto } from '../types/timeline';

export const timelineKeys = {
  list: (from: string, to: string, types: string[]) =>
    ['operation-timeline', from, to, types] as const,
} as const;

export async function getOperationTimeline(
  from:     string,
  to:       string,
  types?:   string[],
  limit?:   number,
  options?: { signal?: AbortSignal },
): Promise<OperationTimelineDto> {
  const params: Record<string, string | number | undefined> = {
    from,
    to,
    limit: limit ?? 200,
  };
  if (types && types.length > 0) params.types = types.join(',');

  const { data } = await client.get<OperationTimelineDto>('/operations/timeline', {
    params,
    ...options,
  });
  return data;
}
```

- [ ] **Step 11: Create hook**

```typescript
// src/MSOSync.Frontend/src/shared/hooks/useOperationTimeline.ts
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { useEffect } from 'react';
import { timelineKeys, getOperationTimeline } from '../api/operationTimeline';
import { useSignalRContext } from '../signalr/context';

export function useOperationTimeline(
  from:  string,
  to:    string,
  types: string[],
) {
  const qc = useQueryClient();
  const { connection } = useSignalRContext();

  useEffect(() => {
    if (!connection) return;
    const key = timelineKeys.list(from, to, types);
    const handler = () => void qc.invalidateQueries({ queryKey: key });
    connection.on('OperationChanged', handler);
    return () => connection.off('OperationChanged', handler);
  }, [connection, qc, from, to, types]);

  return useQuery({
    queryKey: timelineKeys.list(from, to, types),
    queryFn:  ({ signal }) => getOperationTimeline(from, to, types, 200, { signal }),
    enabled:  !!from && !!to,
    staleTime: 30_000,
  });
}
```

- [ ] **Step 12: Create `TimelinePage.tsx`**

```tsx
// src/MSOSync.Frontend/src/features/operations/timeline/TimelinePage.tsx
import { useState, useMemo } from 'react';
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer, Cell,
} from 'recharts';
import { useOperationTimeline } from '@/shared/hooks/useOperationTimeline';
import { Button } from '@/components/ui/button';
import { AlertTriangle } from 'lucide-react';
import { subHours, subDays, format, parseISO } from 'date-fns';

const ALL_TYPES = [
  'Export', 'Rollout', 'Decommission', 'Recovery',
  'RollingMaintenance', 'RollingUpgrade', 'BatchReplay',
];

const STATUS_COLOR: Record<string, string> = {
  Running:   '#3b82f6',
  Completed: '#22c55e',
  Failed:    '#ef4444',
  Cancelled: '#9ca3af',
  Pending:   '#f59e0b',
};

function toIso(d: Date): string {
  return d.toISOString();
}

function defaultRange() {
  const to   = new Date();
  const from = subHours(to, 24);
  return { from: toIso(from), to: toIso(to) };
}

interface GanttDatum {
  name:        string;
  operationId: string;
  start:       number;   // epoch ms
  duration:    number;   // ms
  status:      string;
  label:       string;
  startMs:     number;
  endMs:       number;
}

export default function TimelinePage() {
  const [range, setRange]           = useState(defaultRange);
  const [selectedTypes, setTypes]   = useState<string[]>([]);

  const { data, isFetching } = useOperationTimeline(range.from, range.to, selectedTypes);

  const nowMs = Date.now();

  const ganttData: GanttDatum[] = useMemo(() => {
    if (!data) return [];
    return data.items.map(item => {
      const startMs = parseISO(item.startedAt).getTime();
      const endMs   = item.completedAt ? parseISO(item.completedAt).getTime() : nowMs;
      return {
        name:        item.type,
        operationId: item.operationId,
        start:       startMs,
        duration:    Math.max(endMs - startMs, 60_000), // min 1 min for visibility
        status:      item.status,
        label:       item.label ?? item.type,
        startMs,
        endMs,
      };
    });
  }, [data, nowMs]);

  const domainMin = ganttData.length > 0 ? Math.min(...ganttData.map(d => d.startMs)) : Date.now() - 86_400_000;
  const domainMax = nowMs;

  const toggleType = (t: string) =>
    setTypes(prev => prev.includes(t) ? prev.filter(x => x !== t) : [...prev, t]);

  return (
    <div className="p-6 space-y-4">
      <h1 className="text-2xl font-semibold">Operations Timeline</h1>

      {/* Controls */}
      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <label className="text-xs text-muted-foreground">From (UTC)</label>
          <input
            type="datetime-local"
            className="rounded border bg-background px-2 py-1 text-sm"
            value={range.from.slice(0, 16)}
            onChange={e => setRange(r => ({ ...r, from: new Date(e.target.value).toISOString() }))}
          />
        </div>
        <div className="flex items-center gap-2">
          <label className="text-xs text-muted-foreground">To (UTC)</label>
          <input
            type="datetime-local"
            className="rounded border bg-background px-2 py-1 text-sm"
            value={range.to.slice(0, 16)}
            onChange={e => setRange(r => ({ ...r, to: new Date(e.target.value).toISOString() }))}
          />
        </div>

        {/* Quick range buttons */}
        <div className="flex gap-1">
          {[
            { label: '1h',  fn: () => ({ from: toIso(subHours(new Date(), 1)),  to: toIso(new Date()) }) },
            { label: '24h', fn: () => ({ from: toIso(subHours(new Date(), 24)), to: toIso(new Date()) }) },
            { label: '7d',  fn: () => ({ from: toIso(subDays(new Date(), 7)),   to: toIso(new Date()) }) },
          ].map(({ label, fn }) => (
            <Button key={label} variant="outline" size="sm" onClick={() => setRange(fn())}>
              {label}
            </Button>
          ))}
        </div>

        {/* Type filter chips */}
        <div className="flex flex-wrap gap-1">
          {ALL_TYPES.map(t => (
            <button
              key={t}
              className={`rounded-full px-2 py-0.5 text-xs font-medium border transition-colors ${
                selectedTypes.includes(t) || selectedTypes.length === 0
                  ? 'bg-primary text-primary-foreground border-primary'
                  : 'border-border text-muted-foreground hover:border-primary'
              }`}
              onClick={() => toggleType(t)}
            >
              {t}
            </button>
          ))}
          {selectedTypes.length > 0 && (
            <button className="text-xs text-muted-foreground underline" onClick={() => setTypes([])}>
              Clear
            </button>
          )}
        </div>
      </div>

      {/* HasMore warning */}
      {data?.hasMore && (
        <div className="flex items-center gap-2 rounded border border-amber-300 bg-amber-50 dark:bg-amber-950/20 px-3 py-2 text-sm text-amber-700 dark:text-amber-400">
          <AlertTriangle className="h-4 w-4 shrink-0" />
          Showing {data.returnedCount} of more operations — narrow the time range or add type filters to see all.
        </div>
      )}

      {/* Chart */}
      {isFetching ? (
        <div className="h-64 flex items-center justify-center text-sm text-muted-foreground">
          Loading timeline…
        </div>
      ) : ganttData.length === 0 ? (
        <div className="h-64 flex items-center justify-center rounded-lg border bg-card text-sm text-muted-foreground">
          No operations in this range.
        </div>
      ) : (
        <div className="rounded-lg border bg-card p-4">
          <ResponsiveContainer width="100%" height={Math.max(ganttData.length * 36, 200)}>
            <BarChart
              layout="vertical"
              data={ganttData}
              margin={{ top: 8, right: 24, bottom: 8, left: 120 }}
            >
              <XAxis
                type="number"
                domain={[domainMin, domainMax]}
                tickFormatter={v => format(new Date(v), 'HH:mm')}
                scale="linear"
              />
              <YAxis
                type="category"
                dataKey="name"
                width={110}
                tick={{ fontSize: 11 }}
              />
              <Tooltip
                content={({ active, payload }) => {
                  if (!active || !payload?.[0]) return null;
                  const d = payload[0].payload as GanttDatum;
                  const durationMs = d.endMs - d.startMs;
                  const mins = Math.round(durationMs / 60_000);
                  return (
                    <div className="rounded border bg-background shadow-md px-3 py-2 text-xs space-y-1">
                      <p className="font-semibold">{d.label}</p>
                      <p className="text-muted-foreground">{d.status}</p>
                      <p>{format(new Date(d.startMs), 'HH:mm:ss')} UTC</p>
                      <p>{mins < 60 ? `${mins}m` : `${Math.floor(mins / 60)}h ${mins % 60}m`}</p>
                    </div>
                  );
                }}
              />
              {/* Invisible bar from 0 to start for offset */}
              <Bar dataKey="start" stackId="g" fill="transparent" />
              {/* Visible bar from start to end */}
              <Bar dataKey="duration" stackId="g" radius={[0, 3, 3, 0]}>
                {ganttData.map((entry, i) => (
                  <Cell
                    key={i}
                    fill={STATUS_COLOR[entry.status] ?? '#9ca3af'}
                  />
                ))}
              </Bar>
            </BarChart>
          </ResponsiveContainer>

          {/* Legend */}
          <div className="flex flex-wrap gap-3 mt-3 px-1">
            {Object.entries(STATUS_COLOR).map(([status, color]) => (
              <div key={status} className="flex items-center gap-1.5 text-xs">
                <span className="h-2.5 w-2.5 rounded-sm" style={{ backgroundColor: color }} />
                {status}
              </div>
            ))}
          </div>
        </div>
      )}
    </div>
  );
}
```

- [ ] **Step 13: Write frontend test**

```typescript
// src/MSOSync.Frontend/src/features/operations/timeline/__tests__/TimelinePage.test.tsx
import { describe, it, expect, vi, beforeEach } from 'vitest';
import { render, screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import TimelinePage from '../TimelinePage';
import * as api from '@/shared/api/operationTimeline';
import type { OperationTimelineDto } from '@/shared/types/timeline';

vi.mock('@/shared/api/operationTimeline');
vi.mock('@/shared/signalr/context', () => ({
  useSignalRContext: () => ({ connection: null, connectionState: 'disconnected' }),
}));
// Recharts resize observer not available in JSDOM
vi.mock('recharts', () => ({
  BarChart: ({ children }: { children: React.ReactNode }) => <div data-testid="bar-chart">{children}</div>,
  Bar: () => null,
  XAxis: () => null,
  YAxis: () => null,
  Tooltip: () => null,
  ResponsiveContainer: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  Cell: () => null,
}));

const emptyTimeline: OperationTimelineDto = {
  items: [], from: '', to: '', hasMore: false, returnedCount: 0,
};

function wrapper({ children }: { children: React.ReactNode }) {
  const qc = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  return <QueryClientProvider client={qc}>{children}</QueryClientProvider>;
}

describe('TimelinePage', () => {
  beforeEach(() => {
    vi.mocked(api.getOperationTimeline).mockResolvedValue(emptyTimeline);
  });

  it('renders page heading', async () => {
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText('Operations Timeline')).toBeInTheDocument();
  });

  it('shows empty state message when no operations', async () => {
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText('No operations in this range.')).toBeInTheDocument();
  });

  it('renders chart when operations present', async () => {
    const now = Date.now();
    const data: OperationTimelineDto = {
      items: [{
        operationId: 'op-1', type: 'Export', status: 'Completed',
        label: 'Export job', startedAt: new Date(now - 60_000).toISOString(),
        completedAt: new Date(now - 30_000).toISOString(), progressPercent: 100,
      }],
      from: new Date(now - 3_600_000).toISOString(),
      to:   new Date(now).toISOString(),
      hasMore: false, returnedCount: 1,
    };
    vi.mocked(api.getOperationTimeline).mockResolvedValue(data);
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByTestId('bar-chart')).toBeInTheDocument();
  });

  it('shows HasMore banner when hasMore is true', async () => {
    const now = Date.now();
    vi.mocked(api.getOperationTimeline).mockResolvedValue({
      ...emptyTimeline,
      hasMore: true, returnedCount: 200,
      items: [],
    });
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText(/narrow the time range/i)).toBeInTheDocument();
  });

  it('renders type filter buttons', async () => {
    render(<TimelinePage />, { wrapper });
    expect(await screen.findByText('Export')).toBeInTheDocument();
    expect(await screen.findByText('BatchReplay')).toBeInTheDocument();
  });
});
```

- [ ] **Step 14: Add route and nav item**

In `src/MSOSync.Frontend/src/app/router.tsx`, add:
```tsx
// Add import:
const TimelinePage = lazy(() => import('../features/operations/timeline/TimelinePage'));

// Add route in operations children (after Activity/AuditPage route):
{ path: 'timeline', element: <Suspense fallback={<PageLoader />}><TimelinePage /></Suspense> },
```

In `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`, add to Operations group after Activity:
```tsx
// Add import at top:
import { Calendar } from 'lucide-react';

// In NAV_GROUPS Operations items, after Activity:
{ label: 'Timeline',  path: '/operations/timeline',  icon: Calendar },
```

- [ ] **Step 15: Build frontend**

```
cd src/MSOSync.Frontend && npm run build
```

Expected: 0 TypeScript errors.

- [ ] **Step 16: Run frontend tests**

```
cd src/MSOSync.Frontend && npm test -- TimelinePage
```

Expected: 5 tests PASS.

- [ ] **Step 17: Run full backend tests**

```
dotnet test tests/MSOSync.MetadataTests -v normal
```

Expected: all MetadataTests PASS (OperationTimelineServiceTests included).

- [ ] **Step 18: Commit**

```
git add src/MSOSync.Metadata/Operations/Timeline/
git add src/MSOSync.Metadata/MetadataServiceExtensions.cs
git add src/MSOSync.Api/Controllers/OperationsController.cs
git add tests/MSOSync.MetadataTests/Operations/Timeline/
git add src/MSOSync.Frontend/src/shared/types/timeline.ts
git add src/MSOSync.Frontend/src/shared/api/operationTimeline.ts
git add src/MSOSync.Frontend/src/shared/hooks/useOperationTimeline.ts
git add src/MSOSync.Frontend/src/features/operations/timeline/
git add src/MSOSync.Frontend/src/app/router.tsx
git add src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git commit -m "feat(2B.3-T4): Operations Timeline — service, Gantt endpoint, TimelinePage with Recharts"
```
