# Epic 10D — Task 6: Routers CRUD Forms

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Create, Edit, and Delete forms for Routers. No 10C mutations exist for Routers.

**Architecture:** Single `RouterDialog` with `mode: 'create' | 'edit'`. Delete goes through `ConfirmDialog`. Source and target node groups use `Select` populated from `useTopologyGroups()`. Schema validates that source ≠ target group.

**Critical DTO mismatch:** `RouterDto` response has `sourceGroupId`/`targetGroupId`, but the backend create/update request uses `sourceNodeGroup`/`targetNodeGroup`. Edit mode default values map from response fields:
```ts
{ sourceNodeGroup: router.sourceGroupId, targetNodeGroup: router.targetGroupId }
```

**Prerequisite:** Task 1 complete (shared form components installed).

## Global Constraints

- TypeScript strict; no `any`; relative imports only
- `mutateAsync()` everywhere — never `mutate()`
- No new Vitest tests — `npm run build` exits 0 = done
- Mutation hooks have NO `onError` — dialog/page owns error display
- Group Select items come from `useTopologyGroups()` in `features/topology/hooks.ts`

---

## Interfaces

**Consumes from Task 1:**
```tsx
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
```

**Existing types:**
```ts
// src/shared/types/routers.ts
interface RouterDto {
  routerId: string;
  name: string;
  sourceGroupId: string;   // ← response field name
  targetGroupId: string;   // ← response field name
  channelIds: string[];
  enabled: boolean;
  createdTime: string;
}

// src/shared/types/topology.ts
interface TopologyGroupDto {
  groupId: string;
  name: string;
  totalNodes: number;
  // ...more fields
}
```

**Existing API (src/shared/api/routers.ts):**
```ts
export async function getRouters(): Promise<RouterDto[]> { ... }
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
queryKeys.routers()         // → ['routers']
queryKeys.topologySummary() // → ['topology-summary']
queryKeys.topologyGroups()  // → ['topology-groups']
```

**Produces:**
- API: `createRouter(data)`, `updateRouter(routerId, data)`, `deleteRouter(routerId)`
- Hooks: `useCreateRouterMutation()`, `useUpdateRouterMutation()`, `useDeleteRouterMutation()`
- Dialog: `RouterDialog` — props `{ open, mode, initialValues?, onOpenChange }`
- Schema types: `CreateRouterForm`, `UpdateRouterForm`; constant `ROUTER_TYPES`

---

## Files

**Modify:**
- `src/MSOSync.Frontend/src/shared/api/routers.ts` — add 3 API functions

**Create:**
- `src/MSOSync.Frontend/src/features/routers/schemas.ts`
- `src/MSOSync.Frontend/src/features/routers/mutations.ts`
- `src/MSOSync.Frontend/src/features/routers/RouterDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/features/routers/columns.ts` — convert to factory
- `src/MSOSync.Frontend/src/features/routers/RoutersGrid.tsx` — add props
- `src/MSOSync.Frontend/src/features/routers/RoutersPage.tsx` — add state + dialogs

---

## Steps

- [ ] **Step 1: Add router API functions**

Open `src/MSOSync.Frontend/src/shared/api/routers.ts`. Full file after edit:

```ts
import client from './client';
import type { RouterDto } from '../types';

export async function getRouters(): Promise<RouterDto[]> {
  const { data } = await client.get<RouterDto[]>('/routers');
  return data;
}

export interface CreateRouterRequest {
  routerId: string;
  sourceNodeGroup: string;  // backend request field name
  targetNodeGroup: string;  // backend request field name
  routerType: string;
}

export interface UpdateRouterRequest {
  sourceNodeGroup: string;
  targetNodeGroup: string;
  routerType: string;
}

export async function createRouter(data: CreateRouterRequest): Promise<void> {
  await client.post('/routers', data);
}

export async function updateRouter(routerId: string, data: UpdateRouterRequest): Promise<void> {
  await client.put(`/routers/${encodeURIComponent(routerId)}`, data);
}

export async function deleteRouter(routerId: string): Promise<void> {
  await client.delete(`/routers/${encodeURIComponent(routerId)}`);
}
```

- [ ] **Step 2: Create schemas.ts**

Create `src/MSOSync.Frontend/src/features/routers/schemas.ts`:

