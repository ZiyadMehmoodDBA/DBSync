# Task 14: Jobs Page Frontend + SignalR OperationChanged

**Epic:** 12C System Administration Center
**Depends on:** Task 12 (route `/operations/jobs` registered), backend `GET /api/v1/operations` endpoint
**Blocks:** Nothing — standalone page

---

## Goal

Build the Jobs page: a filterable, sortable table of system operations (Export, Rollout, Decommission, Recovery) with live SignalR updates, Cancel and Retry actions, and deep-link navigation to Correlation view.

---

## Step 1 — Read existing TanStack Table usage

- [ ] Search for an existing table in the codebase to understand the TanStack Table pattern used:

```powershell
Get-ChildItem -Recurse -Path src/MSOSync.Frontend/src -Include "*.tsx" | Select-String "useReactTable" | Select-Object -First 3
```

Note how `useReactTable` is called, how columns are defined, and how the table is rendered (look for `flexRender` calls). You will follow the same pattern.

---

## Step 2 — Read existing mutation pattern

- [ ] Open any existing file that calls `useMutation`. Look for the pattern used for error toasts and loading states. Common locations: `src/MSOSync.Frontend/src/features/nodes/` or `src/MSOSync.Frontend/src/features/admin/`.

---

## Step 3 — Read existing ConfirmDialog component

- [ ] Search for `ConfirmDialog` in `src/MSOSync.Frontend/src/shared/components/`. Read its props interface. Note the exact import path and required props (`open`, `onConfirm`, `onCancel`, `title`, `description` or similar).

---

## Step 4 — Create types file

- [ ] Create `src/MSOSync.Frontend/src/shared/types/operations.ts`:

```typescript
export type OperationStatus = 'Pending' | 'Running' | 'Completed' | 'Failed' | 'Cancelled';
export type OperationResult = 'Success' | 'PartialSuccess' | 'Failure' | 'Cancelled';
export type OperationType = 'Export' | 'Rollout' | 'Decommission' | 'Recovery';

export interface OperationDto {
  operationId: string;
  operationType: OperationType;
  status: OperationStatus;
  result: OperationResult | null;
  progressPercent: number | null;
  progressMessage: string | null;
  queuePosition: number | null;
  correlationId: string | null;
  initiatedBy: string | null;
  startedAt: string;
  completedAt: string | null;
  canCancel: boolean;
  canRetry: boolean;
  summary: string | null;
}

export interface OperationPageDto {
  items: OperationDto[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface OperationFilter {
  types?: OperationType[];
  statuses?: OperationStatus[];
  from?: string;
  to?: string;
  page?: number;
  pageSize?: number;
}
```

---

## Step 5 — Create operations.ts API file

- [ ] Create `src/MSOSync.Frontend/src/shared/api/operations.ts`:

```typescript
import { apiFetch } from './client';
import type { OperationPageDto, OperationDto, OperationFilter } from '../types/operations';

function buildOperationsQuery(filter: OperationFilter): string {
  const params = new URLSearchParams();
  if (filter.types?.length)    filter.types.forEach((t) => params.append('types', t));
  if (filter.statuses?.length) filter.statuses.forEach((s) => params.append('statuses', s));
  if (filter.from)             params.set('from', filter.from);
  if (filter.to)               params.set('to', filter.to);
  if (filter.page != null)     params.set('page', String(filter.page));
  if (filter.pageSize != null) params.set('pageSize', String(filter.pageSize));
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

export async function fetchOperations(filter: OperationFilter): Promise<OperationPageDto> {
  return apiFetch(`/api/v1/operations${buildOperationsQuery(filter)}`);
}

export async function fetchOperationDetail(id: string): Promise<OperationDto> {
  return apiFetch(`/api/v1/operations/${encodeURIComponent(id)}`);
}

export async function cancelOperation(id: string): Promise<OperationDto> {
  return apiFetch(`/api/v1/operations/${encodeURIComponent(id)}/cancel`, {
    method: 'POST',
  });
}

export async function retryOperation(id: string): Promise<OperationDto> {
  return apiFetch(`/api/v1/operations/${encodeURIComponent(id)}/retry`, {
    method: 'POST',
  });
}

export const operationKeys = {
  all: ['operations'] as const,
  list: (filter: OperationFilter) => ['operations', 'list', filter] as const,
  detail: (id: string) => ['operations', 'detail', id] as const,
};
```

