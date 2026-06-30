# Epic 10D — Task 5: Triggers CRUD Forms (extends 10C)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Create, Edit, and Delete forms for Triggers. The 4 existing 10C operational mutations (enable/disable/rebuild/verify) are preserved completely unchanged.

**Architecture:** Single `TriggerDialog` with `mode: 'create' | 'edit'`. Delete goes through `ConfirmDialog`. Form splits source table into `schemaName` + `tableName` fields; submit merges them via `toSourceTable()`. `channelId` is a Select populated from `useChannels()`. `triggerId` shown read-only in edit mode.

**Critical:** The `TriggerDto` response has `captureInsert`/`captureUpdate`/`captureDelete` fields, but the create/update request uses `syncOnInsert`/`syncOnUpdate`/`syncOnDelete`. Edit mode pre-populates form fields `syncOnInsert` from `trigger.captureInsert`, etc.

**Prerequisite:** Task 1 complete (shared form components installed).

## Global Constraints

- TypeScript strict; no `any`; relative imports only
- `mutateAsync()` everywhere — never `mutate()`
- No new Vitest tests — `npm run build` exits 0 = done
- 3 new CRUD mutation hooks have NO `onError` — dialog/page owns error display
- Existing 4 operational hooks (enable/disable/rebuild/verify) with `onError: toast.error()` are NOT modified
- Backend trigger create/update request uses `sourceTable: "schema.table"` (merged), not separate fields

---

## Interfaces

**Consumes from Task 1:**
```tsx
import { EntityDialog, FormActions, FormError, FormSection } from '../../shared/components/forms';
```

**Existing types:**
```ts
// src/shared/types/triggers.ts
interface TriggerDto {
  triggerId: string;
  channelId: string;
  tableName: string;
  schemaName: string;
  captureInsert: boolean;
  captureUpdate: boolean;
  captureDelete: boolean;
  enabled: boolean;
  createdTime: string;
}
```

**Existing API (src/shared/api/triggers.ts):**
```ts
export async function getTriggers(): Promise<TriggerDto[]> { ... }
export async function enableTrigger(triggerId: string): Promise<void> { ... }
export async function disableTrigger(triggerId: string): Promise<void> { ... }
export async function rebuildTrigger(triggerId: string): Promise<void> { ... }
export async function verifyTrigger(triggerId: string): Promise<void> { ... }
```

**Existing mutations (src/features/triggers/mutations.ts) — DO NOT MODIFY:**
```ts
export function useEnableTriggerMutation() { ... }   // has onError — preserved
export function useDisableTriggerMutation() { ... }  // has onError — preserved
export function useRebuildTriggerMutation() { ... }  // has onError — preserved
export function useVerifyTriggerMutation() { ... }   // has onError — preserved
```

**Existing columns factory:**
```ts
// src/features/triggers/columns.ts
export function makeTriggersColumns(
  onAction: (triggerId: string, action: ConfirmableAction) => void,
  onVerify: (triggerId: string) => void,
): ColDef<TriggerDto>[]
```

**Existing TriggersPage.tsx:**
```tsx
// manages: confirmState (for enable/disable/rebuild)
// TriggersGrid accepts: quickFilterText, onAction
```

**Channels hook:**
```ts
// src/features/channels/hooks.ts
export function useChannels() { ... }
// returns useQuery with queryKey: queryKeys.channels()
```

**queryKeys:**
```ts
queryKeys.triggers()        // → ['triggers']
queryKeys.topologySummary() // → ['topology-summary']
queryKeys.topologyGroups()  // → ['topology-groups']
```

**Produces:**
- Helper: `toSourceTable(schemaName, tableName)`, `fromSourceTable(sourceTable)` in `utils.ts`
- API: `createTrigger(data)`, `updateTrigger(triggerId, data)`, `deleteTrigger(triggerId)`
- Hooks: `useCreateTriggerMutation()`, `useUpdateTriggerMutation()`, `useDeleteTriggerMutation()`
- Dialog: `TriggerDialog` — props `{ open, mode, initialValues?, onOpenChange }`
- Schema types: `CreateTriggerForm`, `UpdateTriggerForm`