```ts
import { z } from 'zod';
import type { RouterDto } from '../../shared/types';

export const ROUTER_TYPES = ['default'] as const;
export type RouterType = typeof ROUTER_TYPES[number];

export const createRouterSchema = z.object({
  routerId: z.string().trim().min(1, 'Router ID is required'),
  sourceNodeGroup: z.string().trim().min(1, 'Source group is required'),
  targetNodeGroup: z.string().trim().min(1, 'Target group is required'),
  routerType: z.enum(ROUTER_TYPES),
}).refine(
  (x) => x.sourceNodeGroup !== x.targetNodeGroup,
  { message: 'Source and target groups must differ.', path: ['targetNodeGroup'] },
);
export type CreateRouterForm = z.infer<typeof createRouterSchema>;

export const updateRouterSchema = createRouterSchema.omit({ routerId: true });
export type UpdateRouterForm = z.infer<typeof updateRouterSchema>;

export function getDefaultValues(
  initialValues?: RouterDto,
  mode?: 'create' | 'edit',
): CreateRouterForm | UpdateRouterForm {
  if (mode === 'edit' && initialValues) {
    return {
      // Map response fields (sourceGroupId) to request fields (sourceNodeGroup)
      sourceNodeGroup: initialValues.sourceGroupId,
      targetNodeGroup: initialValues.targetGroupId,
      routerType: 'default',
    };
  }
  return {
    routerId: '',
    sourceNodeGroup: '',
    targetNodeGroup: '',
    routerType: 'default',
  };
}
```

- [ ] **Step 3: Create mutations.ts**

Create `src/MSOSync.Frontend/src/features/routers/mutations.ts`:

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createRouter, updateRouter, deleteRouter } from '../../shared/api/routers';
import type { CreateRouterRequest, UpdateRouterRequest } from '../../shared/api/routers';
import { queryKeys } from '../../shared/queryKeys';

function invalidateRouterRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.routers() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useCreateRouterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateRouterRequest) => createRouter(data),
    onSuccess: () => { invalidateRouterRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateRouterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ routerId, data }: { routerId: string; data: UpdateRouterRequest }) =>
      updateRouter(routerId, data),
    onSuccess: () => { invalidateRouterRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeleteRouterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (routerId: string) => deleteRouter(routerId),
    onSuccess: () => { invalidateRouterRelated(queryClient); },
    // no onError — caller handles it
  });
}
```

- [ ] **Step 4: Create RouterDialog.tsx**

Create `src/MSOSync.Frontend/src/features/routers/RouterDialog.tsx`:

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
import { createRouterSchema, updateRouterSchema, getDefaultValues, ROUTER_TYPES } from './schemas';
import type { CreateRouterForm, UpdateRouterForm } from './schemas';
import { useCreateRouterMutation, useUpdateRouterMutation } from './mutations';
import { useTopologyGroups } from '../topology/hooks';
import type { RouterDto } from '../../shared/types';

interface RouterDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: RouterDto;
  onOpenChange: (open: boolean) => void;
}

export function RouterDialog({ open, mode, initialValues, onOpenChange }: RouterDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateRouterMutation();
  const updateMutation = useUpdateRouterMutation();
  const { data: groups } = useTopologyGroups();

  const schema = mode === 'create' ? createRouterSchema : updateRouterSchema;
  const defaultValues = useMemo(
    () => getDefaultValues(initialValues, mode),
    [initialValues, mode],
  );

  const form = useForm<CreateRouterForm | UpdateRouterForm>({
    resolver: zodResolver(schema),
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

  const onSubmit = async (values: CreateRouterForm | UpdateRouterForm) => {
    setApiError(null);
    try {
      if (mode === 'create') {
        const v = values as CreateRouterForm;
        await createMutation.mutateAsync(v);
        toast.success('Router created');
      } else {
        if (!initialValues) return;
        const v = values as UpdateRouterForm;
        await updateMutation.mutateAsync({ routerId: initialValues.routerId, data: v });
        toast.success('Router updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title = mode === 'create' ? 'Add Router' : `Edit Router: ${initialValues?.routerId ?? ''}`;

  return (
    <EntityDialog open={open} title={title} onOpenChange={onOpenChange}>
      <Form {...form}>
        <form onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }} className="flex flex-col gap-4">
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="routerId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Router ID</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="e.g. hub-to-spoke" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormField
            control={form.control}
            name="sourceNodeGroup"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Source Node Group</FormLabel>
                <Select onValueChange={field.onChange} value={field.value as string}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue placeholder="Select source group…" />
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
            name="targetNodeGroup"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Target Node Group</FormLabel>
                <Select onValueChange={field.onChange} value={field.value as string}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue placeholder="Select target group…" />
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
            name="routerType"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Router Type</FormLabel>
                <Select onValueChange={field.onChange} value={field.value as string}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {ROUTER_TYPES.map((t) => (
                      <SelectItem key={t} value={t}>
                        {t}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel={mode === 'create' ? 'Create Router' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
```

