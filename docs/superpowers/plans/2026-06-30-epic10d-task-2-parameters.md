# Epic 10D — Task 2: Parameters Edit Form

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an Edit form for Parameters. No create or delete (parameters are system config). Edit replaces the masked value with a new one.

**Architecture:** `ParameterDialog` is edit-only; single field `value`; `type="password"` when `isSecret`. Columns factory adds an Edit action. Page manages `editState: ParameterRow | null`.

**Prerequisite:** Task 1 complete (shared form components + shadcn Dialog/Form/Input installed).

## Global Constraints

- TypeScript strict; no `any`; relative imports only
- `mutateAsync()` everywhere — never `mutate()`
- No new Vitest tests — `npm run build` exits 0 = done
- Mutation hook has NO `onError` — dialog owns error display

---

## Interfaces

**Consumes from Task 1:**
```tsx
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
```

**Existing types:**
```ts
// src/shared/types/parameters.ts
interface ParameterDto {
  name: string;
  value: string;
  isSecret: boolean;
  updatedTime?: string;
}
interface ParameterDescriptorDto {
  name: string;
  description: string;
  isSecret: boolean;
  requiresRestart: boolean;
  isDynamic: boolean;
}

// src/features/parameters/columns.ts — existing
interface ParameterRow extends ParameterDto {
  descriptor?: ParameterDescriptorDto;
}
```

**Existing API (src/shared/api/parameters.ts):**
```ts
export async function getParameters(): Promise<ParameterDto[]> { ... }
export async function getParameterDescriptors(): Promise<ParameterDescriptorDto[]> { ... }
```

**Existing hooks (src/features/parameters/hooks.ts):**
```ts
export function useParameters() { ... }
export function useParameterDescriptors() { ... }
```

**Existing queryKeys:**
```ts
queryKeys.parameters() // → ['parameters']
queryKeys.parameterDescriptors() // → ['parameter-descriptors']
```

**Produces (for later tasks to reference):**
- API: `updateParameter(name: string, value: string): Promise<void>`
- Hook: `useUpdateParameterMutation()` — `mutationFn: ({ name, value }) => updateParameter(name, value)`
- Dialog: `ParameterDialog` — props `{ open, initialValues: ParameterRow, onOpenChange }`
- Schema type: `UpdateParameterForm = { value: string }`

---

## Files

**Modify:**
- `src/MSOSync.Frontend/src/shared/api/parameters.ts` — add `updateParameter`
- `src/MSOSync.Frontend/src/features/parameters/columns.ts` — convert to factory

**Create:**
- `src/MSOSync.Frontend/src/features/parameters/schemas.ts`
- `src/MSOSync.Frontend/src/features/parameters/mutations.ts`
- `src/MSOSync.Frontend/src/features/parameters/ParameterDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/features/parameters/ParametersGrid.tsx` — add `onEdit` prop
- `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx` — add edit state + dialog

---

## Steps

- [ ] **Step 1: Add updateParameter to shared API**

Open `src/MSOSync.Frontend/src/shared/api/parameters.ts`. Full file after edit:

```ts
import client from './client';
import type { ParameterDto, ParameterDescriptorDto } from '../types';

export async function getParameters(): Promise<ParameterDto[]> {
  const { data } = await client.get<ParameterDto[]>('/parameters');
  return data;
}

export async function getParameterDescriptors(): Promise<ParameterDescriptorDto[]> {
  const { data } = await client.get<ParameterDescriptorDto[]>('/parameters/descriptors');
  return data;
}

export async function updateParameter(name: string, value: string): Promise<void> {
  await client.put(`/parameters/${encodeURIComponent(name)}`, { value });
}
```

- [ ] **Step 2: Create schemas.ts**

Create `src/MSOSync.Frontend/src/features/parameters/schemas.ts`:

```ts
import { z } from 'zod';
import type { ParameterRow } from './columns';

export const updateParameterSchema = z.object({
  value: z.string().trim().min(1, 'Value is required'),
});
export type UpdateParameterForm = z.infer<typeof updateParameterSchema>;

export function getDefaultValues(_initialValues?: ParameterRow): UpdateParameterForm {
  // Always start empty — never prefill secret values
  return { value: '' };
}
```

- [ ] **Step 3: Create mutations.ts**

Create `src/MSOSync.Frontend/src/features/parameters/mutations.ts`:

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { updateParameter } from '../../shared/api/parameters';
import { queryKeys } from '../../shared/queryKeys';

export function useUpdateParameterMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      updateParameter(name, value),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.parameters() });
    },
    // no onError — dialog handles it
  });
}
```

- [ ] **Step 4: Create ParameterDialog.tsx**

Create `src/MSOSync.Frontend/src/features/parameters/ParameterDialog.tsx`:

```tsx
import { useEffect, useState } from 'react';
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
import { updateParameterSchema, getDefaultValues } from './schemas';
import type { UpdateParameterForm } from './schemas';
import { useUpdateParameterMutation } from './mutations';
import type { ParameterRow } from './columns';

interface ParameterDialogProps {
  open: boolean;
  initialValues: ParameterRow;
  onOpenChange: (open: boolean) => void;
}

