# Epic 10D — Task 7: Nodes Edit Form (extends 10C)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an Edit form for Node metadata (groupId, syncUrl, heartbeatInterval). No create (nodes self-register). No delete from this page. The 3 existing 10C operational mutations (enable/disable/approve) are preserved unchanged.

**Architecture:** `NodeDialog` is edit-only. `groupId` is a Select populated from `useTopologyGroups()`. `heartbeatInterval` uses `inputMode="numeric"`. `NodeDto` needs two new optional fields added.

**Prerequisite:** Task 1 complete (shared form components installed).

## Global Constraints

- TypeScript strict; no `any`; relative imports only
- `mutateAsync()` everywhere — never `mutate()`
- No new Vitest tests — `npm run build` exits 0 = done
- New `useUpdateNodeMutation` has NO `onError` — dialog owns error display
- Existing 3 operational hooks (enable/disable/approve) with `onError: toast.error()` are NOT modified

---

## Interfaces

**Consumes from Task 1:**
```tsx
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
```

**Existing types (to be extended):**
```ts
// src/shared/types/nodes.ts — current
interface NodeDto {
  nodeId: string;
  groupId: string;
  name: string;
  status: string;
  syncEnabled: boolean;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
  createdTime: string;
  // MISSING: syncUrl, heartbeatInterval — must be added as optional
}
```

**Existing API (src/shared/api/nodes.ts):**
```ts
export async function enableNode(nodeId: string): Promise<void> { ... }
export async function disableNode(nodeId: string): Promise<void> { ... }
export async function approveRegistration(requestId: string): Promise<void> { ... }
// getNodes() — verify this exists; add if missing
```

**Existing mutations (src/features/nodes/mutations.ts) — DO NOT MODIFY:**
```ts
export function useEnableNodeMutation() { ... }    // has onError — preserved
export function useDisableNodeMutation() { ... }   // has onError — preserved
export function useApproveRegistrationMutation() { ... } // has onError — preserved
// also: invalidateNodeRelated() helper function — keep
```

**Topology groups hook:**
```ts
// src/features/topology/hooks.ts
export function useTopologyGroups() {
  return useQuery({ queryKey: queryKeys.topologyGroups(), queryFn: getTopologyGroups, ... });
}
```

**queryKeys:**
```ts
queryKeys.nodes()           // → ['nodes']
queryKeys.topologySummary() // → ['topology-summary']
queryKeys.topologyGroups()  // → ['topology-groups']
queryKeys.metricsSummary()  // → ['metrics-summary']
queryKeys.dashboardSummary()// → ['dashboard-summary']
```

**Existing columns factory:**
```ts
// src/features/nodes/columns.ts
export function makeNodeColumns(
  onAction: (nodeId: string, action: NodeAction) => void,
): ColDef<NodeDto>[]
```

**Existing NodesPage.tsx:**
```tsx
// manages: confirmState for enable/disable/approve
// NodesGrid accepts: quickFilterText, onAction
```

**Produces:**
- API: `updateNode(nodeId, data)` in `shared/api/nodes.ts`
- Type: `NodeDto` with `syncUrl?: string; heartbeatInterval?: number` added
- Hook: `useUpdateNodeMutation()` in `features/nodes/mutations.ts`
- Dialog: `NodeDialog` — props `{ open, initialValues: NodeDto, onOpenChange }`
- Schema type: `UpdateNodeForm`

---

## Files

**Modify:**
- `src/MSOSync.Frontend/src/shared/types/nodes.ts` — add 2 optional fields
- `src/MSOSync.Frontend/src/shared/api/nodes.ts` — add `updateNode`

**Create:**
- `src/MSOSync.Frontend/src/features/nodes/schemas.ts`
- `src/MSOSync.Frontend/src/features/nodes/NodeDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/features/nodes/mutations.ts` — APPEND `useUpdateNodeMutation`
- `src/MSOSync.Frontend/src/features/nodes/columns.ts` — extend factory signature
- `src/MSOSync.Frontend/src/features/nodes/NodesGrid.tsx` — add `onEdit` prop
- `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx` — add edit state + NodeDialog