---

## Files

**Create:**
- `src/MSOSync.Frontend/src/features/triggers/utils.ts`
- `src/MSOSync.Frontend/src/features/triggers/schemas.ts`
- `src/MSOSync.Frontend/src/features/triggers/TriggerDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/shared/api/triggers.ts` — add 3 CRUD API functions
- `src/MSOSync.Frontend/src/features/triggers/mutations.ts` — APPEND 3 CRUD hooks (don't touch existing 4)
- `src/MSOSync.Frontend/src/features/triggers/columns.ts` — extend factory signature
- `src/MSOSync.Frontend/src/features/triggers/TriggersGrid.tsx` — add `onEdit`, `onDelete` props
- `src/MSOSync.Frontend/src/features/triggers/TriggersPage.tsx` — add create/edit/delete state

---

## Steps

- [ ] **Step 1: Create utils.ts**

Create `src/MSOSync.Frontend/src/features/triggers/utils.ts`:

```ts
export function toSourceTable(schemaName: string, tableName: string): string {
  return `${schemaName}.${tableName}`;
}

export function fromSourceTable(sourceTable: string): { schemaName: string; tableName: string } {
  const dot = sourceTable.lastIndexOf('.');
  if (dot === -1) return { schemaName: 'dbo', tableName: sourceTable };
  return { schemaName: sourceTable.slice(0, dot), tableName: sourceTable.slice(dot + 1) };
}
```

- [ ] **Step 2: Add CRUD API functions to triggers.ts**

Open `src/MSOSync.Frontend/src/shared/api/triggers.ts`. Full file after edit (preserve existing functions):

```ts
import client from './client';
import type { TriggerDto } from '../types';

export async function getTriggers(): Promise<TriggerDto[]> {
  const { data } = await client.get<TriggerDto[]>('/triggers');
  return data;
}

export async function enableTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/enable`);
}

export async function disableTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/disable`);
}

export async function rebuildTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/rebuild`);
}

export async function verifyTrigger(triggerId: string): Promise<void> {
  await client.post(`/triggers/${encodeURIComponent(triggerId)}/verify`);
}

export interface CreateTriggerRequest {
  triggerId: string;
  sourceTable: string;
  channelId: string;
  syncOnInsert: boolean;
  syncOnUpdate: boolean;
  syncOnDelete: boolean;
}

export interface UpdateTriggerRequest {
  sourceTable: string;
  channelId: string;
  syncOnInsert: boolean;
  syncOnUpdate: boolean;
  syncOnDelete: boolean;
}

export async function createTrigger(data: CreateTriggerRequest): Promise<void> {
  await client.post('/triggers', data);
}

export async function updateTrigger(triggerId: string, data: UpdateTriggerRequest): Promise<void> {
  await client.put(`/triggers/${encodeURIComponent(triggerId)}`, data);
}

export async function deleteTrigger(triggerId: string): Promise<void> {
  await client.delete(`/triggers/${encodeURIComponent(triggerId)}`);
}
```

- [ ] **Step 3: Create schemas.ts**

Create `src/MSOSync.Frontend/src/features/triggers/schemas.ts`:

```ts
import { z } from 'zod';
import type { TriggerDto } from '../../shared/types';

export const createTriggerSchema = z.object({
  triggerId: z.string().trim().min(1, 'Trigger ID is required'),
  schemaName: z.string().trim().min(1, 'Schema name is required'),
  tableName: z.string().trim().min(1, 'Table name is required'),
  channelId: z.string().min(1, 'Channel is required'),
  syncOnInsert: z.boolean(),
  syncOnUpdate: z.boolean(),
  syncOnDelete: z.boolean(),
}).refine(
  (x) => x.syncOnInsert || x.syncOnUpdate || x.syncOnDelete,
  { message: 'At least one sync operation must be enabled.', path: ['syncOnInsert'] },
);
export type CreateTriggerForm = z.infer<typeof createTriggerSchema>;

