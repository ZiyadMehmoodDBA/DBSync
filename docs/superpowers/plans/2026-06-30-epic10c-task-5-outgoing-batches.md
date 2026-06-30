# Epic 10C — Task 5: Outgoing Batches Actions

> Master plan: `docs/superpowers/plans/2026-06-30-epic10c-operational-actions.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10c-operational-actions-design.md`
> Depends on: Task 1 (shared action components must exist)

**Goal:** Add per-row Retry action (via `⋮` menu, no confirm) and a "Retry All" button in the page header (no confirm). Retry All is disabled while in flight.

**Files to create/modify** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/outgoing-batches/mutations.ts`

Modify:
- `shared/api/batches.ts` — add `retryBatch`, `retryAllBatches`
- `features/outgoing-batches/columns.ts` — convert const to factory function
- `features/outgoing-batches/OutgoingBatchesGrid.tsx` — add per-row retry mutation + pending state
- `features/outgoing-batches/OutgoingBatchesPage.tsx` — add Retry All button

**Interfaces — Consumes (from Task 1):**
- `shared/utils/error.ts` → `getErrorMessage(error: unknown): string`
- `shared/components/actions` → `ActionMenu`

**Key types:**
- `OutgoingBatchDto` has: `batchId: number`, `status: string`, `nodeId: string`, `channelId: string`, `createTime: string`, `sentTime?: string`, `ackTime?: string`, `retryCount: number`, `rowCount: number`, `error?: string`

**No confirm dialog for either action** — both retry mutations fire immediately on click.

**Cache invalidation:**
- `retryBatch`: invalidate `['outgoing-batches']` (base key — matches all filter variants)
- `retryAllBatches`: invalidate `['outgoing-batches']`, `queryKeys.dashboardSummary()`, `queryKeys.metricsSummary()`

**Existing `shared/api/batches.ts`** already has `getIncomingBatches`, `getOutgoingBatches`, `getBatchErrors`. Add `retryBatch` and `retryAllBatches` to the same file.

**Existing `features/outgoing-batches/columns.ts`** exports `const outgoingBatchColumns: ColDef<OutgoingBatchDto>[]` with 10 columns. Will be replaced by factory.

**Existing `features/outgoing-batches/OutgoingBatchesGrid.tsx`:**
```tsx
import type { OutgoingBatchFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { outgoingBatchColumns } from './columns';
import { useOutgoingBatches } from './hooks';
interface Props { filter: OutgoingBatchFilter; onFilterChange: (f: OutgoingBatchFilter) => void; }
export function OutgoingBatchesGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useOutgoingBatches(filter);
  return (
    <ServerGrid rowData={data?.data} columnDefs={outgoingBatchColumns} loading={isLoading}
      total={data?.total ?? 0} page={filter.page} pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error} onRetry={() => void refetch()} height={500} />
  );
}
```

**Existing `features/outgoing-batches/OutgoingBatchesPage.tsx`:**
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

- [ ] **Step 1: Add mutation functions to `shared/api/batches.ts`**

Add these two functions to the end of the existing file (keep all existing functions intact):

```typescript
export async function retryBatch(batchId: number): Promise<void> {
  await client.post(`/outgoing-batches/${batchId}/retry`);
}

export async function retryAllBatches(): Promise<void> {
  await client.post('/outgoing-batches/retry-all');
}
```

- [ ] **Step 2: Create `features/outgoing-batches/mutations.ts`**

```typescript
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { retryBatch, retryAllBatches } from '../../shared/api/batches';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