---

## Steps

- [ ] **Step 1: Extend NodeDto with syncUrl and heartbeatInterval**

Open `src/MSOSync.Frontend/src/shared/types/nodes.ts`. Full file after edit:

```ts
export interface NodeDto {
  nodeId: string;
  groupId: string;
  name: string;
  status: string;
  syncEnabled: boolean;
  lastHeartbeat?: string;
  probeLatencyMs?: number;
  createdTime: string;
  syncUrl?: string;
  heartbeatInterval?: number;
}
```

- [ ] **Step 2: Add updateNode to shared API**

Open `src/MSOSync.Frontend/src/shared/api/nodes.ts`. Read the current file first, then append:

```ts
export interface UpdateNodeRequest {
  groupId: string;
  syncUrl: string;
  heartbeatInterval: number;
}

export async function updateNode(nodeId: string, data: UpdateNodeRequest): Promise<void> {
  await client.put(`/nodes/${encodeURIComponent(nodeId)}`, data);
}
```

Keep all existing exports (`enableNode`, `disableNode`, `approveRegistration`, and `getNodes` if it exists). Full file after edit:

```ts
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

export interface UpdateNodeRequest {
  groupId: string;
  syncUrl: string;
  heartbeatInterval: number;
}

export async function updateNode(nodeId: string, data: UpdateNodeRequest): Promise<void> {
  await client.put(`/nodes/${encodeURIComponent(nodeId)}`, data);
}
```

**Note:** Read the actual file first. The exact signatures of `enableNode`, `disableNode`, `approveRegistration`, and `getNodes` may differ slightly — preserve them exactly as-is, only adding the new exports.

- [ ] **Step 3: Create schemas.ts**

Create `src/MSOSync.Frontend/src/features/nodes/schemas.ts`:

```ts
import { z } from 'zod';
import type { NodeDto } from '../../shared/types';

export const updateNodeSchema = z.object({
  groupId: z.string().min(1, 'Group is required'),
  syncUrl: z.string().url('Must be a valid URL including http:// or https://'),
  heartbeatInterval: z.coerce.number().int().min(1).max(1440),
});
export type UpdateNodeForm = z.infer<typeof updateNodeSchema>;

export function getDefaultValues(initialValues: NodeDto): UpdateNodeForm {
  return {
    groupId: initialValues.groupId,
    syncUrl: initialValues.syncUrl ?? '',
    heartbeatInterval: initialValues.heartbeatInterval ?? 5,
  };
}
```

- [ ] **Step 4: Create NodeDialog.tsx**

Create `src/MSOSync.Frontend/src/features/nodes/NodeDialog.tsx`:

```tsx
import { useEffect, useState, useMemo } from 'react';
import { useForm } from 'react-hook-form';
import { zodResolver } from '@hookform/resolvers/zod';
import { toast } from 'sonner';
import {
  Form,
  FormControl,
  FormField,
  FormItem,
  FormLabel,
  FormMessage,
} from '../../components/ui/form';
import { Input } from '../../components/ui/input';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { updateNodeSchema, getDefaultValues } from './schemas';
import type { UpdateNodeForm } from './schemas';
import { useUpdateNodeMutation } from './mutations';
import { useTopologyGroups } from '../topology/hooks';
import type { NodeDto } from '../../shared/types';

interface NodeDialogProps {
  open: boolean;
  initialValues: NodeDto;
  onOpenChange: (open: boolean) => void;
}

export function NodeDialog({ open, initialValues, onOpenChange }: NodeDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const mutation = useUpdateNodeMutation();
  const { data: groups } = useTopologyGroups();

  const defaultValues = useMemo(
    () => getDefaultValues(initialValues),
    [initialValues],
  );

  const form = useForm<UpdateNodeForm>({
    resolver: zodResolver(updateNodeSchema),
    defaultValues,
  });

  useEffect(() => {
    if (open) {
      form.reset(defaultValues);
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, defaultValues, form]);

  const onSubmit = async (values: UpdateNodeForm) => {
    setApiError(null);
    try {
      await mutation.mutateAsync({ nodeId: initialValues.nodeId, data: values });
      toast.success('Node updated');
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  return (
    <EntityDialog
      open={open}
      title={`Edit Node: ${initialValues.name || initialValues.nodeId}`}
      onOpenChange={onOpenChange}
    >
      <Form {...form}>
        <form onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }} className="flex flex-col gap-4">
          <FormField
            control={form.control}
            name="groupId"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Node Group</FormLabel>
                <Select onValueChange={field.onChange} value={field.value}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue placeholder="Select group…" />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {(groups ?? []).map((g) => (
                      <SelectItem key={g.groupId} value={g.groupId}>
                        {g.name}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="syncUrl"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Sync URL</FormLabel>
                <FormControl>
                  <Input {...field} placeholder="https://node.example.com" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="heartbeatInterval"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Heartbeat Interval (minutes, 1–1440)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel="Save Changes"
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
```