**Note:** `useTopologyGroups()` data is `TopologyGroupDto[] | undefined` — `getTopologyGroups()` returns the array directly. Use `(groups ?? []).map(...)` as written above.

- [ ] **Step 5: Convert routerColumns to factory**

Open `src/MSOSync.Frontend/src/features/routers/columns.ts`. Full file after edit:

```ts
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { RouterDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

export function makeRouterColumns(
  onEdit: (row: RouterDto) => void,
  onDelete: (row: RouterDto) => void,
): ColDef<RouterDto>[] {
  return [
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
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<RouterDto>) => {
        if (!p.data) return null;
        const row = p.data;
        return ActionMenu({
          items: [
            { label: 'Edit', onClick: () => onEdit(row) },
            { label: 'Delete', onClick: () => onDelete(row), variant: 'destructive' },
          ],
        });
      },
    },
  ];
}
```

- [ ] **Step 6: Update RoutersGrid**

Open `src/MSOSync.Frontend/src/features/routers/RoutersGrid.tsx`. Full file after edit:

```tsx
import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeRouterColumns } from './columns';
import { useRouters } from './hooks';
import type { RouterDto } from '../../shared/types';

interface Props {
  quickFilterText?: string;
  onEdit: (row: RouterDto) => void;
  onDelete: (row: RouterDto) => void;
}

export function RoutersGrid({ quickFilterText, onEdit, onDelete }: Props) {
  const { data, isLoading, error, refetch } = useRouters();
  const columns = useMemo(() => makeRouterColumns(onEdit, onDelete), [onEdit, onDelete]);
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

- [ ] **Step 7: Update RoutersPage**

Open `src/MSOSync.Frontend/src/features/routers/RoutersPage.tsx`. Full file after edit:

```tsx
import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { RoutersGrid } from './RoutersGrid';
import { RouterDialog } from './RouterDialog';
import { useDeleteRouterMutation } from './mutations';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { RouterDto } from '../../shared/types';

export function RoutersPage() {
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<RouterDto | null>(null);
  const [deleteState, setDeleteState] = useState<RouterDto | null>(null);

  const deleteMutation = useDeleteRouterMutation();

  const onEdit = useCallback((row: RouterDto) => { setEditState(row); }, []);
  const onDelete = useCallback((row: RouterDto) => { setDeleteState(row); }, []);

  const handleDeleteConfirm = async () => {
    if (!deleteState) return;
    try {
      await deleteMutation.mutateAsync(deleteState.routerId);
      toast.success(`Router "${deleteState.routerId}" deleted`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeleteState(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Routers</h1>
        <Button onClick={() => setCreateOpen(true)}>Add Router</Button>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search routers…"
        className="max-w-xs"
      />
      <RoutersGrid quickFilterText={search} onEdit={onEdit} onDelete={onDelete} />
      <RouterDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <RouterDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {deleteState && (
        <ConfirmDialog
          open
          title="Delete Router"
          description={`Delete router "${deleteState.routerId}"? This cannot be undone.`}
          confirmLabel="Delete"
          variant="destructive"
          loading={deleteMutation.isPending}
          onConfirm={() => void handleDeleteConfirm()}
          onOpenChange={(open) => { if (!open) setDeleteState(null); }}
        />
      )}
    </div>
  );
}
```

- [ ] **Step 8: Verify build clean**

Run from `src/MSOSync.Frontend/`:
```bash
npm run build
```
Expected: exits 0, no TypeScript errors.

- [ ] **Step 9: Commit**

```bash
git add src/MSOSync.Frontend/src/shared/api/routers.ts
git add src/MSOSync.Frontend/src/features/routers/schemas.ts
git add src/MSOSync.Frontend/src/features/routers/mutations.ts
git add src/MSOSync.Frontend/src/features/routers/RouterDialog.tsx
git add src/MSOSync.Frontend/src/features/routers/columns.ts
git add src/MSOSync.Frontend/src/features/routers/RoutersGrid.tsx
git add src/MSOSync.Frontend/src/features/routers/RoutersPage.tsx
git commit -m "feat(10d): add routers create/edit/delete forms"
```
