# Epic 10B — Task 5: Nodes + Config Pages (Client-Side)

> Read the master plan first: `docs/superpowers/plans/2026-06-30-epic10b-data-tables.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

**Goal:** Replace 4 placeholder pages (Nodes, Channels, Triggers, Routers) with client-side AG Grid pages. All fetch once and let AG Grid handle pagination, sorting, and filtering.

**Files to create** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/nodes/columns.ts`
- `features/nodes/hooks.ts`
- `features/nodes/NodesGrid.tsx`
- `features/channels/columns.ts`
- `features/channels/hooks.ts`
- `features/channels/ChannelsGrid.tsx`
- `features/triggers/columns.ts`
- `features/triggers/hooks.ts`
- `features/triggers/TriggersGrid.tsx`
- `features/routers/columns.ts`
- `features/routers/hooks.ts`
- `features/routers/RoutersGrid.tsx`

Modify:
- `features/nodes/NodesPage.tsx` — replace PlaceholderPage
- `features/channels/ChannelsPage.tsx` — replace PlaceholderPage
- `features/triggers/TriggersPage.tsx` — replace PlaceholderPage
- `features/routers/RoutersPage.tsx` — replace PlaceholderPage

**Interfaces — Consumes (from Tasks 1 & 2):**
- `shared/api/nodes.ts` → `getNodes`
- `shared/api/channels.ts` → `getChannels`
- `shared/api/triggers.ts` → `getTriggers`
- `shared/api/routers.ts` → `getRouters`
- `shared/queryKeys.ts` → `queryKeys`
- `shared/types` → `NodeDto`, `ChannelDto`, `TriggerDto`, `RouterDto`
- `shared/utils/date.ts` → `formatDateTime`, `formatRelativeTime`
- `shared/utils/numbers.ts` → `formatLatency`
- `shared/utils/status.ts` → `nodeStatusVariant`
- `shared/components/data-display/DataGrid.tsx`
- `shared/components/data-display/StatusBadge.tsx`

**Pattern for all client-side pages:**
1. `columns.ts` — `ColDef<T>[]` export
2. `hooks.ts` — `useQuery` with `staleTime: 60_000`, `refetchOnWindowFocus: false`
3. `*Grid.tsx` — hook + DataGrid; exposes `quickFilterText` prop
4. `*Page.tsx` — `<h1>` + search input + grid; quick search filters client-side

---

### Nodes Page

- [ ] **Step 1: Create `features/nodes/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { NodeDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { nodeStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const nodeColumns: ColDef<NodeDto>[] = [
  { field: 'nodeId', headerName: 'Node ID', width: 180 },
  { field: 'groupId', headerName: 'Group', width: 150 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  {
    field: 'status',
    headerName: 'Status',
    width: 130,
    cellRenderer: (p: ICellRendererParams<NodeDto>) =>
      p.value
        ? StatusBadge({ status: p.value as string, variant: nodeStatusVariant(p.value as string) })
        : null,
  },
  {
    field: 'syncEnabled',
    headerName: 'Sync',
    width: 90,
    valueFormatter: (p) => (p.value ? 'Yes' : 'No'),
  },
  {
    field: 'lastHeartbeat',
    headerName: 'Last Heartbeat',
    width: 150,
    valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : '—'),
  },
  {
    field: 'probeLatencyMs',
    headerName: 'Latency',
    width: 100,
    valueFormatter: (p) => (p.value != null ? formatLatency(p.value as number) : '—'),
  },
  {
    field: 'createdTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
```

- [ ] **Step 2: Create `features/nodes/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getNodes } from '../../shared/api/nodes';

export function useNodes() {
  return useQuery({
    queryKey: queryKeys.nodes(),
    queryFn: getNodes,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 3: Create `features/nodes/NodesGrid.tsx`**

```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { nodeColumns } from './columns';
import { useNodes } from './hooks';

interface Props {
  quickFilterText?: string;
}

