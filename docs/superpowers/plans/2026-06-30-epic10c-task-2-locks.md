# Epic 10C — Task 2: Locks Actions

> Master plan: `docs/superpowers/plans/2026-06-30-epic10c-operational-actions.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10c-operational-actions-design.md`
> Depends on: Task 1 (shared action components must exist)

**Goal:** Add "Release" action to each row on the Locks page. Clicking releases the lock after confirmation.

**Files to create/modify** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/locks/mutations.ts`

Modify:
- `shared/api/locks.ts` — add `releaseLock`
- `features/locks/columns.ts` — convert const to factory function
- `features/locks/LocksGrid.tsx` — add confirm state + mutation + ConfirmDialog
- `features/locks/LocksPage.tsx` — remove placeholder paragraph

**Interfaces — Consumes (from Task 1):**
- `shared/utils/error.ts` → `getErrorMessage(error: unknown): string`
- `shared/components/actions` → `ActionButton`, `ConfirmDialog`

**Existing file state to be aware of:**

`shared/api/locks.ts` currently:
```typescript
import client from './client';
import type { LockDto } from '../types';
export async function getLocks(): Promise<LockDto[]> {
  const { data } = await client.get<LockDto[]>('/locks');
  return data;
}
```

`features/locks/columns.ts` currently exports `const lockColumns: ColDef<LockDto>[]` with 4 columns (lockName, lockOwner, lockTime, Duration valueGetter). This will become a factory function.

`features/locks/LocksGrid.tsx` currently:
```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { lockColumns } from './columns';
import { useLocks } from './hooks';
export function LocksGrid() {
  const { data, isLoading, error, refetch } = useLocks();
  return (
    <DataGrid rowData={data} columnDefs={lockColumns} loading={isLoading}
      error={error} onRetry={() => void refetch()} height={500} />
  );
}
```

`features/locks/LocksPage.tsx` currently has a placeholder `<p>` tag to remove.

---

- [ ] **Step 1: Add `releaseLock` to `shared/api/locks.ts`**

```typescript
import client from './client';
import type { LockDto } from '../types';

export async function getLocks(): Promise<LockDto[]> {
  const { data } = await client.get<LockDto[]>('/locks');
  return data;
}

export async function releaseLock(lockName: string): Promise<void> {
  await client.delete(`/locks/${encodeURIComponent(lockName)}`);
}
```

- [ ] **Step 2: Create `features/locks/mutations.ts`**

```typescript
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import { releaseLock } from '../../shared/api/locks';
import { getErrorMessage } from '../../shared/utils/error';
import { queryKeys } from '../../shared/queryKeys';

export function useReleaseLockMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (lockName: string) => releaseLock(lockName),
    onSuccess: () => {
      toast.success('Lock released');
      void queryClient.invalidateQueries({ queryKey: queryKeys.locks() });
    },
    onError: (error) => {
      toast.error(getErrorMessage(error));
    },
  });
}
```

- [ ] **Step 3: Rewrite `features/locks/columns.ts` as factory**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { LockDto } from '../../shared/types';
import { formatRelativeTime } from '../../shared/utils/date';
import { ActionButton } from '../../shared/components/actions';

export function makeLocksColumns(onRelease: (lockName: string) => void): ColDef<LockDto>[] {
  return [
    { field: 'lockName', headerName: 'Lock Name', flex: 1, minWidth: 180 },
    { field: 'lockOwner', headerName: 'Owner', width: 200 },
    {
      field: 'lockTime',
      headerName: 'Held Since',
      width: 160,
      valueFormatter: (p) => formatRelativeTime(p.value as string),
    },
    {
      headerName: 'Duration',
      width: 140,
      valueGetter: (p) => {
        if (!p.data?.lockTime) return '';
        const diffMs = Date.now() - new Date(p.data.lockTime).getTime();
        const diffSec = Math.round(diffMs / 1000);
        if (diffSec < 60) return `${diffSec}s`;
        const diffMin = Math.round(diffSec / 60);
        if (diffMin < 60) return `${diffMin}m`;
        return `${Math.round(diffMin / 60)}h`;
      },
    },
    {
      headerName: 'Actions',
      width: 110,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<LockDto>) => {
        if (!p.data) return null;
        return ActionButton({
          label: 'Release',
          onClick: () => onRelease(p.data!.lockName),
          variant: 'destructive',
        });
      },
    },
  ];
}
```

- [ ] **Step 4: Rewrite `features/locks/LocksGrid.tsx`**

```tsx
import { useState, useCallback, useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { ConfirmDialog } from '../../shared/components/actions';
import { makeLocksColumns } from './columns';
import { useReleaseLockMutation } from './mutations';
import { useLocks } from './hooks';

export function LocksGrid() {
  const { data, isLoading, error, refetch } = useLocks();
  const releaseMutation = useReleaseLockMutation();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [pendingLockName, setPendingLockName] = useState<string | null>(null);

  const openConfirm = useCallback((lockName: string) => {
    setPendingLockName(lockName);
    setConfirmOpen(true);
  }, []);

  const columns = useMemo(() => makeLocksColumns(openConfirm), [openConfirm]);

  const handleConfirm = async () => {
    if (!pendingLockName) return;
    await releaseMutation.mutateAsync(pendingLockName);
    setConfirmOpen(false);
    setPendingLockName(null);
  };

  return (
    <>
      <DataGrid
        rowData={data}
        columnDefs={columns}
        loading={isLoading}
        error={error}
        onRetry={() => void refetch()}
        height={500}
      />
      <ConfirmDialog
        open={confirmOpen}
        title="Release Lock"
        description={`Release lock "${pendingLockName ?? ''}"? This may affect active processes.`}
        confirmLabel="Release"
        variant="destructive"
        loading={releaseMutation.isPending}
        onConfirm={() => void handleConfirm()}
        onOpenChange={(open) => {
          if (!open) setPendingLockName(null);
          setConfirmOpen(open);
        }}
      />
    </>
  );
}
```

- [ ] **Step 5: Rewrite `features/locks/LocksPage.tsx` — remove placeholder**

```tsx
import { LocksGrid } from './LocksGrid';

export function LocksPage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Locks</h1>
      <LocksGrid />
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
git add src/MSOSync.Frontend/src/shared/api/locks.ts
git add src/MSOSync.Frontend/src/features/locks/mutations.ts
git add src/MSOSync.Frontend/src/features/locks/columns.ts
git add src/MSOSync.Frontend/src/features/locks/LocksGrid.tsx
git add src/MSOSync.Frontend/src/features/locks/LocksPage.tsx
git commit -m "feat(10c): add release lock action"
```

---

## Report Contract

Write report to the path given by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files modified (count)
- Build result
- Test result (N/12 pass)
- Any concerns