export function useRetryBatchMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (batchId: number) => retryBatch(batchId),
    onSuccess: () => {
      toast.success('Batch queued for retry');
      void queryClient.invalidateQueries({ queryKey: ['outgoing-batches'] });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useRetryAllBatchesMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => retryAllBatches(),
    onSuccess: () => {
      toast.success('All failed batches queued for retry');
      void queryClient.invalidateQueries({ queryKey: ['outgoing-batches'] });
      void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
      void queryClient.invalidateQueries({ queryKey: queryKeys.metricsSummary() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}
```

Note: `['outgoing-batches']` (bare array, not `queryKeys.outgoingBatches(filter)`) invalidates all outgoing-batch queries regardless of which filter is active.

- [ ] **Step 3: Rewrite `features/outgoing-batches/columns.ts` as factory**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { OutgoingBatchDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { batchStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

export function makeOutgoingBatchColumns(
  onRetry: (batchId: number) => void,
  pendingBatchId: number | null,
): ColDef<OutgoingBatchDto>[] {
  return [
    { field: 'batchId', headerName: 'Batch ID', width: 110 },
    { field: 'nodeId', headerName: 'Node', width: 160 },
    { field: 'channelId', headerName: 'Channel', width: 130 },
    {
      field: 'status',
      headerName: 'Status',
      width: 120,
      cellRenderer: (p: ICellRendererParams<OutgoingBatchDto>) =>
        p.value
          ? StatusBadge({
              status: p.value as string,
              variant: batchStatusVariant(p.value as string),
            })
          : null,
    },
    { field: 'rowCount', headerName: 'Rows', width: 80 },
    { field: 'retryCount', headerName: 'Retries', width: 85 },
    {
      field: 'createTime',
      headerName: 'Created',
      width: 165,
      valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
    },
    {
      field: 'sentTime',
      headerName: 'Sent',
      width: 165,
      valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
    },
    {
      field: 'ackTime',
      headerName: 'Ack',
      width: 165,
      valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
    },
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
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<OutgoingBatchDto>) => {
        if (!p.data) return null;
        const { batchId } = p.data;
        return ActionMenu({
          items: [
            {
              label: 'Retry',
              onClick: () => onRetry(batchId),
              disabled: pendingBatchId === batchId,
            },
          ],
        });
      },
    },
  ];
}
```

- [ ] **Step 4: Rewrite `features/outgoing-batches/OutgoingBatchesGrid.tsx`**

```tsx
import { useState, useCallback, useMemo } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { makeOutgoingBatchColumns } from './columns';
import { useOutgoingBatches } from './hooks';
import { useRetryBatchMutation } from './mutations';

interface Props {
  filter: OutgoingBatchFilter;
  onFilterChange: (f: OutgoingBatchFilter) => void;
}

export function OutgoingBatchesGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useOutgoingBatches(filter);
  const retryMutation = useRetryBatchMutation();
  const [pendingBatchId, setPendingBatchId] = useState<number | null>(null);

  const handleRetry = useCallback(
    async (batchId: number) => {
      setPendingBatchId(batchId);
      try {
        await retryMutation.mutateAsync(batchId);
      } finally {
        setPendingBatchId(null);
      }
    },
    [retryMutation],
  );

  const columns = useMemo(
    () => makeOutgoingBatchColumns((batchId) => void handleRetry(batchId), pendingBatchId),
    [handleRetry, pendingBatchId],
  );

  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={columns}
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

- [ ] **Step 5: Rewrite `features/outgoing-batches/OutgoingBatchesPage.tsx` — add Retry All button**

```tsx
import { useState } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { OutgoingBatchFilters } from './OutgoingBatchFilters';
import { OutgoingBatchesGrid } from './OutgoingBatchesGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';
import { Button } from '../../components/ui/button';
import { useRetryAllBatchesMutation } from './mutations';

const defaultFilter: OutgoingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function OutgoingBatchesPage() {
  const [filter, setFilter] = useState<OutgoingBatchFilter>(defaultFilter);
  const retryAllMutation = useRetryAllBatchesMutation();

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Outgoing Batches</h1>
        <Button
          variant="outline"
          onClick={() => void retryAllMutation.mutateAsync()}
          disabled={retryAllMutation.isPending}
        >
          {retryAllMutation.isPending ? 'Retrying…' : 'Retry All'}
        </Button>
      </div>
      <OutgoingBatchFilters onFilter={setFilter} />
      <OutgoingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

- [ ] **Step 6: Verify build and tests pass**

Run from `src/MSOSync.Frontend/`:
```bash
npm run build
```
Expected: exits 0.

```bash
npm test -- --run
```
Expected: 12/12 pass.

- [ ] **Step 7: Commit**

```bash
git add src/MSOSync.Frontend/src/shared/api/batches.ts
git add src/MSOSync.Frontend/src/features/outgoing-batches/mutations.ts
git add src/MSOSync.Frontend/src/features/outgoing-batches/columns.ts
git add src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesGrid.tsx
git add src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx
git commit -m "feat(10c): add retry batch + retry all actions to outgoing batches"
```

---

## Report Contract

Write report to the path given by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files modified (count)
- Build result
- Test result (N/12 pass)
- Any concerns