export function ParameterDialog({ open, initialValues, onOpenChange }: ParameterDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const mutation = useUpdateParameterMutation();

  const form = useForm<UpdateParameterForm>({
    resolver: zodResolver(updateParameterSchema),
    defaultValues: getDefaultValues(initialValues),
  });

  useEffect(() => {
    if (open) {
      form.reset(getDefaultValues(initialValues));
      setApiError(null);
    } else {
      form.reset();
      setApiError(null);
    }
  }, [open, initialValues, form]);

  const onSubmit = async (values: UpdateParameterForm) => {
    setApiError(null);
    try {
      await mutation.mutateAsync({ name: initialValues.name, value: values.value });
      toast.success('Parameter updated');
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  return (
    <EntityDialog
      open={open}
      title={`Edit Parameter: ${initialValues.name}`}
      description={initialValues.descriptor?.description}
      onOpenChange={onOpenChange}
    >
      <Form {...form}>
        <form onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }} className="flex flex-col gap-4">
          <FormField
            control={form.control}
            name="value"
            render={({ field }) => (
              <FormItem>
                <FormLabel>Value</FormLabel>
                <FormControl>
                  <Input
                    {...field}
                    type={initialValues.isSecret ? 'password' : 'text'}
                    autoComplete="off"
                    placeholder={initialValues.isSecret ? 'Enter new secret value' : 'Enter value'}
                  />
                </FormControl>
                <FormMessage />
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel="Update"
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
```

- [ ] **Step 5: Convert parameterColumns to factory**

Open `src/MSOSync.Frontend/src/features/parameters/columns.ts`. Full file after edit:

```ts
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { ParameterDto, ParameterDescriptorDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { ActionMenu } from '../../shared/components/actions';

export interface ParameterRow extends ParameterDto {
  descriptor?: ParameterDescriptorDto;
}

export function makeParameterColumns(
  onEdit: (row: ParameterRow) => void,
): ColDef<ParameterRow>[] {
  return [
    { field: 'name', headerName: 'Name', width: 220 },
    {
      field: 'value',
      headerName: 'Value',
      flex: 1,
      minWidth: 200,
      valueFormatter: (p) => {
        const row = p.data as ParameterRow | undefined;
        return row?.isSecret ? '••••••••' : (p.value as string ?? '');
      },
    },
    {
      headerName: 'Description',
      flex: 1,
      minWidth: 200,
      valueGetter: (p) => p.data?.descriptor?.description ?? '—',
    },
    {
      field: 'isSecret',
      headerName: 'Secret',
      width: 90,
      valueFormatter: (p) => (p.value ? 'Yes' : 'No'),
    },
    {
      headerName: 'Restart Req.',
      width: 120,
      valueGetter: (p) => (p.data?.descriptor?.requiresRestart ? 'Yes' : 'No'),
    },
    {
      headerName: 'Dynamic',
      width: 100,
      valueGetter: (p) => (p.data?.descriptor?.isDynamic ? 'Yes' : 'No'),
    },
    {
      field: 'updatedTime',
      headerName: 'Updated',
      width: 165,
      valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
    },
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<ParameterRow>) => {
        if (!p.data) return null;
        const row = p.data;
        return ActionMenu({
          items: [{ label: 'Edit', onClick: () => onEdit(row) }],
        });
      },
    },
  ];
}
```

- [ ] **Step 6: Update ParametersGrid**

Open `src/MSOSync.Frontend/src/features/parameters/ParametersGrid.tsx`. Full file after edit:

```tsx
import { useMemo, useCallback } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeParameterColumns } from './columns';
import type { ParameterRow } from './columns';
import { useParameters, useParameterDescriptors } from './hooks';

interface Props {
  quickFilterText?: string;
  onEdit: (row: ParameterRow) => void;
}

export function ParametersGrid({ quickFilterText, onEdit }: Props) {
  const { data: params, isLoading: paramsLoading, error: paramsError, refetch: refetchParams } = useParameters();
  const { data: descriptors } = useParameterDescriptors();

  const rows: ParameterRow[] | undefined = useMemo(() => {
    if (!params) return undefined;
    const descriptorMap = new Map((descriptors ?? []).map((d) => [d.name, d]));
    return params.map((p) => ({ ...p, descriptor: descriptorMap.get(p.name) }));
  }, [params, descriptors]);

  const columns = useMemo(() => makeParameterColumns(onEdit), [onEdit]);

  return (
    <DataGrid
      rowData={rows}
      columnDefs={columns}
      loading={paramsLoading}
      error={paramsError}
      onRetry={() => void refetchParams()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
```

- [ ] **Step 7: Update ParametersPage**

Open `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx`. Full file after edit:

```tsx
import { useState, useCallback } from 'react';
import { ParametersGrid } from './ParametersGrid';
import { ParameterDialog } from './ParameterDialog';
import type { ParameterRow } from './columns';

export function ParametersPage() {
  const [editState, setEditState] = useState<ParameterRow | null>(null);

  const onEdit = useCallback((row: ParameterRow) => {
    setEditState(row);
  }, []);

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Parameters</h1>
      <ParametersGrid onEdit={onEdit} />
      {editState && (
        <ParameterDialog
          open={!!editState}
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
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
git add src/MSOSync.Frontend/src/shared/api/parameters.ts
git add src/MSOSync.Frontend/src/features/parameters/schemas.ts
git add src/MSOSync.Frontend/src/features/parameters/mutations.ts
git add src/MSOSync.Frontend/src/features/parameters/ParameterDialog.tsx
git add src/MSOSync.Frontend/src/features/parameters/columns.ts
git add src/MSOSync.Frontend/src/features/parameters/ParametersGrid.tsx
git add src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx
git commit -m "feat(10d): add parameter edit dialog"
```