export function NodesGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useNodes();
  return (
    <DataGrid
      rowData={data}
      columnDefs={nodeColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
```

- [ ] **Step 4: Replace `features/nodes/NodesPage.tsx`**

```tsx
import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { NodesGrid } from './NodesGrid';

export function NodesPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Nodes</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search nodes…"
        className="max-w-xs"
      />
      <NodesGrid quickFilterText={search} />
    </div>
  );
}
```

---

### Channels Page

- [ ] **Step 5: Create `features/channels/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { ChannelDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const channelColumns: ColDef<ChannelDto>[] = [
  { field: 'channelId', headerName: 'Channel ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  { field: 'description', headerName: 'Description', flex: 1, minWidth: 200 },
  {
    field: 'enabled',
    headerName: 'Enabled',
    width: 110,
    cellRenderer: (p: ICellRendererParams<ChannelDto>) =>
      StatusBadge({
        status: p.value ? 'Enabled' : 'Disabled',
        variant: p.value ? 'success' : 'neutral',
      }),
  },
  {
    field: 'createdTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
```

- [ ] **Step 6: Create `features/channels/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getChannels } from '../../shared/api/channels';

export function useChannels() {
  return useQuery({
    queryKey: queryKeys.channels(),
    queryFn: getChannels,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 7: Create `features/channels/ChannelsGrid.tsx`**

```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { channelColumns } from './columns';
import { useChannels } from './hooks';

interface Props { quickFilterText?: string; }

export function ChannelsGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useChannels();
  return (
    <DataGrid
      rowData={data}
      columnDefs={channelColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
```

- [ ] **Step 8: Replace `features/channels/ChannelsPage.tsx`**

```tsx
import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { ChannelsGrid } from './ChannelsGrid';

export function ChannelsPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Channels</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search channels…" className="max-w-xs" />
      <ChannelsGrid quickFilterText={search} />
    </div>
  );
}
```

---

### Triggers Page

- [ ] **Step 9: Create `features/triggers/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { TriggerDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const triggerColumns: ColDef<TriggerDto>[] = [
  { field: 'triggerId', headerName: 'Trigger ID', width: 180 },
  { field: 'channelId', headerName: 'Channel', width: 150 },
  { field: 'schemaName', headerName: 'Schema', width: 120 },
  { field: 'tableName', headerName: 'Table', width: 150 },
  {
    field: 'captureInsert',
    headerName: 'Insert',
    width: 80,
    valueFormatter: (p) => (p.value ? '✓' : '—'),
  },
  {
    field: 'captureUpdate',
    headerName: 'Update',
    width: 80,
    valueFormatter: (p) => (p.value ? '✓' : '—'),
  },
  {
    field: 'captureDelete',
    headerName: 'Delete',
    width: 80,
    valueFormatter: (p) => (p.value ? '✓' : '—'),
  },
  {
    field: 'enabled',
    headerName: 'Enabled',
    width: 110,
    cellRenderer: (p: ICellRendererParams<TriggerDto>) =>
      StatusBadge({
        status: p.value ? 'Enabled' : 'Disabled',
        variant: p.value ? 'success' : 'neutral',
      }),
  },
  {
    field: 'createdTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
```

- [ ] **Step 10: Create `features/triggers/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getTriggers } from '../../shared/api/triggers';

export function useTriggers() {
  return useQuery({
    queryKey: queryKeys.triggers(),
    queryFn: getTriggers,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 11: Create `features/triggers/TriggersGrid.tsx`**

```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { triggerColumns } from './columns';
import { useTriggers } from './hooks';

interface Props { quickFilterText?: string; }

export function TriggersGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useTriggers();
  return (
    <DataGrid
      rowData={data}
      columnDefs={triggerColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
```

- [ ] **Step 12: Replace `features/triggers/TriggersPage.tsx`**

```tsx
import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { TriggersGrid } from './TriggersGrid';

export function TriggersPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Triggers</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search triggers…" className="max-w-xs" />
      <TriggersGrid quickFilterText={search} />
    </div>
  );
}
```

---

### Routers Page

- [ ] **Step 13: Create `features/routers/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { RouterDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const routerColumns: ColDef<RouterDto>[] = [
  { field: 'routerId', headerName: 'Router ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  { field: 'sourceGroupId', headerName: 'Source Group', width: 160 },
  { field: 'targetGroupId', headerName: 'Target Group', width: 160 },
  {
    field: 'channelIds',
    headerName: 'Channels',
    width: 200,
    valueFormatter: (p) => {
      const ids = p.value as string[] | undefined;
      return ids ? ids.join(', ') : '—';
    },
  },
  {
    field: 'enabled',
    headerName: 'Enabled',
    width: 110,
    cellRenderer: (p: ICellRendererParams<RouterDto>) =>
      StatusBadge({
        status: p.value ? 'Enabled' : 'Disabled',
        variant: p.value ? 'success' : 'neutral',
      }),
  },
  {
    field: 'createdTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
```

- [ ] **Step 14: Create `features/routers/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getRouters } from '../../shared/api/routers';

export function useRouters() {
  return useQuery({
    queryKey: queryKeys.routers(),
    queryFn: getRouters,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 15: Create `features/routers/RoutersGrid.tsx`**

```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { routerColumns } from './columns';
import { useRouters } from './hooks';

interface Props { quickFilterText?: string; }

export function RoutersGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useRouters();
  return (
    <DataGrid
      rowData={data}
      columnDefs={routerColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
```

- [ ] **Step 16: Replace `features/routers/RoutersPage.tsx`**

```tsx
import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { RoutersGrid } from './RoutersGrid';

export function RoutersPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Routers</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search routers…" className="max-w-xs" />
      <RoutersGrid quickFilterText={search} />
    </div>
  );
}
```

- [ ] **Step 17: Verify build + lint + tests pass**

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

- [ ] **Step 18: Commit**

```
git add src/MSOSync.Frontend/src/features/nodes/
git add src/MSOSync.Frontend/src/features/channels/
git add src/MSOSync.Frontend/src/features/triggers/
git add src/MSOSync.Frontend/src/features/routers/
git commit -m "feat(10b): wire nodes, channels, triggers, routers client-side pages"
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
