# Epic 10B — Task 6: Topology + Metrics Pages

> Read the master plan first: `docs/superpowers/plans/2026-06-30-epic10b-data-tables.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

**Goal:** Replace Topology and Metrics placeholders. Topology shows summary cards + groups grid. Metrics shows summary cards (30s polling) + runtime row + node/channel grids.

**Files to create** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/topology/columns.ts`
- `features/topology/hooks.ts`
- `features/topology/TopologySummaryCards.tsx`
- `features/topology/TopologyGroupsGrid.tsx`
- `features/metrics/hooks.ts`
- `features/metrics/MetricsSummaryCards.tsx`
- `features/metrics/NodeMetricsGrid.tsx`
- `features/metrics/ChannelMetricsGrid.tsx`

Modify:
- `features/topology/TopologyPage.tsx` — replace PlaceholderPage
- `features/metrics/MetricsPage.tsx` — replace PlaceholderPage

**Interfaces — Consumes (from Tasks 1 & 2):**
- `shared/api/topology.ts` → `getTopologySummary`, `getTopologyGroups`
- `shared/api/metrics.ts` → `getMetricsSummary`, `getNodeMetrics`, `getChannelMetrics`, `getRuntimeMetrics`
- `shared/queryKeys.ts` → `queryKeys`
- `shared/constants/query.ts` → `DASHBOARD_REFRESH_MS`
- `shared/types` → `TopologySummaryDto`, `TopologyGroupDto`, `MetricsSummaryDto`, `NodeMetricsDto`, `ChannelMetricsDto`, `RuntimeMetricsDto`
- `shared/utils/date.ts` → `formatRelativeTime`
- `shared/utils/numbers.ts` → `formatQueueDepth`, `formatPercent`, `formatLatency`, `formatUptime`
- `shared/utils/status.ts` → `nodeStatusVariant`, `connectivityStatusVariant`
- `shared/components/data-display/SummaryCard.tsx`
- `shared/components/data-display/DataGrid.tsx`
- `shared/components/data-display/StatusBadge.tsx`
- `shared/components/feedback/ErrorState.tsx`
- `components/ui/skeleton` (shadcn Skeleton)

---

### Topology Page

- [ ] **Step 1: Create `features/topology/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { TopologyGroupDto } from '../../shared/types';
import { connectivityStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const topologyGroupColumns: ColDef<TopologyGroupDto>[] = [
  { field: 'groupId', headerName: 'Group ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  { field: 'totalNodes', headerName: 'Total', width: 90 },
  { field: 'reachableNodes', headerName: 'Reachable', width: 110 },
  { field: 'degradedNodes', headerName: 'Degraded', width: 110 },
  { field: 'unreachableNodes', headerName: 'Unreachable', width: 120 },
  { field: 'unknownNodes', headerName: 'Unknown', width: 100 },
  {
    field: 'connectivityStatus',
    headerName: 'Status',
    width: 130,
    cellRenderer: (p: ICellRendererParams<TopologyGroupDto>) =>
      p.value
        ? StatusBadge({
            status: p.value as string,
            variant: connectivityStatusVariant(p.value as string),
          })
        : null,
  },
];
```

- [ ] **Step 2: Create `features/topology/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getTopologySummary, getTopologyGroups } from '../../shared/api/topology';

export function useTopologySummary() {
  return useQuery({
    queryKey: queryKeys.topologySummary(),
    queryFn: getTopologySummary,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}

export function useTopologyGroups() {
  return useQuery({
    queryKey: queryKeys.topologyGroups(),
    queryFn: getTopologyGroups,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 3: Create `features/topology/TopologySummaryCards.tsx`**

```tsx
import { Skeleton } from '../../components/ui/skeleton';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { useTopologySummary } from './hooks';