---

## Step 6 — Create useOperations.ts hook

- [ ] Create `src/MSOSync.Frontend/src/shared/hooks/useOperations.ts`:

```typescript
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  fetchOperations,
  fetchOperationDetail,
  cancelOperation,
  retryOperation,
  operationKeys,
} from '../api/operations';
import type { OperationFilter } from '../types/operations';

export function useOperations(filter: OperationFilter) {
  return useQuery({
    queryKey: operationKeys.list(filter),
    queryFn: () => fetchOperations(filter),
    staleTime: 10_000,
    refetchOnWindowFocus: true,
  });
}

export function useOperationDetail(id: string) {
  return useQuery({
    queryKey: operationKeys.detail(id),
    queryFn: () => fetchOperationDetail(id),
    staleTime: 5_000,
    enabled: !!id,
  });
}

export function useCancelOperation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: cancelOperation,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: operationKeys.all });
    },
  });
}

export function useRetryOperation() {
  const qc = useQueryClient();
  return useMutation({
    mutationFn: retryOperation,
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: operationKeys.all });
    },
  });
}
```

---

## Step 7 — Create OperationStatusBadge component

- [ ] Create `src/MSOSync.Frontend/src/features/operations/jobs/components/OperationStatusBadge.tsx`:

```typescript
import { Badge } from '@/shared/components/ui/badge';
import { Loader2 } from 'lucide-react';
import type { OperationStatus, OperationResult } from '@/shared/types/operations';

interface Props {
  status: OperationStatus;
  result: OperationResult | null;
}

export function OperationStatusBadge({ status, result }: Props) {
  if (status === 'Running') {
    return (
      <Badge className="gap-1 bg-blue-100 text-blue-800 border border-blue-200">
        <Loader2 className="h-3 w-3 animate-spin" />
        Running
      </Badge>
    );
  }
  if (status === 'Pending') {
    return (
      <Badge className="bg-gray-100 text-gray-600 border border-gray-200">
        Pending
      </Badge>
    );
  }
  if (status === 'Cancelled') {
    return (
      <Badge className="bg-gray-100 text-gray-400 border border-gray-200 line-through">
        Cancelled
      </Badge>
    );
  }
  if (status === 'Failed') {
    return (
      <Badge className="bg-red-100 text-red-800 border border-red-200">
        Failed
      </Badge>
    );
  }
  // Completed — show result
  if (result === 'Success') {
    return (
      <Badge className="bg-green-100 text-green-800 border border-green-200">
        Success
      </Badge>
    );
  }
  if (result === 'PartialSuccess') {
    return (
      <Badge className="bg-yellow-100 text-yellow-800 border border-yellow-200">
        Partial
      </Badge>
    );
  }
  return (
    <Badge className="bg-gray-100 text-gray-600 border border-gray-200">
      {status}
    </Badge>
  );
}
```

---

## Step 8 — Create OperationProgressCell component

- [ ] Create `src/MSOSync.Frontend/src/features/operations/jobs/components/OperationProgressCell.tsx`:

```typescript
import type { OperationStatus } from '@/shared/types/operations';

interface Props {
  status: OperationStatus;
  progressPercent: number | null;
  progressMessage: string | null;
}

export function OperationProgressCell({ status, progressPercent, progressMessage }: Props) {
  // Only show progress bar for Pending and Running states
  if (status !== 'Running' && status !== 'Pending') {
    return <span className="text-xs text-muted-foreground">—</span>;
  }

  const pct = progressPercent ?? 0;

  return (
    <div className="min-w-[100px]">
      <div className="flex items-center justify-between mb-0.5">
        <span className="text-xs font-medium text-foreground">{pct}%</span>
      </div>
      <div className="h-1.5 w-full overflow-hidden rounded-full bg-muted">
        <div
          className="h-full rounded-full bg-blue-500 transition-all duration-300"
          style={{ width: `${pct}%` }}
        />
      </div>
      {progressMessage && (
        <p className="mt-0.5 truncate text-xs text-muted-foreground max-w-[160px]">
          {progressMessage}
        </p>
      )}
    </div>
  );
}
```

