# Epic 10C — Task 4: Triggers Actions

> Master plan: `docs/superpowers/plans/2026-06-30-epic10c-operational-actions.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10c-operational-actions-design.md`
> Depends on: Task 1 (shared action components must exist)

**Goal:** Add Enable, Disable, Rebuild, Verify actions to each row on the Triggers page. Enable/Disable/Rebuild show a confirm dialog. Verify fires immediately and shows an info toast.

**Files to create/modify** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/triggers/mutations.ts`

Modify:
- `shared/api/triggers.ts` — add 4 mutation functions
- `features/triggers/columns.ts` — convert const to factory function
- `features/triggers/TriggersGrid.tsx` — accept `onAction` prop + own verify mutation
- `features/triggers/TriggersPage.tsx` — add confirm state + 3 mutations + ConfirmDialog

**Interfaces — Consumes (from Task 1):**
- `shared/utils/error.ts` → `getErrorMessage(error: unknown): string`
- `shared/components/actions` → `ActionMenu`, `ConfirmDialog`

**Key types:**
- `TriggerDto` has: `triggerId: string`, `channelId: string`, `tableName: string`, `schemaName: string`, `captureInsert: boolean`, `captureUpdate: boolean`, `captureDelete: boolean`, `enabled: boolean`, `createdTime: string`

**Verify is special:** fires on click with no confirm dialog. `useVerifyTriggerMutation` lives in `TriggersGrid` (not TriggersPage) so it doesn't interfere with confirm state management. On success: `toast.info('Trigger verified successfully.')`. On error: `toast.error(getErrorMessage(error))`.

**Existing `shared/api/triggers.ts`:**
```typescript
import client from './client';
import type { TriggerDto } from '../types';
export async function getTriggers(): Promise<TriggerDto[]> {
  const { data } = await client.get<TriggerDto[]>('/triggers');
  return data;
}
```

**Existing `features/triggers/columns.ts`** exports `const triggerColumns: ColDef<TriggerDto>[]` with 9 columns including StatusBadge for `enabled`. Will be replaced by factory.

**Existing `features/triggers/TriggersGrid.tsx`:**
```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { triggerColumns } from './columns';
import { useTriggers } from './hooks';
interface Props { quickFilterText?: string; }
export function TriggersGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useTriggers();
  return (
    <DataGrid rowData={data} columnDefs={triggerColumns} loading={isLoading}
      error={error} onRetry={() => void refetch()} quickFilterText={quickFilterText} height={500} />
  );
}
```

**Existing `features/triggers/TriggersPage.tsx`:**
```tsx
import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { TriggersGrid } from './TriggersGrid';
export function TriggersPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Triggers</h1>
      <Input value={search} onChange={(e) => setSearch(e.target.value)}
        placeholder="Search triggers…" className="max-w-xs" />
      <TriggersGrid quickFilterText={search} />
    </div>
  );
}
```

---

- [ ] **Step 1: Add mutation functions to `shared/api/triggers.ts`**

```typescript
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
```

- [ ] **Step 2: Create `features/triggers/mutations.ts`**

```typescript
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import {
  enableTrigger,
  disableTrigger,
  rebuildTrigger,
  verifyTrigger,
} from '../../shared/api/triggers';
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
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
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
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
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
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}