**Note on `groups` shape:** `useTopologyGroups()` returns `useQuery` result whose `data` is `TopologyGroupDto[]` directly (the API function `getTopologyGroups()` returns `TopologyGroupDto[]`). Use `(groups ?? []).map(...)`. If you get a TypeScript error, check `src/shared/api/topology.ts` for the actual return type.

- [ ] **Step 5: Append useUpdateNodeMutation to mutations.ts**

Open `src/MSOSync.Frontend/src/features/nodes/mutations.ts`. APPEND after the existing 3 hooks and the `invalidateNodeRelated` helper. Do not modify any existing code.

Append these imports at the top:
```ts
import { updateNode } from '../../shared/api/nodes';
import type { UpdateNodeRequest } from '../../shared/api/nodes';
```

Append this function at the bottom:
```ts
export function useUpdateNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ nodeId, data }: { nodeId: string; data: UpdateNodeRequest }) =>
      updateNode(nodeId, data),
    onSuccess: () => { invalidateNodeRelated(queryClient); },
    // no onError — caller handles it
  });
}
```

Full file after edit:

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { enableNode, disableNode, approveRegistration, updateNode } from '../../shared/api/nodes';
import type { UpdateNodeRequest } from '../../shared/api/nodes';
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
    onError: (error) => { toast.error(getErrorMessage(error)); },
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
    onError: (error) => { toast.error(getErrorMessage(error)); },
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
    onError: (error) => { toast.error(getErrorMessage(error)); },
  });
}

export function useUpdateNodeMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ nodeId, data }: { nodeId: string; data: UpdateNodeRequest }) =>
      updateNode(nodeId, data),
    onSuccess: () => { invalidateNodeRelated(queryClient); },
    // no onError — caller handles it
  });
}
```

- [ ] **Step 6: Extend makeNodeColumns factory**

Open `src/MSOSync.Frontend/src/features/nodes/columns.ts`. Add `onEdit` param to the factory and add an "Edit" item to the existing ActionMenu. The existing `NodeAction` type and existing menu items (Enable/Disable/Approve Registration) must remain completely unchanged.

Update the factory signature from:
```ts
export function makeNodeColumns(
  onAction: (nodeId: string, action: NodeAction) => void,
): ColDef<NodeDto>[]
```
to:
```ts
export function makeNodeColumns(
  onAction: (nodeId: string, action: NodeAction) => void,
  onEdit: (node: NodeDto) => void,
): ColDef<NodeDto>[]
```

In the ActionMenu items array, append before the destructive items:
```ts
{ label: 'Edit', onClick: () => onEdit(p.data) },
```

Full file after edit:

```ts
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
  onEdit: (node: NodeDto) => void,
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
        const node = p.data;
        return ActionMenu({
          items: [
            { label: 'Edit', onClick: () => onEdit(node) },
            { label: 'Enable', onClick: () => onAction(nodeId, 'enable') },
            { label: 'Disable', onClick: () => onAction(nodeId, 'disable'), variant: 'destructive' },
            { label: 'Approve Registration', onClick: () => onAction(nodeId, 'approve') },
          ],
        });
      },
    },
  ];
}
```

- [ ] **Step 7: Update NodesGrid**

Open `src/MSOSync.Frontend/src/features/nodes/NodesGrid.tsx`. Full file after edit:

```tsx
import { useCallback, useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeNodeColumns } from './columns';
import { useNodes } from './hooks';
import type { NodeDto } from '../../shared/types';