---

## Step 9 — Create JobsPage.tsx

- [ ] Create `src/MSOSync.Frontend/src/features/operations/jobs/JobsPage.tsx`:

```typescript
import { useState } from 'react';
import { useNavigate } from 'react-router-dom';
import {
  useReactTable,
  getCoreRowModel,
  getSortedRowModel,
  flexRender,
  createColumnHelper,
  type SortingState,
} from '@tanstack/react-table';
import { useOperations, useCancelOperation, useRetryOperation } from '@/shared/hooks/useOperations';
import { OperationStatusBadge } from './components/OperationStatusBadge';
import { OperationProgressCell } from './components/OperationProgressCell';
import { Button } from '@/shared/components/ui/button';
import { Badge } from '@/shared/components/ui/badge';
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/shared/components/ui/select';
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/shared/components/ui/table';
import type { OperationDto, OperationFilter, OperationType, OperationStatus } from '@/shared/types/operations';
import { formatDistanceToNow, differenceInMilliseconds, parseISO } from 'date-fns';

// --- Helpers ---

function relativeTime(iso: string): string {
  try {
    return formatDistanceToNow(parseISO(iso), { addSuffix: true });
  } catch {
    return iso;
  }
}

function duration(startedAt: string, completedAt: string | null): string {
  if (!completedAt) return '—';
  try {
    const ms = differenceInMilliseconds(parseISO(completedAt), parseISO(startedAt));
    if (ms < 1000) return `${ms}ms`;
    const s = Math.floor(ms / 1000);
    if (s < 60) return `${s}s`;
    const m = Math.floor(s / 60);
    return `${m}m ${s % 60}s`;
  } catch {
    return '—';
  }
}

const TYPE_BADGE_COLORS: Record<string, string> = {
  Export:        'bg-violet-100 text-violet-800',
  Rollout:       'bg-blue-100 text-blue-800',
  Decommission:  'bg-orange-100 text-orange-800',
  Recovery:      'bg-teal-100 text-teal-800',
};

const ALL_TYPES: OperationType[] = ['Export', 'Rollout', 'Decommission', 'Recovery'];
const ALL_STATUSES: OperationStatus[] = ['Pending', 'Running', 'Completed', 'Failed', 'Cancelled'];

const helper = createColumnHelper<OperationDto>();

// --- Component ---

export function JobsPage() {
  const navigate = useNavigate();
  const [sorting, setSorting] = useState<SortingState>([{ id: 'startedAt', desc: true }]);
  const [filter, setFilter] = useState<OperationFilter>({
    page: 1,
    pageSize: 50,
  });
  const [selectedType, setSelectedType] = useState<string>('all');
  const [selectedStatus, setSelectedStatus] = useState<string>('all');

  // Update filter when selects change
  const applyFilters = (type: string, status: string) => {
    setFilter((f) => ({
      ...f,
      types: type !== 'all' ? [type as OperationType] : undefined,
      statuses: status !== 'all' ? [status as OperationStatus] : undefined,
      page: 1,
    }));
  };

  const { data, isLoading } = useOperations(filter);
  const cancelMutation = useCancelOperation();
  const retryMutation = useRetryOperation();

  const [cancelTarget, setCancelTarget] = useState<string | null>(null);

  const columns = [
    helper.accessor('operationType', {
      header: 'Type',
      cell: (info) => (
        <Badge className={`text-xs ${TYPE_BADGE_COLORS[info.getValue()] ?? ''}`}>
          {info.getValue()}
        </Badge>
      ),
    }),
    helper.accessor('summary', {
      header: 'Summary',
      cell: (info) => (
        <span className="max-w-[240px] truncate text-sm">{info.getValue() ?? '—'}</span>
      ),
    }),
    helper.accessor('status', {
      header: 'Status',
      cell: (info) => (
        <OperationStatusBadge status={info.getValue()} result={info.row.original.result} />
      ),
    }),
    helper.display({
      id: 'progress',
      header: 'Progress',
      cell: (info) => (
        <OperationProgressCell
          status={info.row.original.status}
          progressPercent={info.row.original.progressPercent}
          progressMessage={info.row.original.progressMessage}
        />
      ),
    }),
    helper.accessor('queuePosition', {
      header: 'Queue',
      cell: (info) =>
        info.row.original.status === 'Pending' && info.getValue() != null
          ? <span className="text-xs text-muted-foreground">#{info.getValue()}</span>
          : null,
    }),
    helper.accessor('initiatedBy', {
      header: 'Initiated by',
      cell: (info) => <span className="text-xs">{info.getValue() ?? '—'}</span>,
    }),
    helper.accessor('startedAt', {
      header: 'Started',
      cell: (info) => (
        <span className="text-xs text-muted-foreground" title={info.getValue()}>
          {relativeTime(info.getValue())}
        </span>
      ),
    }),
    helper.display({
      id: 'duration',
      header: 'Duration',
      cell: (info) => (
        <span className="text-xs text-muted-foreground">
          {duration(info.row.original.startedAt, info.row.original.completedAt)}
        </span>
      ),
    }),
    helper.display({
      id: 'actions',
      header: '',
      cell: (info) => {
        const op = info.row.original;
        return (
          <div className="flex items-center gap-1">
            {op.canCancel && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs text-destructive hover:text-destructive"
                onClick={(e) => { e.stopPropagation(); setCancelTarget(op.operationId); }}
                disabled={cancelMutation.isPending}
              >
                Cancel
              </Button>
            )}
            {op.canRetry && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs"
                onClick={(e) => {
                  e.stopPropagation();
                  retryMutation.mutate(op.operationId);
                }}
                disabled={retryMutation.isPending}
              >
                Retry
              </Button>
            )}
          </div>
        );
      },
    }),
  ];

  const table = useReactTable({
    data: data?.items ?? [],
    columns,
    state: { sorting },
    onSortingChange: setSorting,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    manualPagination: true,
  });

  return (
    <div className="space-y-4 p-6">
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Jobs</h1>
        <p className="text-sm text-muted-foreground">System operations — exports, rollouts, lifecycle events</p>
      </div>

      {/* Filter bar */}
      <div className="flex items-center gap-3">
        <Select
          value={selectedType}
          onValueChange={(v) => { setSelectedType(v); applyFilters(v, selectedStatus); }}
        >
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder="All types" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All types</SelectItem>
            {ALL_TYPES.map((t) => (
              <SelectItem key={t} value={t}>{t}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={selectedStatus}
          onValueChange={(v) => { setSelectedStatus(v); applyFilters(selectedType, v); }}
        >
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder="All statuses" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            {ALL_STATUSES.map((s) => (
              <SelectItem key={s} value={s}>{s}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        {data && (
          <span className="ml-auto text-xs text-muted-foreground">
            {data.totalCount} total
          </span>
        )}
      </div>

      {/* Table */}
      <div className="rounded-lg border">
        <Table>
          <TableHeader>
            {table.getHeaderGroups().map((hg) => (
              <TableRow key={hg.id}>
                {hg.headers.map((h) => (
                  <TableHead
                    key={h.id}
                    className={h.column.getCanSort() ? 'cursor-pointer select-none' : ''}
                    onClick={h.column.getToggleSortingHandler()}
                  >
                    {flexRender(h.column.columnDef.header, h.getContext())}
                    {h.column.getIsSorted() === 'asc' ? ' ↑' : h.column.getIsSorted() === 'desc' ? ' ↓' : ''}
                  </TableHead>
                ))}
              </TableRow>
            ))}
          </TableHeader>
          <TableBody>
            {isLoading ? (
              <TableRow>
                <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
                  Loading…
                </TableCell>
              </TableRow>
            ) : table.getRowModel().rows.length === 0 ? (
              <TableRow>
                <TableCell colSpan={columns.length} className="h-24 text-center text-muted-foreground">
                  No operations found.
                </TableCell>
              </TableRow>
            ) : (
              table.getRowModel().rows.map((row) => (
                <TableRow
                  key={row.id}
                  className="cursor-pointer hover:bg-muted/50"
                  onClick={() => {
                    const op = row.original;
                    if (op.correlationId) {
                      navigate(`/operations/activity?correlationId=${encodeURIComponent(op.correlationId)}`);
                    }
                  }}
                >
                  {row.getVisibleCells().map((cell) => (
                    <TableCell key={cell.id}>
                      {flexRender(cell.column.columnDef.cell, cell.getContext())}
                    </TableCell>
                  ))}
                </TableRow>
              ))
            )}
          </TableBody>
        </Table>
      </div>

      {/* Cancel confirmation dialog */}
      {cancelTarget && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-black/40">
          <div className="rounded-lg bg-background border p-6 shadow-lg max-w-sm w-full space-y-4">
            <h3 className="text-lg font-semibold">Cancel operation?</h3>
            <p className="text-sm text-muted-foreground">
              This will attempt to cancel the operation. Already-processed steps cannot be undone.
            </p>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setCancelTarget(null)}>
                Keep running
              </Button>
              <Button
                variant="destructive"
                onClick={() => {
                  if (cancelTarget) {
                    cancelMutation.mutate(cancelTarget, {
                      onSettled: () => setCancelTarget(null),
                    });
                  }
                }}
                disabled={cancelMutation.isPending}
              >
                {cancelMutation.isPending ? 'Cancelling…' : 'Cancel operation'}
              </Button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
```