export function useVerifyTriggerMutation() {
  return useMutation({
    mutationFn: (triggerId: string) => verifyTrigger(triggerId),
    onSuccess: () => {
      toast.info('Trigger verified successfully.');
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}
```

- [ ] **Step 3: Rewrite `features/triggers/columns.ts` as factory**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { TriggerDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

export function makeTriggersColumns(
  onAction: (triggerId: string, action: ConfirmableAction) => void,
  onVerify: (triggerId: string) => void,
): ColDef<TriggerDto>[] {
  return [
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
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<TriggerDto>) => {
        if (!p.data) return null;
        const { triggerId } = p.data;
        return ActionMenu({
          items: [
            { label: 'Enable', onClick: () => onAction(triggerId, 'enable') },
            {
              label: 'Disable',
              onClick: () => onAction(triggerId, 'disable'),
              variant: 'destructive',
            },
            {
              label: 'Rebuild',
              onClick: () => onAction(triggerId, 'rebuild'),
              variant: 'destructive',
            },
            { label: 'Verify', onClick: () => onVerify(triggerId) },
          ],
        });
      },
    },
  ];
}
```

- [ ] **Step 4: Rewrite `features/triggers/TriggersGrid.tsx`**

`useVerifyTriggerMutation` is managed here (not in TriggersPage) because verify fires immediately without a confirm dialog — no state coordination needed with the page.

```tsx
import { useCallback, useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeTriggersColumns } from './columns';
import { useTriggers } from './hooks';
import { useVerifyTriggerMutation } from './mutations';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

interface Props {
  quickFilterText?: string;
  onAction: (triggerId: string, action: ConfirmableAction) => void;
}

export function TriggersGrid({ quickFilterText, onAction }: Props) {
  const { data, isLoading, error, refetch } = useTriggers();
  const verifyMutation = useVerifyTriggerMutation();

  const onVerify = useCallback(
    (triggerId: string) => {
      void verifyMutation.mutateAsync(triggerId);
    },
    [verifyMutation],
  );

  const columns = useMemo(
    () => makeTriggersColumns(onAction, onVerify),
    [onAction, onVerify],
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

- [ ] **Step 5: Rewrite `features/triggers/TriggersPage.tsx`**

```tsx
import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { ConfirmDialog } from '../../shared/components/actions';
import { TriggersGrid } from './TriggersGrid';
import {
  useEnableTriggerMutation,
  useDisableTriggerMutation,
  useRebuildTriggerMutation,
} from './mutations';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

interface ConfirmState {
  triggerId: string;
  action: ConfirmableAction;
}

const CONFIRM_CONFIG: Record<
  ConfirmableAction,
  {
    title: string;
    description: (triggerId: string) => string;
    confirmLabel: string;
    variant: 'default' | 'destructive';
  }
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

  const enableMutation = useEnableTriggerMutation();
  const disableMutation = useDisableTriggerMutation();
  const rebuildMutation = useRebuildTriggerMutation();

  const onAction = useCallback((triggerId: string, action: ConfirmableAction) => {
    setConfirmState({ triggerId, action });
  }, []);

  const isPending =
    enableMutation.isPending || disableMutation.isPending || rebuildMutation.isPending;

  const handleConfirm = async () => {
    if (!confirmState) return;
    const { triggerId, action } = confirmState;
    if (action === 'enable') await enableMutation.mutateAsync(triggerId);
    else if (action === 'disable') await disableMutation.mutateAsync(triggerId);
    else await rebuildMutation.mutateAsync(triggerId);
    setConfirmState(null);
  };

  const config = confirmState ? CONFIRM_CONFIG[confirmState.action] : null;

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Triggers</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search triggers…"
        className="max-w-xs"
      />
      <TriggersGrid quickFilterText={search} onAction={onAction} />
      {config && confirmState && (
        <ConfirmDialog
          open
          title={config.title}
          description={config.description(confirmState.triggerId)}
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
git add src/MSOSync.Frontend/src/shared/api/triggers.ts
git add src/MSOSync.Frontend/src/features/triggers/mutations.ts
git add src/MSOSync.Frontend/src/features/triggers/columns.ts
git add src/MSOSync.Frontend/src/features/triggers/TriggersGrid.tsx
git add src/MSOSync.Frontend/src/features/triggers/TriggersPage.tsx
git commit -m "feat(10c): add enable/disable/rebuild/verify actions to triggers"
```

---

## Report Contract

Write report to the path given by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files modified (count)
- Build result
- Test result (N/12 pass)
- Any concerns