type NodeAction = 'enable' | 'disable' | 'approve';

interface Props {
  quickFilterText?: string;
  onAction: (nodeId: string, action: NodeAction) => void;
  onEdit: (node: NodeDto) => void;
}

export function NodesGrid({ quickFilterText, onAction, onEdit }: Props) {
  const { data, isLoading, error, refetch } = useNodes();
  const columns = useMemo(() => makeNodeColumns(onAction, onEdit), [onAction, onEdit]);
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

**Note:** Verify that `useNodes()` exists in `src/features/nodes/hooks.ts`. If NodesGrid currently uses a different hook name, keep it.

- [ ] **Step 8: Update NodesPage**

Open `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`. Add `editState` and `NodeDialog` alongside the existing `confirmState` logic. Full file after edit:

```tsx
import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { ConfirmDialog } from '../../shared/components/actions';
import { NodesGrid } from './NodesGrid';
import { NodeDialog } from './NodeDialog';
import {
  useEnableNodeMutation,
  useDisableNodeMutation,
  useApproveRegistrationMutation,
} from './mutations';
import type { NodeDto } from '../../shared/types';

type NodeAction = 'enable' | 'disable' | 'approve';

interface ConfirmState {
  nodeId: string;
  action: NodeAction;
}

const CONFIRM_CONFIG: Record<
  NodeAction,
  { title: string; description: (nodeId: string) => string; confirmLabel: string; variant: 'default' | 'destructive' }
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
  const [editState, setEditState] = useState<NodeDto | null>(null);

  const enableMutation = useEnableNodeMutation();
  const disableMutation = useDisableNodeMutation();
  const approveMutation = useApproveRegistrationMutation();

  const onAction = useCallback((nodeId: string, action: NodeAction) => {
    setConfirmState({ nodeId, action });
  }, []);

  const onEdit = useCallback((node: NodeDto) => { setEditState(node); }, []);

  const isPending =
    enableMutation.isPending || disableMutation.isPending || approveMutation.isPending;

  const handleConfirm = async () => {
    if (!confirmState) return;
    const { nodeId, action } = confirmState;
    try {
      if (action === 'enable') await enableMutation.mutateAsync(nodeId);
      else if (action === 'disable') await disableMutation.mutateAsync(nodeId);
      else await approveMutation.mutateAsync(nodeId);
    } finally {
      setConfirmState(null);
    }
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
      <NodesGrid quickFilterText={search} onAction={onAction} onEdit={onEdit} />
      {editState && (
        <NodeDialog
          open={!!editState}
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {config && confirmState && (
        <ConfirmDialog
          open
          title={config.title}
          description={config.description(confirmState.nodeId)}
          confirmLabel={config.confirmLabel}
          variant={config.variant}
          loading={isPending}
          onConfirm={() => void handleConfirm()}
          onOpenChange={(open) => { if (!open) setConfirmState(null); }}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 9: Verify build clean and tests pass**

Run from `src/MSOSync.Frontend/`:
```bash
npm run build
npm test -- --run
```
Expected: build exits 0, 12/12 tests pass.

- [ ] **Step 10: Commit**

```bash
git add src/MSOSync.Frontend/src/shared/types/nodes.ts
git add src/MSOSync.Frontend/src/shared/api/nodes.ts
git add src/MSOSync.Frontend/src/features/nodes/schemas.ts
git add src/MSOSync.Frontend/src/features/nodes/NodeDialog.tsx
git add src/MSOSync.Frontend/src/features/nodes/mutations.ts
git add src/MSOSync.Frontend/src/features/nodes/columns.ts
git add src/MSOSync.Frontend/src/features/nodes/NodesGrid.tsx
git add src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx
git commit -m "feat(10d): add nodes edit form (extends 10C actions)"
```