Note: This page imports `date-fns`. If `date-fns` is not already in `package.json`, install it:

```powershell
cd src/MSOSync.Frontend && npm install date-fns
```

---

## Step 10 — Wire OperationChanged in eventRouter.ts

- [ ] Open `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`. Add the import:

```typescript
import { operationKeys } from '../api/operations';
```

- [ ] Add the new case inside the switch statement:

```typescript
case 'OperationChanged':
  queryClient.invalidateQueries({ queryKey: operationKeys.all });
  break;
```

---

## Step 11 — Create barrel index

- [ ] Create `src/MSOSync.Frontend/src/features/operations/jobs/index.ts`:

```typescript
export { JobsPage } from './JobsPage';
```

---

## Step 12 — Build check

- [ ] Run:

```powershell
cd src/MSOSync.Frontend && npm run build 2>&1
```

Fix any TypeScript errors. Common issue: `@tanstack/react-table` column helper types. If `createColumnHelper` is not available (older version), replace with:

```typescript
const columns: ColumnDef<OperationDto>[] = [ ... ]
```

Import: `import type { ColumnDef } from '@tanstack/react-table';`

---

## Step 13 — Manual smoke test

- [ ] Open `/operations/jobs`. Verify filter selects work, table renders, and Cancel/Retry buttons appear for eligible rows.
- [ ] Click a row with a correlationId — confirm navigation to `/operations/activity?correlationId=...`.

---

## Step 14 — Commit

- [ ] Stage files:

```powershell
git add src/MSOSync.Frontend/src/shared/types/operations.ts
git add src/MSOSync.Frontend/src/shared/api/operations.ts
git add src/MSOSync.Frontend/src/shared/hooks/useOperations.ts
git add src/MSOSync.Frontend/src/features/operations/jobs/
git add src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts
```

- [ ] Commit:

```powershell
git commit -m "feat(12C-14): Jobs page with filter, TanStack Table, cancel/retry, SignalR OperationChanged"
```
