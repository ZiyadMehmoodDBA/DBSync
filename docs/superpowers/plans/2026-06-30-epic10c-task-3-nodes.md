# Epic 10C — Task 3: Nodes Actions

> Master plan: `docs/superpowers/plans/2026-06-30-epic10c-operational-actions.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10c-operational-actions-design.md`
> Depends on: Task 1 (shared action components must exist)

**Goal:** Add Enable, Disable, Approve Registration actions to each row on the Nodes page via a `⋮` dropdown menu.

**Files to create/modify** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/nodes/mutations.ts`

Modify:
- `shared/api/nodes.ts` — add `enableNode`, `disableNode`, `approveRegistration`
- `features/nodes/columns.ts` — convert const to factory function
- `features/nodes/NodesGrid.tsx` — accept `onAction` prop, pass to columns
- `features/nodes/NodesPage.tsx` — add confirm state + 3 mutations + ConfirmDialog

**Interfaces — Consumes (from Task 1):**
- `shared/utils/error.ts` → `getErrorMessage(error: unknown): string`
- `shared/components/actions` → `ActionMenu`, `ConfirmDialog`

**Key types:**
- `NodeDto` has: `nodeId: string`, `groupId: string`, `name: string`, `status: string`, `syncEnabled: boolean`, `lastHeartbeat?: string`, `probeLatencyMs?: number`, `createdTime: string`

**Existing `shared/api/nodes.ts`:**
```typescript
import client from './client';
import type { NodeDto } from '../types';
export async function getNodes(): Promise<NodeDto[]> {
  const { data } = await client.get<NodeDto[]>('/nodes');
  return data;
}
```

**Existing `features/nodes/columns.ts`** exports `const nodeColumns: ColDef<NodeDto>[]` with 8 columns. Will be replaced by factory.

**Existing `features/nodes/NodesGrid.tsx`:**
```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { nodeColumns } from './columns';
import { useNodes } from './hooks';
interface Props { quickFilterText?: string; }
export function NodesGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useNodes();
  return (
    <DataGrid rowData={data} columnDefs={nodeColumns} loading={isLoading}
      error={error} onRetry={() => void refetch()} quickFilterText={quickFilterText} height={500} />
  );
}
```

**Existing `features/nodes/NodesPage.tsx`:**
```tsx
import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { NodesGrid } from './NodesGrid';
export function NodesPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Nodes</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)}
        placeholder="Search nodes…" className="max-w-xs" />
      <NodesGrid quickFilterText={search} />
    </div>
  );
}
```

**Cache invalidation for node mutations:** invalidate nodes, dashboardSummary, metricsSummary, topologySummary, topologyGroups.

---

- [ ] **Step 1: Add mutation functions to `shared/api/nodes.ts`**

```typescript
import client from './client';
import type { NodeDto } from '../types';

export async function getNodes(): Promise<NodeDto[]> {
  const { data } = await client.get<NodeDto[]>('/nodes');
  return data;
}

export async function enableNode(nodeId: string): Promise<void> {
  await client.post(`/nodes/${encodeURIComponent(nodeId)}/enable`);
}

export async function disableNode(nodeId: string): Promise<void> {
  await client.post(`/nodes/${encodeURIComponent(nodeId)}/disable`);
}

export async function approveRegistration(requestId: string): Promise<void> {
  await client.post(`/nodes/registrations/${encodeURIComponent(requestId)}/approve`);
}
```

- [ ] **Step 2: Create `features/nodes/mutations.ts`**

```typescript
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { enableNode, disableNode, approveRegistration } from '../../shared/api/nodes';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

function invalidateNodeRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.nodes() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.metricsSummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useEnableNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (nodeId: string) => enableNode(nodeId),
    onSuccess: () => {
      toast.success('Node enabled');
      invalidateNodeRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useDisableNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (nodeId: string) => disableNode(nodeId),
    onSuccess: () => {
      toast.success('Node disabled');
      invalidateNodeRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useApproveRegistrationMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (requestId: string) => approveRegistration(requestId),
    onSuccess: () => {
      toast.success('Registration approved');
      invalidateNodeRelated(queryClient);
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}
```

- [ ] **Step 3: Rewrite `features/nodes/columns.ts` as factory**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { NodeDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { nodeStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

type NodeAction = 'enable' | 'disable' | 'approve';

export function makeNodeColumns(
  onAction: (nodeId: string, action: NodeAction) => void,
): ColDef<NodeDto>[] {
  return [
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
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<NodeDto>) => {
        if (!p.data) return null;
        const { nodeId } = p.data;
        return ActionMenu({
          items: [
            { label: 'Enable', onClick: () => onAction(nodeId, 'enable') },
            {
              label: 'Disable',
              onClick: () => onAction(nodeId, 'disable'),
              variant: 'destructive',
            },
            { label: 'Approve Registration', onClick: () => onAction(nodeId, 'approve') },
          ],
        });
      },
    },
  ];
}
```

- [ ] **Step 4: Rewrite `features/nodes/NodesGrid.tsx`**

```tsx
import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeNodeColumns } from './columns';
import { useNodes } from './hooks';

type NodeAction = 'enable' | 'disable' | 'approve';

interface Props {
  quickFilterText?: string;
  onAction: (nodeId: string, action: NodeAction) => void;
}

export function NodesGrid({ quickFilterText, onAction }: Props) {
  const { data, isLoading, error, refetch } = useNodes();
  const columns = useMemo(() => makeNodeColumns(onAction), [onAction]);
  return (
    <DataGrid
      rowData={data}
      columnDefs={columns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
```

- [ ] **Step 5: Rewrite `features/nodes/NodesPage.tsx`**

```tsx
import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { ConfirmDialog } from '../../shared/components/actions';
import { NodesGrid } from './NodesGrid';
import {
  useEnableNodeMutation,
  useDisableNodeMutation,
  useApproveRegistrationMutation,
} from './mutations';

type NodeAction = 'enable' | 'disable' | 'approve';

interface ConfirmState {
  nodeId: string;
  action: NodeAction;
}

const CONFIRM_CONFIG: Record<
  NodeAction,
  {
    title: string;
    description: (nodeId: string) => string;
    confirmLabel: string;
    variant: 'default' | 'destructive';
  }
> = {
  enable: {
    title: 'Enable Node',
    description: (id) => `Enable node "${id}"? It will resume participating in sync.`,
    confirmLabel: 'Enable',
    variant: 'default',
  },
  disable: {
    title: 'Disable Node',
    description: (id) => `Disable node "${id}"? It will stop participating in sync.`,
    confirmLabel: 'Disable',
    variant: 'destructive',
  },
  approve: {
    title: 'Approve Registration',
    description: (id) => `Approve registration request for node "${id}"?`,
    confirmLabel: 'Approve',
    variant: 'default',
  },
};

export function NodesPage() {
  const [search, setSearch] = useState('');
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null);

  const enableMutation = useEnableNodeMutation();
  const disableMutation = useDisableNodeMutation();
  const approveMutation = useApproveRegistrationMutation();

  const onAction = useCallback((nodeId: string, action: NodeAction) => {
    setConfirmState({ nodeId, action });
  }, []);

  const isPending =
    enableMutation.isPending || disableMutation.isPending || approveMutation.isPending;

  const handleConfirm = async () => {
    if (!confirmState) return;
    const { nodeId, action } = confirmState;
    if (action === 'enable') await enableMutation.mutateAsync(nodeId);
    else if (action === 'disable') await disableMutation.mutateAsync(nodeId);
    else await approveMutation.mutateAsync(nodeId);
    setConfirmState(null);
  };

  const config = confirmState ? CONFIRM_CONFIG[confirmState.action] : null;

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Nodes</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search nodes…"
        className="max-w-xs"
      />
      <NodesGrid quickFilterText={search} onAction={onAction} />
      {config && confirmState && (
        <ConfirmDialog
          open
          title={config.title}
          description={config.description(confirmState.nodeId)}
          confirmLabel={config.confirmLabel}
          variant={config.variant}
          loading={isPending}
          onConfirm={() => void handleConfirm()}
          onOpenChange={(open) => {
            if (!open) setConfirmState(null);
          }}
        />
      )}
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
git add src/MSOSync.Frontend/src/shared/api/nodes.ts
git add src/MSOSync.Frontend/src/features/nodes/mutations.ts
git add src/MSOSync.Frontend/src/features/nodes/columns.ts
git add src/MSOSync.Frontend/src/features/nodes/NodesGrid.tsx
git add src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx
git commit -m "feat(10c): add enable/disable/approve registration actions to nodes"
```

---

## Report Contract

Write report to the path given by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files modified (count)
- Build result
- Test result (N/12 pass)
- Any concerns
