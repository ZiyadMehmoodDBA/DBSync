# Epic 10D — Task 4: Channels CRUD Forms

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Create, Edit, and Delete forms for Channels.

**Architecture:** Single `ChannelDialog` with `mode: 'create' | 'edit'`. Delete goes through `ConfirmDialog`. Number inputs use `inputMode="numeric"`. `channelId` locked (read-only display) in edit mode.

**Prerequisite:** Task 1 complete (shared form components installed).

## Global Constraints

- TypeScript strict; no `any`; relative imports only
- `mutateAsync()` everywhere — never `mutate()`
- No new Vitest tests — `npm run build` exits 0 = done
- Mutation hooks have NO `onError` — dialog/page owns error display
- Number inputs use `z.coerce.number()` and `inputMode="numeric"`

---

## Interfaces

**Consumes from Task 1:**
```tsx
import { EntityDialog, FormActions, FormError, FormSection } from '../../shared/components/forms';
```

**Existing types:**
```ts
// src/shared/types/channels.ts
interface ChannelDto {
  channelId: string;
  name: string;
  description?: string;
  enabled: boolean;
  createdTime: string;
}
```

**Existing API (src/shared/api/channels.ts):**
```ts
export async function getChannels(): Promise<ChannelDto[]> { ... }
```

**queryKeys:**
```ts
queryKeys.channels()        // → ['channels']
queryKeys.topologySummary() // → ['topology-summary']
queryKeys.topologyGroups()  // → ['topology-groups']
```

**Produces:**
- API: `createChannel(data)`, `updateChannel(channelId, data)`, `deleteChannel(channelId)`
- Hooks: `useCreateChannelMutation()`, `useUpdateChannelMutation()`, `useDeleteChannelMutation()`
- Dialog: `ChannelDialog` — props `{ open, mode, initialValues?, onOpenChange }`
- Schema types: `CreateChannelForm`, `UpdateChannelForm`

---

## Files

**Modify:**
- `src/MSOSync.Frontend/src/shared/api/channels.ts` — add 3 API functions

**Create:**
- `src/MSOSync.Frontend/src/features/channels/schemas.ts`
- `src/MSOSync.Frontend/src/features/channels/mutations.ts`
- `src/MSOSync.Frontend/src/features/channels/ChannelDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/features/channels/columns.ts` — convert to factory
- `src/MSOSync.Frontend/src/features/channels/ChannelsGrid.tsx` — add props
- `src/MSOSync.Frontend/src/features/channels/ChannelsPage.tsx` — add state + dialogs

---

## Steps

- [ ] **Step 1: Add channel API functions**

Open `src/MSOSync.Frontend/src/shared/api/channels.ts`. Full file after edit:

```ts
import client from './client';
import type { ChannelDto } from '../types';

export async function getChannels(): Promise<ChannelDto[]> {
  const { data } = await client.get<ChannelDto[]>('/channels');
  return data;
}

export interface CreateChannelRequest {
  channelId: string;
  priority: number;
  batchSize: number;
  maxBatchToSend: number;
  maxDataSize: number;
}

export interface UpdateChannelRequest {
  priority: number;
  batchSize: number;
  maxBatchToSend: number;
  maxDataSize: number;
}

export async function createChannel(data: CreateChannelRequest): Promise<void> {
  await client.post('/channels', data);
}

export async function updateChannel(channelId: string, data: UpdateChannelRequest): Promise<void> {
  await client.put(`/channels/${encodeURIComponent(channelId)}`, data);
}

export async function deleteChannel(channelId: string): Promise<void> {
  await client.delete(`/channels/${encodeURIComponent(channelId)}`);
}
```

- [ ] **Step 2: Create schemas.ts**

Create `src/MSOSync.Frontend/src/features/channels/schemas.ts`:

```ts
import { z } from 'zod';
import type { ChannelDto } from '../../shared/types';

export const createChannelSchema = z.object({
  channelId: z.string().trim().min(1, 'Channel ID is required'),
  priority: z.coerce.number().int().min(0).max(100),
  batchSize: z.coerce.number().int().min(1).max(1_000_000),
  maxBatchToSend: z.coerce.number().int().min(1).max(10_000),
  maxDataSize: z.coerce.number().int().min(1),
});
export type CreateChannelForm = z.infer<typeof createChannelSchema>;

export const updateChannelSchema = createChannelSchema.omit({ channelId: true });
export type UpdateChannelForm = z.infer<typeof updateChannelSchema>;

export function getDefaultValues(
  initialValues?: ChannelDto,
  mode?: 'create' | 'edit',
): CreateChannelForm | UpdateChannelForm {
  if (mode === 'edit' && initialValues) {
    return {
      priority: 0,
      batchSize: 1000,
      maxBatchToSend: 10,
      maxDataSize: 1048576,
    };
  }
  return {
    channelId: '',
    priority: 0,
    batchSize: 1000,
    maxBatchToSend: 10,
    maxDataSize: 1048576,
  };
}
```

- [ ] **Step 3: Create mutations.ts**

Create `src/MSOSync.Frontend/src/features/channels/mutations.ts`:

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createChannel, updateChannel, deleteChannel } from '../../shared/api/channels';
import type { CreateChannelRequest, UpdateChannelRequest } from '../../shared/api/channels';
import { queryKeys } from '../../shared/queryKeys';

function invalidateChannelRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.channels() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useCreateChannelMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateChannelRequest) => createChannel(data),
    onSuccess: () => { invalidateChannelRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateChannelMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ channelId, data }: { channelId: string; data: UpdateChannelRequest }) =>
      updateChannel(channelId, data),
    onSuccess: () => { invalidateChannelRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeleteChannelMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (channelId: string) => deleteChannel(channelId),
    onSuccess: () => { invalidateChannelRelated(queryClient); },
    // no onError — caller handles it
  });
}
```

- [ ] **Step 4: Create ChannelDialog.tsx**

Create `src/MSOSync.Frontend/src/features/channels/ChannelDialog.tsx`:

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
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { createChannelSchema, updateChannelSchema, getDefaultValues } from './schemas';
import type { CreateChannelForm, UpdateChannelForm } from './schemas';
import { useCreateChannelMutation, useUpdateChannelMutation } from './mutations';
import type { ChannelDto } from '../../shared/types';

interface ChannelDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: ChannelDto;
  onOpenChange: (open: boolean) => void;
}

export function ChannelDialog({ open, mode, initialValues, onOpenChange }: ChannelDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateChannelMutation();
  const updateMutation = useUpdateChannelMutation();

  const schema = mode === 'create' ? createChannelSchema : updateChannelSchema;
  const defaultValues = useMemo(
    () => getDefaultValues(initialValues, mode),
    [initialValues, mode],
  );

  const form = useForm<CreateChannelForm | UpdateChannelForm>({
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

  const onSubmit = async (values: CreateChannelForm | UpdateChannelForm) => {
    setApiError(null);
    try {
      if (mode === 'create') {
        const v = values as CreateChannelForm;
        await createMutation.mutateAsync(v);
        toast.success('Channel created');
      } else {
        if (!initialValues) return;
        const v = values as UpdateChannelForm;
        await updateMutation.mutateAsync({ channelId: initialValues.channelId, data: v });
        toast.success('Channel updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title = mode === 'create' ? 'Add Channel' : `Edit Channel: ${initialValues?.channelId ?? ''}`;

  return (
    <EntityDialog open={open} title={title} onOpenChange={onOpenChange}>
      <Form {...form}>
        <form onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }} className="flex flex-col gap-4">
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="channelId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Channel ID</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="e.g. default" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormField
            control={form.control}
            name="priority"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Priority (0–100)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="batchSize"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Batch Size (1–1,000,000)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="maxBatchToSend"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Max Batches to Send (1–10,000)</FormLabel>
                <FormControl>
                  <Input {...field} inputMode="numeric" />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormField
            control={form.control}
            name="maxDataSize"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Max Data Size (bytes)</FormLabel>
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
            submitLabel={mode === 'create' ? 'Create Channel' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
```

- [ ] **Step 5: Convert channelColumns to factory**

Open `src/MSOSync.Frontend/src/features/channels/columns.ts`. Full file after edit:

```ts
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { ChannelDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

export function makeChannelColumns(
  onEdit: (row: ChannelDto) => void,
  onDelete: (row: ChannelDto) => void,
): ColDef<ChannelDto>[] {
  return [
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
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<ChannelDto>) => {
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

- [ ] **Step 6: Update ChannelsGrid**

Open `src/MSOSync.Frontend/src/features/channels/ChannelsGrid.tsx`. Full file after edit:

```tsx
import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeChannelColumns } from './columns';
import { useChannels } from './hooks';
import type { ChannelDto } from '../../shared/types';

interface Props {
  quickFilterText?: string;
  onEdit: (row: ChannelDto) => void;
  onDelete: (row: ChannelDto) => void;
}

export function ChannelsGrid({ quickFilterText, onEdit, onDelete }: Props) {
  const { data, isLoading, error, refetch } = useChannels();
  const columns = useMemo(() => makeChannelColumns(onEdit, onDelete), [onEdit, onDelete]);
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

- [ ] **Step 7: Update ChannelsPage**

Open `src/MSOSync.Frontend/src/features/channels/ChannelsPage.tsx`. Full file after edit:

```tsx
import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { ChannelsGrid } from './ChannelsGrid';
import { ChannelDialog } from './ChannelDialog';
import { useDeleteChannelMutation } from './mutations';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { ChannelDto } from '../../shared/types';

export function ChannelsPage() {
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<ChannelDto | null>(null);
  const [deleteState, setDeleteState] = useState<ChannelDto | null>(null);

  const deleteMutation = useDeleteChannelMutation();

  const onEdit = useCallback((row: ChannelDto) => { setEditState(row); }, []);
  const onDelete = useCallback((row: ChannelDto) => { setDeleteState(row); }, []);

  const handleDeleteConfirm = async () => {
    if (!deleteState) return;
    try {
      await deleteMutation.mutateAsync(deleteState.channelId);
      toast.success(`Channel "${deleteState.channelId}" deleted`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeleteState(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Channels</h1>
        <Button onClick={() => setCreateOpen(true)}>Add Channel</Button>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search channels…"
        className="max-w-xs"
      />
      <ChannelsGrid quickFilterText={search} onEdit={onEdit} onDelete={onDelete} />
      <ChannelDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <ChannelDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {deleteState && (
        <ConfirmDialog
          open
          title="Delete Channel"
          description={`Delete channel "${deleteState.channelId}"? This cannot be undone.`}
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
git add src/MSOSync.Frontend/src/shared/api/channels.ts
git add src/MSOSync.Frontend/src/features/channels/schemas.ts
git add src/MSOSync.Frontend/src/features/channels/mutations.ts
git add src/MSOSync.Frontend/src/features/channels/ChannelDialog.tsx
git add src/MSOSync.Frontend/src/features/channels/columns.ts
git add src/MSOSync.Frontend/src/features/channels/ChannelsGrid.tsx
git add src/MSOSync.Frontend/src/features/channels/ChannelsPage.tsx
git commit -m "feat(10d): add channels create/edit/delete forms"
```
