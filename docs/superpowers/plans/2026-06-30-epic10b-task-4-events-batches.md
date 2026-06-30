# Epic 10B — Task 4: Events + Batches (Server-Side Paginated)

> Read the master plan first: `docs/superpowers/plans/2026-06-30-epic10b-data-tables.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

**Goal:** Replace the 4 batch/event placeholders with server-side paginated pages using ServerGrid. Create new directory structure for batches (separate folders per type). Update router.tsx imports.

**Files to create** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/events/columns.ts`
- `features/events/hooks.ts`
- `features/events/EventFilters.tsx`
- `features/events/EventsGrid.tsx`
- `features/incoming-batches/columns.ts`
- `features/incoming-batches/hooks.ts`
- `features/incoming-batches/IncomingBatchFilters.tsx`
- `features/incoming-batches/IncomingBatchesGrid.tsx`
- `features/incoming-batches/IncomingBatchesPage.tsx`
- `features/outgoing-batches/columns.ts`
- `features/outgoing-batches/hooks.ts`
- `features/outgoing-batches/OutgoingBatchFilters.tsx`
- `features/outgoing-batches/OutgoingBatchesGrid.tsx`
- `features/outgoing-batches/OutgoingBatchesPage.tsx`
- `features/batch-errors/columns.ts`
- `features/batch-errors/hooks.ts`
- `features/batch-errors/BatchErrorFilters.tsx`
- `features/batch-errors/BatchErrorsGrid.tsx`
- `features/batch-errors/BatchErrorsPage.tsx`

Modify:
- `features/events/EventsPage.tsx` — replace PlaceholderPage
- `app/router.tsx` — update batch imports to new directories

Note: Old placeholder files at `features/batches/IncomingBatchesPage.tsx`, `features/batches/OutgoingBatchesPage.tsx`, `features/batches/BatchErrorsPage.tsx` become orphaned dead code. Do NOT delete them — the router.tsx update will stop importing them. They don't affect build or lint.

**Interfaces — Consumes (from Tasks 1 & 2):**
- `shared/api/events.ts` → `getEvents`
- `shared/api/batches.ts` → `getIncomingBatches`, `getOutgoingBatches`, `getBatchErrors`
- `shared/queryKeys.ts` → `queryKeys`
- `shared/types` → `EventSummaryDto`, `EventFilter`, `IncomingBatchSummaryDto`, `IncomingBatchFilter`, `OutgoingBatchDto`, `OutgoingBatchFilter`, `BatchErrorDetailDto`, `BatchErrorFilter`
- `shared/utils/date.ts` → `formatDateTime`
- `shared/utils/status.ts` → `batchStatusVariant`
- `shared/components/data-display/ServerGrid.tsx`
- `shared/components/data-display/StatusBadge.tsx`
- `shared/constants/query.ts` → `DEFAULT_PAGE_SIZE`, `DEFAULT_BATCH_PAGE_SIZE`

**Pattern for all server-side pages:**
1. `columns.ts` — `ColDef<T>[]` export; use `valueFormatter` for dates; use `cellRenderer` for StatusBadge
2. `hooks.ts` — `useQuery` with `placeholderData: (prev) => prev` to avoid grid flash on page change
3. `*Filters.tsx` — controlled form with local state; calls `onFilter(newFilter)` prop
4. `*Grid.tsx` — combines hook + ServerGrid; owns page/pageSize state
5. `*Page.tsx` — `<h1>` + filter component + grid component

---

### Events Page

- [ ] **Step 1: Create `features/events/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { EventSummaryDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const eventColumns: ColDef<EventSummaryDto>[] = [
  { field: 'eventId', headerName: 'Event ID', width: 100 },
  { field: 'triggerId', headerName: 'Trigger', width: 150 },
  { field: 'sourceNodeId', headerName: 'Source Node', width: 160 },
  { field: 'channelId', headerName: 'Channel', width: 130 },
  { field: 'eventType', headerName: 'Type', width: 90 },
  { field: 'tableName', headerName: 'Table', width: 150 },
  { field: 'batchId', headerName: 'Batch ID', width: 100 },
  {
    field: 'createTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
  {
    field: 'isProcessed',
    headerName: 'Processed',
    width: 110,
    cellRenderer: (p: ICellRendererParams<EventSummaryDto>) =>
      p.value
        ? StatusBadge({ status: 'Yes', variant: 'success' })
        : StatusBadge({ status: 'No', variant: 'neutral' }),
  },
];
```