export const updateTriggerSchema = createTriggerSchema.omit({ triggerId: true });
export type UpdateTriggerForm = z.infer<typeof updateTriggerSchema>;

export function getDefaultValues(
  initialValues?: TriggerDto,
  mode?: 'create' | 'edit',
): CreateTriggerForm | UpdateTriggerForm {
  if (mode === 'edit' && initialValues) {
    return {
      schemaName: initialValues.schemaName,
      tableName: initialValues.tableName,
      channelId: initialValues.channelId,
      syncOnInsert: initialValues.captureInsert,
      syncOnUpdate: initialValues.captureUpdate,
      syncOnDelete: initialValues.captureDelete,
    };
  }
  return {
    triggerId: '',
    schemaName: 'dbo',
    tableName: '',
    channelId: '',
    syncOnInsert: true,
    syncOnUpdate: true,
    syncOnDelete: true,
  };
}
```

- [ ] **Step 4: Add CRUD hooks to mutations.ts**

Open `src/MSOSync.Frontend/src/features/triggers/mutations.ts`. APPEND after the existing 4 hooks (do not modify them):

```ts
// --- append these imports at the top ---
import { createTrigger, updateTrigger, deleteTrigger } from '../../shared/api/triggers';
import type { CreateTriggerRequest, UpdateTriggerRequest } from '../../shared/api/triggers';

// --- append these functions at the bottom ---
function invalidateTriggerRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useCreateTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateTriggerRequest) => createTrigger(data),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ triggerId, data }: { triggerId: string; data: UpdateTriggerRequest }) =>
      updateTrigger(triggerId, data),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeleteTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => deleteTrigger(triggerId),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
    // no onError — caller handles it
  });
}
```

Full final file (showing preserved existing hooks + new appended hooks):

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  enableTrigger,
  disableTrigger,
  rebuildTrigger,
  verifyTrigger,
  createTrigger,
  updateTrigger,
  deleteTrigger,
} from '../../shared/api/triggers';
import type { CreateTriggerRequest, UpdateTriggerRequest } from '../../shared/api/triggers';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

export function useEnableTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => enableTrigger(triggerId),
    onSuccess: () => {
      toast.success('Trigger enabled');
      void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
    },
    onError: (error) => { toast.error(getErrorMessage(error)); },
  });
}

export function useDisableTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => disableTrigger(triggerId),
    onSuccess: () => {
      toast.success('Trigger disabled');
      void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
    },
    onError: (error) => { toast.error(getErrorMessage(error)); },
  });
}

export function useRebuildTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => rebuildTrigger(triggerId),
    onSuccess: () => {
      toast.success('Trigger rebuilt');
      void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
    },
    onError: (error) => { toast.error(getErrorMessage(error)); },
  });
}

export function useVerifyTriggerMutation() {
  return useMutation({
    mutationFn: (triggerId: string) => verifyTrigger(triggerId),
    onSuccess: () => { toast.info('Trigger verified successfully.'); },
    onError: (error) => { toast.error(getErrorMessage(error)); },
  });
}

function invalidateTriggerRelated(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: queryKeys.triggers() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologySummary() });
  void queryClient.invalidateQueries({ queryKey: queryKeys.topologyGroups() });
}

export function useCreateTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateTriggerRequest) => createTrigger(data),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
  });
}

export function useUpdateTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ triggerId, data }: { triggerId: string; data: UpdateTriggerRequest }) =>
      updateTrigger(triggerId, data),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
  });
}

export function useDeleteTriggerMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (triggerId: string) => deleteTrigger(triggerId),
    onSuccess: () => { invalidateTriggerRelated(queryClient); },
  });
}
```

- [ ] **Step 5: Create TriggerDialog.tsx**