export function TopologySummaryCards() {
  const { data, isLoading, error, refetch } = useTopologySummary();

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />;

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
        {Array.from({ length: 4 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="grid grid-cols-2 gap-4 md:grid-cols-4">
      <SummaryCard title="Total Groups" value={data?.totalGroups ?? '—'} />
      <SummaryCard title="Total Nodes" value={data?.totalNodes ?? '—'} />
      <SummaryCard title="Reachable" value={data?.reachableNodes ?? '—'} variant="success" />
      <SummaryCard title="Unreachable" value={data?.unreachableNodes ?? '—'} variant="danger" />
    </div>
  );
}
```

- [ ] **Step 4: Create `features/topology/TopologyGroupsGrid.tsx`**

```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { topologyGroupColumns } from './columns';
import { useTopologyGroups } from './hooks';

export function TopologyGroupsGrid() {
  const { data, isLoading, error, refetch } = useTopologyGroups();
  return (
    <DataGrid
      rowData={data}
      columnDefs={topologyGroupColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={400}
    />
  );
}
```

- [ ] **Step 5: Replace `features/topology/TopologyPage.tsx`**

```tsx
import { TopologySummaryCards } from './TopologySummaryCards';
import { TopologyGroupsGrid } from './TopologyGroupsGrid';

export function TopologyPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <div>
        <h1 className="text-2xl font-semibold">Topology</h1>
        <p className="text-sm text-neutral-500 dark:text-neutral-400 mt-1">
          React Flow graph view available in Epic 10C.
        </p>
      </div>
      <TopologySummaryCards />
      <div>
        <h2 className="text-base font-semibold mb-3">Node Groups</h2>
        <TopologyGroupsGrid />
      </div>
    </div>
  );
}
```

---

### Metrics Page

- [ ] **Step 6: Create `features/metrics/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import {
  getMetricsSummary,
  getNodeMetrics,
  getChannelMetrics,
  getRuntimeMetrics,
} from '../../shared/api/metrics';
import { DASHBOARD_REFRESH_MS } from '../../shared/constants/query';

export function useMetricsSummary() {
  return useQuery({
    queryKey: queryKeys.metricsSummary(),
    queryFn: getMetricsSummary,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  });
}

export function useRuntimeMetrics() {
  return useQuery({
    queryKey: queryKeys.runtimeMetrics(),
    queryFn: getRuntimeMetrics,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  });
}

export function useNodeMetrics() {
  return useQuery({
    queryKey: queryKeys.nodeMetrics(),
    queryFn: getNodeMetrics,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}

export function useChannelMetrics() {
  return useQuery({
    queryKey: queryKeys.channelMetrics(),
    queryFn: getChannelMetrics,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 7: Create `features/metrics/MetricsSummaryCards.tsx`**

```tsx
import { Skeleton } from '../../components/ui/skeleton';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { formatQueueDepth, formatPercent, formatRelativeTime } from '../../shared/utils/numbers';
import { useMetricsSummary } from './hooks';

// formatRelativeTime is from date.ts not numbers.ts — import from date.ts
import { formatRelativeTime as frt } from '../../shared/utils/date';

export function MetricsSummaryCards() {
  const { data, isLoading, error, refetch } = useMetricsSummary();

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />;

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        {Array.from({ length: 6 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        <SummaryCard title="Queue In" value={data ? formatQueueDepth(data.incomingQueueDepth) : '—'} />
        <SummaryCard title="Queue Out" value={data ? formatQueueDepth(data.outgoingQueueDepth) : '—'} />
        <SummaryCard title="Batches 24h" value={data?.batchesProcessed24h ?? '—'} variant="success" />
        <SummaryCard title="Errors 24h" value={data?.errors24h ?? '—'} variant={data && data.errors24h > 0 ? 'danger' : 'default'} />
        <SummaryCard title="Error Rate" value={data ? formatPercent(data.errorRatePercent) : '—'} variant={data && data.errorRatePercent > 5 ? 'warning' : 'default'} />
        <SummaryCard title="Throughput/min" value={data?.throughputPerMinute ?? '—'} />
      </div>
      {data?.generatedAt && (
        <p className="text-xs text-neutral-500 dark:text-neutral-400">
          Updated {frt(data.generatedAt)}
        </p>
      )}
    </div>
  );
}
```

Note: `formatQueueDepth` and `formatPercent` are from `shared/utils/numbers.ts`. `formatRelativeTime` is from `shared/utils/date.ts`. Both are imported correctly above.

- [ ] **Step 8: Create `features/metrics/NodeMetricsGrid.tsx`**

```tsx
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { NodeMetricsDto } from '../../shared/types';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { nodeStatusVariant } from '../../shared/utils/status';
import { formatRelativeTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { useNodeMetrics } from './hooks';

const nodeMetricColumns: ColDef<NodeMetricsDto>[] = [
  { field: 'nodeId', headerName: 'Node ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 140 },
  {
    field: 'status',
    headerName: 'Status',
    width: 130,
    cellRenderer: (p: ICellRendererParams<NodeMetricsDto>) =>
      p.value
        ? StatusBadge({ status: p.value as string, variant: nodeStatusVariant(p.value as string) })
        : null,
  },
  { field: 'pendingEvents', headerName: 'Pending', width: 100 },
  { field: 'batchesSent', headerName: 'Sent', width: 90 },
  { field: 'batchesReceived', headerName: 'Received', width: 100 },
  {
    field: 'lastHeartbeat',
    headerName: 'Last HB',
    width: 140,
    valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : '—'),
  },
  {
    field: 'probeLatencyMs',
    headerName: 'Latency',
    width: 100,
    valueFormatter: (p) => (p.value != null ? formatLatency(p.value as number) : '—'),
  },
];

export function NodeMetricsGrid() {
  const { data, isLoading, error, refetch } = useNodeMetrics();
  return (
    <DataGrid
      rowData={data}
      columnDefs={nodeMetricColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={350}
    />
  );
}
```

- [ ] **Step 9: Create `features/metrics/ChannelMetricsGrid.tsx`**

```tsx
import type { ColDef } from 'ag-grid-community';
import type { ChannelMetricsDto } from '../../shared/types';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { formatQueueDepth, formatPercent } from '../../shared/utils/numbers';
import { useChannelMetrics } from './hooks';

const channelMetricColumns: ColDef<ChannelMetricsDto>[] = [
  { field: 'channelId', headerName: 'Channel ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  {
    field: 'queueDepth',
    headerName: 'Queue Depth',
    width: 130,
    valueFormatter: (p) => formatQueueDepth(p.value as number),
  },
  {
    field: 'throughputPerMinute',
    headerName: 'Throughput/min',
    width: 150,
    valueFormatter: (p) => String(p.value ?? 0),
  },
  {
    field: 'errorRate',
    headerName: 'Error Rate',
    width: 120,
    valueFormatter: (p) => formatPercent(p.value as number),
  },
];

export function ChannelMetricsGrid() {
  const { data, isLoading, error, refetch } = useChannelMetrics();
  return (
    <DataGrid
      rowData={data}
      columnDefs={channelMetricColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={300}
    />
  );
}
```

- [ ] **Step 10: Replace `features/metrics/MetricsPage.tsx`**

```tsx
import { MetricsSummaryCards } from './MetricsSummaryCards';
import { NodeMetricsGrid } from './NodeMetricsGrid';
import { ChannelMetricsGrid } from './ChannelMetricsGrid';
import { useRuntimeMetrics } from './hooks';
import { formatUptime, formatPercent } from '../../shared/utils/numbers';

function RuntimeRow() {
  const { data, isLoading } = useRuntimeMetrics();
  if (isLoading || !data) return null;
  return (
    <div className="flex flex-wrap gap-6 rounded-lg border border-neutral-200 dark:border-neutral-800 p-4 text-sm">
      <span><span className="font-medium">Uptime:</span> {formatUptime(data.uptimeSeconds)}</span>
      <span><span className="font-medium">Memory:</span> {data.memoryMb} MB</span>
      <span><span className="font-medium">CPU:</span> {formatPercent(data.cpuPercent)}</span>
      <span><span className="font-medium">Workers:</span> {data.activeWorkers}</span>
    </div>
  );
}

export function MetricsPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <h1 className="text-2xl font-semibold">Metrics</h1>
      <MetricsSummaryCards />
      <RuntimeRow />
      <div>
        <h2 className="text-base font-semibold mb-3">Node Metrics</h2>
        <NodeMetricsGrid />
      </div>
      <div>
        <h2 className="text-base font-semibold mb-3">Channel Metrics</h2>
        <ChannelMetricsGrid />
      </div>
    </div>
  );
}
```

- [ ] **Step 11: Fix import in `MetricsSummaryCards.tsx`**

The file above has a duplicate import pattern. Clean it up — use only one import for `formatRelativeTime` from `'../../shared/utils/date'`. Remove the line `import { formatRelativeTime } from '../../shared/utils/numbers'` that was included by mistake in the template. The final `MetricsSummaryCards.tsx` imports should be:

```typescript
import { Skeleton } from '../../components/ui/skeleton';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { formatQueueDepth, formatPercent } from '../../shared/utils/numbers';
import { formatRelativeTime } from '../../shared/utils/date';
import { useMetricsSummary } from './hooks';
```

And use `formatRelativeTime` (not `frt`) for the "Updated" line.

- [ ] **Step 12: Verify build + lint + tests pass**

Run from `src/MSOSync.Frontend/`:
```
npm run build
```
Expected: exits 0.

```
npm run lint
```
Expected: 0 errors.

```
npm test
```
Expected: 12/12 pass.

- [ ] **Step 13: Commit**

```
git add src/MSOSync.Frontend/src/features/topology/
git add src/MSOSync.Frontend/src/features/metrics/
git commit -m "feat(10b): wire topology and metrics pages"
```

---

## Report Contract

Write report to the path specified by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files created/modified (count)
- Build result
- Lint result
- Test result (N/12 pass)
- Any concerns