- [ ] **Step 2: Create `features/events/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import type { EventFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getEvents } from '../../shared/api/events';

export function useEvents(filter: EventFilter) {
  return useQuery({
    queryKey: queryKeys.events(filter),
    queryFn: () => getEvents(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 3: Create `features/events/EventFilters.tsx`**

```tsx
import { useState } from 'react';
import type { EventFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

interface Props {
  onFilter: (filter: EventFilter) => void;
}

export function EventFilters({ onFilter }: Props) {
  const [sourceNodeId, setSourceNodeId] = useState('');
  const [eventType, setEventType] = useState('');
  const [isProcessed, setIsProcessed] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function handleApply() {
    onFilter({
      sourceNodeId: sourceNodeId || undefined,
      eventType: eventType || undefined,
      isProcessed: isProcessed === '' ? undefined : isProcessed === 'true',
      from: from || undefined,
      to: to || undefined,
      page: 1,
      pageSize: DEFAULT_PAGE_SIZE,
    });
  }

  function handleReset() {
    setSourceNodeId('');
    setEventType('');
    setIsProcessed('');
    setFrom('');
    setTo('');
    onFilter({ page: 1, pageSize: DEFAULT_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Source Node</Label>
        <Input
          value={sourceNodeId}
          onChange={(e) => setSourceNodeId(e.target.value)}
          placeholder="node-id"
          className="h-8 w-40 text-sm"
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Event Type</Label>
        <select
          value={eventType}
          onChange={(e) => setEventType(e.target.value)}
          className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm"
        >
          <option value="">All</option>
          <option value="INSERT">INSERT</option>
          <option value="UPDATE">UPDATE</option>
          <option value="DELETE">DELETE</option>
        </select>
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Status</Label>
        <select
          value={isProcessed}
          onChange={(e) => setIsProcessed(e.target.value)}
          className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm"
        >
          <option value="">All</option>
          <option value="false">Pending</option>
          <option value="true">Processed</option>
        </select>
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">From</Label>
        <Input
          type="date"
          value={from}
          onChange={(e) => setFrom(e.target.value)}
          className="h-8 text-sm"
        />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">To</Label>
        <Input
          type="date"
          value={to}
          onChange={(e) => setTo(e.target.value)}
          className="h-8 text-sm"
        />
      </div>
      <Button size="sm" onClick={handleApply}>Apply</Button>
      <Button size="sm" variant="outline" onClick={handleReset}>Reset</Button>
    </div>
  );
}
```

- [ ] **Step 4: Create `features/events/EventsGrid.tsx`**

```tsx
import { useState } from 'react';
import type { EventFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { eventColumns } from './columns';
import { useEvents } from './hooks';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

interface Props {
  filter: EventFilter;
  onFilterChange: (f: EventFilter) => void;
}

export function EventsGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useEvents(filter);

  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={eventColumns}
      loading={isLoading}
      total={data?.total ?? 0}
      page={filter.page}
      pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
```

- [ ] **Step 5: Replace `features/events/EventsPage.tsx`**

```tsx
import { useState } from 'react';
import type { EventFilter } from '../../shared/types';
import { EventFilters } from './EventFilters';
import { EventsGrid } from './EventsGrid';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: EventFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function EventsPage() {
  const [filter, setFilter] = useState<EventFilter>(defaultFilter);

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Events</h1>
      <EventFilters onFilter={setFilter} />
      <EventsGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

---

### Incoming Batches Page

- [ ] **Step 6: Create `features/incoming-batches/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { IncomingBatchSummaryDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { batchStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const incomingBatchColumns: ColDef<IncomingBatchSummaryDto>[] = [
  { field: 'batchId', headerName: 'Batch ID', width: 110 },
  { field: 'sourceNodeId', headerName: 'Source Node', width: 160 },
  { field: 'channelId', headerName: 'Channel', width: 130 },
  {
    field: 'status',
    headerName: 'Status',
    width: 120,
    cellRenderer: (p: ICellRendererParams<IncomingBatchSummaryDto>) =>
      p.value ? StatusBadge({ status: p.value as string, variant: batchStatusVariant(p.value as string) }) : null,
  },
  { field: 'rowCount', headerName: 'Rows', width: 80 },
  {
    field: 'receivedTime',
    headerName: 'Received',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
  {
    field: 'appliedTime',
    headerName: 'Applied',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
  {
    field: 'applyTimeMs',
    headerName: 'Apply Time',
    width: 110,
    valueFormatter: (p) => (p.value != null ? formatLatency(p.value as number) : '—'),
  },
];
```

- [ ] **Step 7: Create `features/incoming-batches/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import type { IncomingBatchFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getIncomingBatches } from '../../shared/api/batches';

export function useIncomingBatches(filter: IncomingBatchFilter) {
  return useQuery({
    queryKey: queryKeys.incomingBatches(filter),
    queryFn: () => getIncomingBatches(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 8: Create `features/incoming-batches/IncomingBatchFilters.tsx`**

```tsx
import { useState } from 'react';
import type { IncomingBatchFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

interface Props {
  onFilter: (filter: IncomingBatchFilter) => void;
}

export function IncomingBatchFilters({ onFilter }: Props) {
  const [sourceNodeId, setSourceNodeId] = useState('');
  const [channelId, setChannelId] = useState('');
  const [status, setStatus] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function handleApply() {
    onFilter({
      sourceNodeId: sourceNodeId || undefined,
      channelId: channelId || undefined,
      status: status || undefined,
      from: from || undefined,
      to: to || undefined,
      page: 1,
      pageSize: DEFAULT_BATCH_PAGE_SIZE,
    });
  }

  function handleReset() {
    setSourceNodeId(''); setChannelId(''); setStatus(''); setFrom(''); setTo('');
    onFilter({ page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Source Node</Label>
        <Input value={sourceNodeId} onChange={(e) => setSourceNodeId(e.target.value)} placeholder="node-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Channel</Label>
        <Input value={channelId} onChange={(e) => setChannelId(e.target.value)} placeholder="channel-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Status</Label>
        <select value={status} onChange={(e) => setStatus(e.target.value)} className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm">
          <option value="">All</option>
          <option value="OK">OK</option>
          <option value="LOADING">Loading</option>
          <option value="ERROR">Error</option>
          <option value="CONFLICT">Conflict</option>
        </select>
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">From</Label>
        <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-8 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">To</Label>
        <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-8 text-sm" />
      </div>
      <Button size="sm" onClick={handleApply}>Apply</Button>
      <Button size="sm" variant="outline" onClick={handleReset}>Reset</Button>
    </div>
  );
}
```

- [ ] **Step 9: Create `features/incoming-batches/IncomingBatchesGrid.tsx`**

```tsx
import type { IncomingBatchFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { incomingBatchColumns } from './columns';
import { useIncomingBatches } from './hooks';

interface Props {
  filter: IncomingBatchFilter;
  onFilterChange: (f: IncomingBatchFilter) => void;
}

export function IncomingBatchesGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useIncomingBatches(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={incomingBatchColumns}
      loading={isLoading}
      total={data?.total ?? 0}
      page={filter.page}
      pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
```

- [ ] **Step 10: Create `features/incoming-batches/IncomingBatchesPage.tsx`**

```tsx
import { useState } from 'react';
import type { IncomingBatchFilter } from '../../shared/types';
import { IncomingBatchFilters } from './IncomingBatchFilters';
import { IncomingBatchesGrid } from './IncomingBatchesGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: IncomingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function IncomingBatchesPage() {
  const [filter, setFilter] = useState<IncomingBatchFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Incoming Batches</h1>
      <IncomingBatchFilters onFilter={setFilter} />
      <IncomingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

---

### Outgoing Batches Page

- [ ] **Step 11: Create `features/outgoing-batches/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { OutgoingBatchDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { batchStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const outgoingBatchColumns: ColDef<OutgoingBatchDto>[] = [
  { field: 'batchId', headerName: 'Batch ID', width: 110 },
  { field: 'nodeId', headerName: 'Node', width: 160 },
  { field: 'channelId', headerName: 'Channel', width: 130 },
  {
    field: 'status',
    headerName: 'Status',
    width: 120,
    cellRenderer: (p: ICellRendererParams<OutgoingBatchDto>) =>
      p.value ? StatusBadge({ status: p.value as string, variant: batchStatusVariant(p.value as string) }) : null,
  },
  { field: 'rowCount', headerName: 'Rows', width: 80 },
  { field: 'retryCount', headerName: 'Retries', width: 85 },
  { field: 'createTime', headerName: 'Created', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  { field: 'sentTime', headerName: 'Sent', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  { field: 'ackTime', headerName: 'Ack', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  {
    field: 'error',
    headerName: 'Error',
    flex: 1,
    minWidth: 200,
    valueFormatter: (p) => {
      const v = p.value as string | undefined;
      return v && v.length > 80 ? v.slice(0, 80) + '…' : (v ?? '');
    },
  },
];
```

- [ ] **Step 12: Create `features/outgoing-batches/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import type { OutgoingBatchFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getOutgoingBatches } from '../../shared/api/batches';

export function useOutgoingBatches(filter: OutgoingBatchFilter) {
  return useQuery({
    queryKey: queryKeys.outgoingBatches(filter),
    queryFn: () => getOutgoingBatches(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 13: Create `features/outgoing-batches/OutgoingBatchFilters.tsx`**

```tsx
import { useState } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

interface Props { onFilter: (filter: OutgoingBatchFilter) => void; }

export function OutgoingBatchFilters({ onFilter }: Props) {
  const [nodeId, setNodeId] = useState('');
  const [channelId, setChannelId] = useState('');
  const [status, setStatus] = useState('');

  function handleApply() {
    onFilter({
      nodeId: nodeId || undefined,
      channelId: channelId || undefined,
      status: status || undefined,
      page: 1,
      pageSize: DEFAULT_BATCH_PAGE_SIZE,
    });
  }

  function handleReset() {
    setNodeId(''); setChannelId(''); setStatus('');
    onFilter({ page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Node</Label>
        <Input value={nodeId} onChange={(e) => setNodeId(e.target.value)} placeholder="node-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Channel</Label>
        <Input value={channelId} onChange={(e) => setChannelId(e.target.value)} placeholder="channel-id" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Status</Label>
        <select value={status} onChange={(e) => setStatus(e.target.value)} className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm">
          <option value="">All</option>
          <option value="SENT">Sent</option>
          <option value="OK">OK</option>
          <option value="ERROR">Error</option>
          <option value="LOADING">Loading</option>
        </select>
      </div>
      <Button size="sm" onClick={handleApply}>Apply</Button>
      <Button size="sm" variant="outline" onClick={handleReset}>Reset</Button>
    </div>
  );
}
```

- [ ] **Step 14: Create `features/outgoing-batches/OutgoingBatchesGrid.tsx` and `OutgoingBatchesPage.tsx`**

`features/outgoing-batches/OutgoingBatchesGrid.tsx`:
```tsx
import type { OutgoingBatchFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { outgoingBatchColumns } from './columns';
import { useOutgoingBatches } from './hooks';

interface Props { filter: OutgoingBatchFilter; onFilterChange: (f: OutgoingBatchFilter) => void; }

export function OutgoingBatchesGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useOutgoingBatches(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={outgoingBatchColumns}
      loading={isLoading}
      total={data?.total ?? 0}
      page={filter.page}
      pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
```

`features/outgoing-batches/OutgoingBatchesPage.tsx`:
```tsx
import { useState } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { OutgoingBatchFilters } from './OutgoingBatchFilters';
import { OutgoingBatchesGrid } from './OutgoingBatchesGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: OutgoingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function OutgoingBatchesPage() {
  const [filter, setFilter] = useState<OutgoingBatchFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Outgoing Batches</h1>
      <OutgoingBatchFilters onFilter={setFilter} />
      <OutgoingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

---

### Batch Errors Page

- [ ] **Step 15: Create `features/batch-errors/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { BatchErrorDetailDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import type { StatusVariant } from '../../shared/utils/status';

function severityVariant(severity: string): StatusVariant {
  switch (severity.toUpperCase()) {
    case 'CRITICAL': return 'danger';
    case 'WARNING': return 'warning';
    case 'INFO': return 'neutral';
    default: return 'neutral';
  }
}

export const batchErrorColumns: ColDef<BatchErrorDetailDto>[] = [
  { field: 'errorId', headerName: 'Error ID', width: 100 },
  { field: 'batchId', headerName: 'Batch ID', width: 110 },
  { field: 'conflictType', headerName: 'Conflict Type', width: 150 },
  {
    field: 'severity',
    headerName: 'Severity',
    width: 120,
    cellRenderer: (p: ICellRendererParams<BatchErrorDetailDto>) =>
      p.value ? StatusBadge({ status: p.value as string, variant: severityVariant(p.value as string) }) : null,
  },
  { field: 'createTime', headerName: 'Created', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  {
    field: 'detail',
    headerName: 'Detail',
    flex: 1,
    minWidth: 200,
    valueFormatter: (p) => {
      const v = p.value as string | undefined;
      return v && v.length > 100 ? v.slice(0, 100) + '…' : (v ?? '');
    },
  },
];
```

- [ ] **Step 16: Create `features/batch-errors/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import type { BatchErrorFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getBatchErrors } from '../../shared/api/batches';

export function useBatchErrors(filter: BatchErrorFilter) {
  return useQuery({
    queryKey: queryKeys.batchErrors(filter),
    queryFn: () => getBatchErrors(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 17: Create `features/batch-errors/BatchErrorFilters.tsx`**

```tsx
import { useState } from 'react';
import type { BatchErrorFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

interface Props { onFilter: (filter: BatchErrorFilter) => void; }

export function BatchErrorFilters({ onFilter }: Props) {
  const [batchId, setBatchId] = useState('');
  const [conflictType, setConflictType] = useState('');
  const [severity, setSeverity] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function handleApply() {
    onFilter({
      batchId: batchId ? Number(batchId) : undefined,
      conflictType: conflictType || undefined,
      severity: severity || undefined,
      from: from || undefined,
      to: to || undefined,
      page: 1,
      pageSize: DEFAULT_BATCH_PAGE_SIZE,
    });
  }

  function handleReset() {
    setBatchId(''); setConflictType(''); setSeverity(''); setFrom(''); setTo('');
    onFilter({ page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Batch ID</Label>
        <Input type="number" value={batchId} onChange={(e) => setBatchId(e.target.value)} placeholder="batch id" className="h-8 w-32 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Conflict Type</Label>
        <Input value={conflictType} onChange={(e) => setConflictType(e.target.value)} placeholder="conflict type" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Severity</Label>
        <select value={severity} onChange={(e) => setSeverity(e.target.value)} className="h-8 rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 text-sm">
          <option value="">All</option>
          <option value="CRITICAL">Critical</option>
          <option value="WARNING">Warning</option>
          <option value="INFO">Info</option>
        </select>
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">From</Label>
        <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-8 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">To</Label>
        <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-8 text-sm" />
      </div>
      <Button size="sm" onClick={handleApply}>Apply</Button>
      <Button size="sm" variant="outline" onClick={handleReset}>Reset</Button>
    </div>
  );
}
```

- [ ] **Step 18: Create `features/batch-errors/BatchErrorsGrid.tsx` and `BatchErrorsPage.tsx`**

`features/batch-errors/BatchErrorsGrid.tsx`:
```tsx
import type { BatchErrorFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { batchErrorColumns } from './columns';
import { useBatchErrors } from './hooks';

interface Props { filter: BatchErrorFilter; onFilterChange: (f: BatchErrorFilter) => void; }

export function BatchErrorsGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useBatchErrors(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={batchErrorColumns}
      loading={isLoading}
      total={data?.total ?? 0}
      page={filter.page}
      pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
```

`features/batch-errors/BatchErrorsPage.tsx`:
```tsx
import { useState } from 'react';
import type { BatchErrorFilter } from '../../shared/types';
import { BatchErrorFilters } from './BatchErrorFilters';
import { BatchErrorsGrid } from './BatchErrorsGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: BatchErrorFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function BatchErrorsPage() {
  const [filter, setFilter] = useState<BatchErrorFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Batch Errors</h1>
      <BatchErrorFilters onFilter={setFilter} />
      <BatchErrorsGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

---

- [ ] **Step 19: Update `app/router.tsx` — update batch imports to new directories**

In `router.tsx`, find and replace the three batch import lines:
```typescript
// OLD (remove these 3 lines):
import { IncomingBatchesPage } from '../features/batches/IncomingBatchesPage';
import { OutgoingBatchesPage } from '../features/batches/OutgoingBatchesPage';
import { BatchErrorsPage } from '../features/batches/BatchErrorsPage';

// NEW (replace with these 3 lines):
import { IncomingBatchesPage } from '../features/incoming-batches/IncomingBatchesPage';
import { OutgoingBatchesPage } from '../features/outgoing-batches/OutgoingBatchesPage';
import { BatchErrorsPage } from '../features/batch-errors/BatchErrorsPage';
```

The route paths (`incoming-batches`, `outgoing-batches`, `batch-errors`) remain unchanged.

- [ ] **Step 20: Verify build + lint + tests pass**

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

- [ ] **Step 21: Commit**

```
git add src/MSOSync.Frontend/src/features/events/
git add src/MSOSync.Frontend/src/features/incoming-batches/
git add src/MSOSync.Frontend/src/features/outgoing-batches/
git add src/MSOSync.Frontend/src/features/batch-errors/
git add src/MSOSync.Frontend/src/app/router.tsx
git commit -m "feat(10b): wire events and batches pages with server-side pagination"
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