Create `src/MSOSync.Frontend/src/features/triggers/TriggerDialog.tsx`:

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
import { Checkbox } from '../../components/ui/checkbox';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '../../components/ui/select';
import { EntityDialog, FormActions, FormError, FormSection } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { createTriggerSchema, updateTriggerSchema, getDefaultValues } from './schemas';
import type { CreateTriggerForm, UpdateTriggerForm } from './schemas';
import { useCreateTriggerMutation, useUpdateTriggerMutation } from './mutations';
import { toSourceTable } from './utils';
import { useChannels } from '../channels/hooks';
import type { TriggerDto } from '../../shared/types';

interface TriggerDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: TriggerDto;
  onOpenChange: (open: boolean) => void;
}

export function TriggerDialog({ open, mode, initialValues, onOpenChange }: TriggerDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateTriggerMutation();
  const updateMutation = useUpdateTriggerMutation();
  const { data: channels } = useChannels();

  const schema = mode === 'create' ? createTriggerSchema : updateTriggerSchema;
  const defaultValues = useMemo(
    () => getDefaultValues(initialValues, mode),
    [initialValues, mode],
  );

  const form = useForm<CreateTriggerForm | UpdateTriggerForm>({
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

  const onSubmit = async (values: CreateTriggerForm | UpdateTriggerForm) => {
    setApiError(null);
    try {
      if (mode === 'create') {
        const v = values as CreateTriggerForm;
        await createMutation.mutateAsync({
          triggerId: v.triggerId,
          sourceTable: toSourceTable(v.schemaName, v.tableName),
          channelId: v.channelId,
          syncOnInsert: v.syncOnInsert,
          syncOnUpdate: v.syncOnUpdate,
          syncOnDelete: v.syncOnDelete,
        });
        toast.success('Trigger created');
      } else {
        if (!initialValues) return;
        const v = values as UpdateTriggerForm;
        await updateMutation.mutateAsync({
          triggerId: initialValues.triggerId,
          data: {
            sourceTable: toSourceTable(v.schemaName, v.tableName),
            channelId: v.channelId,
            syncOnInsert: v.syncOnInsert,
            syncOnUpdate: v.syncOnUpdate,
            syncOnDelete: v.syncOnDelete,
          },
        });
        toast.success('Trigger updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title = mode === 'create'
    ? 'Add Trigger'
    : `Edit Trigger: ${initialValues?.triggerId ?? ''}`;

  return (
    <EntityDialog open={open} title={title} onOpenChange={onOpenChange}>
      <Form {...form}>
        <form onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }} className="flex flex-col gap-4">
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="triggerId"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Trigger ID</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="e.g. trg_orders" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormSection title="Source Table">
            <FormField
              control={form.control}
              name="schemaName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Schema</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="dbo" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="tableName"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Table</FormLabel>
                  <FormControl>
                    <Input {...field} placeholder="Orders" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          </FormSection>
          <FormField
            control={form.control}
            name="channelId"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Channel</FormLabel>
                <Select onValueChange={field.onChange} value={field.value as string}>
                  <FormControl>
                    <SelectTrigger>
                      <SelectValue placeholder="Select channel…" />
                    </SelectTrigger>
                  </FormControl>
                  <SelectContent>
                    {(channels ?? []).map((ch) => (
                      <SelectItem key={ch.channelId} value={ch.channelId}>
                        {ch.channelId}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormSection title="Sync Operations">
            <FormField
              control={form.control}
              name="syncOnInsert"
              render={({ field }) => (
                <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                  <FormControl>
                    <Checkbox checked={field.value as boolean} onCheckedChange={field.onChange} />
                  </FormControl>
                  <FormLabel>Sync on Insert</FormLabel>
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="syncOnUpdate"
              render={({ field }) => (
                <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                  <FormControl>
                    <Checkbox checked={field.value as boolean} onCheckedChange={field.onChange} />
                  </FormControl>
                  <FormLabel>Sync on Update</FormLabel>
                </FormItem>
              )}
            />
            <FormField
              control={form.control}
              name="syncOnDelete"
              render={({ field }) => (
                <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                  <FormControl>
                    <Checkbox checked={field.value as boolean} onCheckedChange={field.onChange} />
                  </FormControl>
                  <FormLabel>Sync on Delete</FormLabel>
                </FormItem>
              )}
            />
            {/* Show refine error on syncOnInsert field */}
            <FormMessage>{form.formState.errors.syncOnInsert?.message}</FormMessage>
          </FormSection>
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel={mode === 'create' ? 'Create Trigger' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
```

- [ ] **Step 6: Extend makeTriggersColumns factory**

Open `src/MSOSync.Frontend/src/features/triggers/columns.ts`. Add `onEdit` and `onDelete` params to the factory and add items to the existing ActionMenu. The existing `ConfirmableAction` type and existing menu items (Enable/Disable/Rebuild/Verify) must remain completely unchanged.

```ts
import { useCallback, useMemo } from 'react';
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { TriggerDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';
```

Read the current `src/MSOSync.Frontend/src/features/triggers/columns.ts` first to see its full content, then update the factory signature from:
```ts
export function makeTriggersColumns(
  onAction: (triggerId: string, action: ConfirmableAction) => void,
  onVerify: (triggerId: string) => void,
): ColDef<TriggerDto>[]
```
to:
```ts
export function makeTriggersColumns(
  onAction: (triggerId: string, action: ConfirmableAction) => void,
  onVerify: (triggerId: string) => void,
  onEdit: (trigger: TriggerDto) => void,
  onDelete: (trigger: TriggerDto) => void,
): ColDef<TriggerDto>[]
```

And in the ActionMenu items array, append after the existing items:
```ts
{ label: 'Edit', onClick: () => onEdit(trigger) },
{ label: 'Delete', onClick: () => onDelete(trigger), variant: 'destructive' },
```

**Important:** Read the actual current file before editing — you need the exact content to do a precise diff edit. Do NOT remove or reorder any existing columns or menu items.

- [ ] **Step 7: Update TriggersGrid**

Open `src/MSOSync.Frontend/src/features/triggers/TriggersGrid.tsx`. Full file after edit:

```tsx
import { useCallback, useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeTriggersColumns } from './columns';
import { useTriggers } from './hooks';
import { useVerifyTriggerMutation } from './mutations';
import type { TriggerDto } from '../../shared/types';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

interface Props {
  quickFilterText?: string;
  onAction: (triggerId: string, action: ConfirmableAction) => void;
  onEdit: (trigger: TriggerDto) => void;
  onDelete: (trigger: TriggerDto) => void;
}

export function TriggersGrid({ quickFilterText, onAction, onEdit, onDelete }: Props) {
  const { data, isLoading, error, refetch } = useTriggers();
  const verifyMutation = useVerifyTriggerMutation();

  const onVerify = useCallback(
    (triggerId: string) => { void verifyMutation.mutateAsync(triggerId); },
    [verifyMutation],
  );

  const columns = useMemo(
    () => makeTriggersColumns(onAction, onVerify, onEdit, onDelete),
    [onAction, onVerify, onEdit, onDelete],
  );

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

- [ ] **Step 8: Update TriggersPage**

Open `src/MSOSync.Frontend/src/features/triggers/TriggersPage.tsx`. Full file after edit (preserve existing `confirmState` logic for 10C enable/disable/rebuild):

```tsx
import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { TriggersGrid } from './TriggersGrid';
import { TriggerDialog } from './TriggerDialog';
import {
  useEnableTriggerMutation,
  useDisableTriggerMutation,
  useRebuildTriggerMutation,
  useDeleteTriggerMutation,
} from './mutations';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { TriggerDto } from '../../shared/types';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

interface ConfirmState {
  triggerId: string;
  action: ConfirmableAction;
}

const CONFIRM_CONFIG: Record<
  ConfirmableAction,
  { title: string; description: (triggerId: string) => string; confirmLabel: string; variant: 'default' | 'destructive' }
> = {
  enable: {
    title: 'Enable Trigger',
    description: (id) => `Enable trigger "${id}"?`,
    confirmLabel: 'Enable',
    variant: 'default',
  },
  disable: {
    title: 'Disable Trigger',
    description: (id) => `Disable trigger "${id}"?`,
    confirmLabel: 'Disable',
    variant: 'destructive',
  },
  rebuild: {
    title: 'Rebuild Trigger',
    description: () => 'This will drop and recreate the database trigger. Are you sure?',
    confirmLabel: 'Rebuild',
    variant: 'destructive',
  },
};

export function TriggersPage() {
  const [search, setSearch] = useState('');
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<TriggerDto | null>(null);
  const [deleteState, setDeleteState] = useState<TriggerDto | null>(null);

  const enableMutation = useEnableTriggerMutation();
  const disableMutation = useDisableTriggerMutation();
  const rebuildMutation = useRebuildTriggerMutation();
  const deleteMutation = useDeleteTriggerMutation();

  const onAction = useCallback((triggerId: string, action: ConfirmableAction) => {
    setConfirmState({ triggerId, action });
  }, []);

  const onEdit = useCallback((trigger: TriggerDto) => { setEditState(trigger); }, []);
  const onDelete = useCallback((trigger: TriggerDto) => { setDeleteState(trigger); }, []);

  const isPending =
    enableMutation.isPending || disableMutation.isPending || rebuildMutation.isPending;

  const handleConfirm = async () => {
    if (!confirmState) return;
    const { triggerId, action } = confirmState;
    try {
      if (action === 'enable') await enableMutation.mutateAsync(triggerId);
      else if (action === 'disable') await disableMutation.mutateAsync(triggerId);
      else await rebuildMutation.mutateAsync(triggerId);
    } finally {
      setConfirmState(null);
    }
  };

  const handleDeleteConfirm = async () => {
    if (!deleteState) return;
    try {
      await deleteMutation.mutateAsync(deleteState.triggerId);
      toast.success(`Trigger "${deleteState.triggerId}" deleted`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeleteState(null);
    }
  };

  const config = confirmState ? CONFIRM_CONFIG[confirmState.action] : null;

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Triggers</h1>
        <Button onClick={() => setCreateOpen(true)}>Add Trigger</Button>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search triggers…"
        className="max-w-xs"
      />
      <TriggersGrid
        quickFilterText={search}
        onAction={onAction}
        onEdit={onEdit}
        onDelete={onDelete}
      />
      <TriggerDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <TriggerDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {config && confirmState && (
        <ConfirmDialog
          open
          title={config.title}
          description={config.description(confirmState.triggerId)}
          confirmLabel={config.confirmLabel}
          variant={config.variant}
          loading={isPending}
          onConfirm={() => void handleConfirm()}
          onOpenChange={(open) => { if (!open) setConfirmState(null); }}
        />
      )}
      {deleteState && (
        <ConfirmDialog
          open
          title="Delete Trigger"
          description={`Delete trigger "${deleteState.triggerId}"? This will drop the database trigger. This cannot be undone.`}
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

- [ ] **Step 9: Verify build clean**

Run from `src/MSOSync.Frontend/`:
```bash
npm run build
```
Expected: exits 0, no TypeScript errors.

- [ ] **Step 10: Commit**

```bash
git add src/MSOSync.Frontend/src/features/triggers/utils.ts
git add src/MSOSync.Frontend/src/features/triggers/schemas.ts
git add src/MSOSync.Frontend/src/features/triggers/TriggerDialog.tsx
git add src/MSOSync.Frontend/src/shared/api/triggers.ts
git add src/MSOSync.Frontend/src/features/triggers/mutations.ts
git add src/MSOSync.Frontend/src/features/triggers/columns.ts
git add src/MSOSync.Frontend/src/features/triggers/TriggersGrid.tsx
git add src/MSOSync.Frontend/src/features/triggers/TriggersPage.tsx
git commit -m "feat(10d): add triggers create/edit/delete forms (extends 10C actions)"
```
