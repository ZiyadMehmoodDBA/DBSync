# Epic 10D — Task 3: Users CRUD Forms

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Create, Edit, and Deactivate (soft-delete) forms for Users.

**Architecture:** Single `UserDialog` with `mode: 'create' | 'edit'`. Deactivate goes through `ConfirmDialog` (no form). Self-deactivation guard: hide/disable Deactivate for the currently authenticated user (compare `auth.user?.username` with row `username`).

**Prerequisite:** Task 1 complete (shared form components installed).

## Global Constraints

- TypeScript strict; no `any`; relative imports only
- `mutateAsync()` everywhere — never `mutate()`
- No new Vitest tests — `npm run build` exits 0 = done
- Mutation hooks have NO `onError` — dialog/page owns error display
- Self-deactivation guard: `auth.user?.username === row.username` (AuthState has `user.username`, not userId)

---

## Interfaces

**Consumes from Task 1:**
```tsx
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
```

**Existing types:**
```ts
// src/shared/types/users.ts
interface UserSummaryDto {
  userId: number;
  username: string;
  enabled: boolean;
  roles: string[];
  createdTime: string;
  lastLoginTime?: string;
}

// src/shared/types/auth.ts
interface UserProfile {
  username: string;
  roles: string[];
  expiresAt: string;
}
interface AuthState {
  accessToken: string | null;
  user: UserProfile | null;
  // ...
}
```

**Existing API (src/shared/api/users.ts):**
```ts
export async function getUsers(filter: UserFilter): Promise<PagedResult<UserSummaryDto>> { ... }
```

**useAuth hook:**
```ts
// src/features/auth/useAuth.ts
export function useAuth(): AuthState { ... }
```

**queryKeys:**
```ts
queryKeys.users(filter)      // → ['users', filter] — needs filter arg
queryKeys.dashboardSummary() // → ['dashboard-summary']
// For cache invalidation, use base key: ['users'] to match all user queries
```

**Produces:**
- API: `createUser(data)`, `updateUser(userId, data)`, `deactivateUser(userId)`
- Hooks: `useCreateUserMutation()`, `useUpdateUserMutation()`, `useDeactivateUserMutation()`
- Dialog: `UserDialog` — props `{ open, mode, initialValues?, onOpenChange }`
- Schema types: `CreateUserForm`, `UpdateUserForm`

---

## Files

**Modify:**
- `src/MSOSync.Frontend/src/shared/api/users.ts` — add 3 API functions

**Create:**
- `src/MSOSync.Frontend/src/features/users/schemas.ts`
- `src/MSOSync.Frontend/src/features/users/mutations.ts`
- `src/MSOSync.Frontend/src/features/users/UserDialog.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/features/users/columns.ts` — convert to factory
- `src/MSOSync.Frontend/src/features/users/UsersGrid.tsx` — add props
- `src/MSOSync.Frontend/src/features/users/UsersPage.tsx` — add state + dialogs

---

## Steps

- [ ] **Step 1: Add user API functions**

Open `src/MSOSync.Frontend/src/shared/api/users.ts`. Full file after edit:

```ts
import client from './client';
import type { UserSummaryDto, UserFilter } from '../types';
import type { PagedResult } from '../types/common';

export async function getUsers(filter: UserFilter): Promise<PagedResult<UserSummaryDto>> {
  const { data } = await client.get<PagedResult<UserSummaryDto>>('/users', {
    params: filter,
  });
  return data;
}

export interface CreateUserRequest {
  username: string;
  password: string;
  enabled: boolean;
}

export interface UpdateUserRequest {
  enabled: boolean;
  newPassword?: string;
}

export async function createUser(data: CreateUserRequest): Promise<void> {
  await client.post('/users', data);
}

export async function updateUser(userId: number, data: UpdateUserRequest): Promise<void> {
  await client.put(`/users/${userId}`, data);
}

export async function deactivateUser(userId: number): Promise<void> {
  await client.delete(`/users/${userId}`);
}
```

- [ ] **Step 2: Create schemas.ts**

Create `src/MSOSync.Frontend/src/features/users/schemas.ts`:

```ts
import { z } from 'zod';
import type { UserSummaryDto } from '../../shared/types';

export const createUserSchema = z.object({
  username: z.string().trim().min(3, 'Username must be at least 3 characters').max(100),
  password: z.string().min(8, 'Password must be at least 8 characters'),
  enabled: z.boolean(),
});
export type CreateUserForm = z.infer<typeof createUserSchema>;

export const updateUserSchema = z.object({
  enabled: z.boolean(),
  newPassword: z.string().min(8, 'Password must be at least 8 characters').optional().or(z.literal('')),
});
export type UpdateUserForm = z.infer<typeof updateUserSchema>;

export function getDefaultValues(
  initialValues?: UserSummaryDto,
  mode?: 'create' | 'edit',
): CreateUserForm | UpdateUserForm {
  if (mode === 'edit' && initialValues) {
    return { enabled: initialValues.enabled, newPassword: '' };
  }
  return { username: '', password: '', enabled: true };
}
```

- [ ] **Step 3: Create mutations.ts**

Create `src/MSOSync.Frontend/src/features/users/mutations.ts`:

```ts
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { createUser, updateUser, deactivateUser } from '../../shared/api/users';
import type { CreateUserRequest, UpdateUserRequest } from '../../shared/api/users';
import { queryKeys } from '../../shared/queryKeys';

function invalidateUsers(queryClient: ReturnType<typeof useQueryClient>) {
  void queryClient.invalidateQueries({ queryKey: ['users'] });
  void queryClient.invalidateQueries({ queryKey: queryKeys.dashboardSummary() });
}

export function useCreateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (data: CreateUserRequest) => createUser(data),
    onSuccess: () => { invalidateUsers(queryClient); },
    // no onError — caller handles it
  });
}

export function useUpdateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ userId, data }: { userId: number; data: UpdateUserRequest }) =>
      updateUser(userId, data),
    onSuccess: () => { invalidateUsers(queryClient); },
    // no onError — caller handles it
  });
}

export function useDeactivateUserMutation() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (userId: number) => deactivateUser(userId),
    onSuccess: () => { invalidateUsers(queryClient); },
    // no onError — caller handles it
  });
}
```

- [ ] **Step 4: Create UserDialog.tsx**

Create `src/MSOSync.Frontend/src/features/users/UserDialog.tsx`:

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
import { EntityDialog, FormActions, FormError } from '../../shared/components/forms';
import { getErrorMessage } from '../../shared/utils/error';
import { createUserSchema, updateUserSchema, getDefaultValues } from './schemas';
import type { CreateUserForm, UpdateUserForm } from './schemas';
import { useCreateUserMutation, useUpdateUserMutation } from './mutations';
import type { UserSummaryDto } from '../../shared/types';

interface UserDialogProps {
  open: boolean;
  mode: 'create' | 'edit';
  initialValues?: UserSummaryDto;
  onOpenChange: (open: boolean) => void;
}

export function UserDialog({ open, mode, initialValues, onOpenChange }: UserDialogProps) {
  const [apiError, setApiError] = useState<string | null>(null);
  const createMutation = useCreateUserMutation();
  const updateMutation = useUpdateUserMutation();

  const schema = mode === 'create' ? createUserSchema : updateUserSchema;
  const defaultValues = useMemo(
    () => getDefaultValues(initialValues, mode),
    [initialValues, mode],
  );

  const form = useForm<CreateUserForm | UpdateUserForm>({
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

  const onSubmit = async (values: CreateUserForm | UpdateUserForm) => {
    setApiError(null);
    try {
      if (mode === 'create') {
        const v = values as CreateUserForm;
        await createMutation.mutateAsync({ username: v.username, password: v.password, enabled: v.enabled });
        toast.success('User created');
      } else {
        if (!initialValues) return;
        const v = values as UpdateUserForm;
        await updateMutation.mutateAsync({
          userId: initialValues.userId,
          data: { enabled: v.enabled, newPassword: v.newPassword || undefined },
        });
        toast.success('User updated');
      }
      onOpenChange(false);
    } catch (error) {
      setApiError(getErrorMessage(error));
    }
  };

  const title = mode === 'create' ? 'Add User' : `Edit User: ${initialValues?.username ?? ''}`;

  return (
    <EntityDialog open={open} title={title} onOpenChange={onOpenChange}>
      <Form {...form}>
        <form onSubmit={(e) => { e.preventDefault(); void form.handleSubmit(onSubmit)(e); }} className="flex flex-col gap-4">
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="username"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Username</FormLabel>
                  <FormControl>
                    <Input {...field} autoComplete="off" placeholder="e.g. jsmith" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          {mode === 'create' && (
            <FormField
              control={form.control}
              name="password"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>Password</FormLabel>
                  <FormControl>
                    <Input {...field} type="password" autoComplete="new-password" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          {mode === 'edit' && (
            <FormField
              control={form.control}
              name="newPassword"
              render={({ field }) => (
                <FormItem>
                  <FormLabel>New Password (leave blank to keep current)</FormLabel>
                  <FormControl>
                    <Input {...field} type="password" autoComplete="new-password" />
                  </FormControl>
                  <FormMessage />
                </FormItem>
              )}
            />
          )}
          <FormField
            control={form.control}
            name="enabled"
            render={({ field }) => (
              <FormItem className="flex flex-row items-start space-x-3 space-y-0">
                <FormControl>
                  <Checkbox
                    checked={field.value as boolean}
                    onCheckedChange={field.onChange}
                  />
                </FormControl>
                <FormLabel>Enabled</FormLabel>
              </FormItem>
            )}
          />
          <FormError error={apiError} />
          <FormActions
            loading={form.formState.isSubmitting}
            onCancel={() => onOpenChange(false)}
            submitLabel={mode === 'create' ? 'Create User' : 'Save Changes'}
          />
        </form>
      </Form>
    </EntityDialog>
  );
}
```

- [ ] **Step 5: Convert userColumns to factory**

Open `src/MSOSync.Frontend/src/features/users/columns.ts`. Full file after edit:

```ts
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { UserSummaryDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

export function makeUserColumns(
  onEdit: (row: UserSummaryDto) => void,
  onDeactivate: (row: UserSummaryDto) => void,
  currentUsername?: string,
): ColDef<UserSummaryDto>[] {
  return [
    { field: 'userId', headerName: 'User ID', width: 90 },
    { field: 'username', headerName: 'Username', flex: 1, minWidth: 150 },
    {
      field: 'roles',
      headerName: 'Roles',
      width: 200,
      valueFormatter: (p) => {
        const roles = p.value as string[] | undefined;
        return roles ? roles.join(', ') : '—';
      },
    },
    {
      field: 'enabled',
      headerName: 'Status',
      width: 110,
      cellRenderer: (p: ICellRendererParams<UserSummaryDto>) =>
        StatusBadge({
          status: p.value ? 'Active' : 'Disabled',
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
      field: 'lastLoginTime',
      headerName: 'Last Login',
      width: 150,
      valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : 'Never'),
    },
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<UserSummaryDto>) => {
        if (!p.data) return null;
        const row = p.data;
        const isSelf = currentUsername != null && row.username === currentUsername;
        return ActionMenu({
          items: [
            { label: 'Edit', onClick: () => onEdit(row) },
            {
              label: 'Deactivate',
              onClick: () => onDeactivate(row),
              variant: 'destructive',
              disabled: isSelf,
            },
          ],
        });
      },
    },
  ];
}
```

- [ ] **Step 6: Check ActionMenu supports disabled prop**

Open `src/MSOSync.Frontend/src/shared/components/actions/ActionMenu.tsx` and verify that menu item objects support a `disabled` property. If they don't, add it:

The `ActionMenu` component from 10C likely has an `items` array of `{ label, onClick, variant? }`. If `disabled` is not yet supported, update `ActionMenu.tsx` to accept and apply it:

```tsx
// In ActionMenu.tsx, find the items type and add disabled support if missing:
// items: { label: string; onClick: () => void; variant?: 'default' | 'destructive'; disabled?: boolean }[]
// Then in the render: add `disabled={item.disabled}` to DropdownMenuItem
```

Only modify if `disabled` is not already supported.

- [ ] **Step 7: Update UsersGrid**

Open `src/MSOSync.Frontend/src/features/users/UsersGrid.tsx`. Full file after edit:

```tsx
import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeUserColumns } from './columns';
import { useUsers } from './hooks';
import type { UserSummaryDto } from '../../shared/types';

interface Props {
  quickFilterText?: string;
  onEdit: (row: UserSummaryDto) => void;
  onDeactivate: (row: UserSummaryDto) => void;
  currentUsername?: string;
}

export function UsersGrid({ quickFilterText, onEdit, onDeactivate, currentUsername }: Props) {
  const { data, isLoading, error, refetch } = useUsers();
  const columns = useMemo(
    () => makeUserColumns(onEdit, onDeactivate, currentUsername),
    [onEdit, onDeactivate, currentUsername],
  );
  return (
    <DataGrid
      rowData={data?.data}
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

- [ ] **Step 8: Update UsersPage**

Open `src/MSOSync.Frontend/src/features/users/UsersPage.tsx`. Full file after edit:

```tsx
import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { UsersGrid } from './UsersGrid';
import { UserDialog } from './UserDialog';
import { useDeactivateUserMutation } from './mutations';
import { useAuth } from '../auth/useAuth';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { UserSummaryDto } from '../../shared/types';

export function UsersPage() {
  const [search, setSearch] = useState('');
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<UserSummaryDto | null>(null);
  const [deactivateState, setDeactivateState] = useState<UserSummaryDto | null>(null);

  const deactivateMutation = useDeactivateUserMutation();
  const { user } = useAuth();

  const onEdit = useCallback((row: UserSummaryDto) => { setEditState(row); }, []);
  const onDeactivate = useCallback((row: UserSummaryDto) => { setDeactivateState(row); }, []);

  const handleDeactivateConfirm = async () => {
    if (!deactivateState) return;
    try {
      await deactivateMutation.mutateAsync(deactivateState.userId);
      toast.success(`User "${deactivateState.username}" deactivated`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeactivateState(null);
    }
  };

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Users</h1>
        <Button onClick={() => setCreateOpen(true)}>Add User</Button>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search users…"
        className="max-w-xs"
      />
      <UsersGrid
        quickFilterText={search}
        onEdit={onEdit}
        onDeactivate={onDeactivate}
        currentUsername={user?.username}
      />
      <UserDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <UserDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {deactivateState && (
        <ConfirmDialog
          open
          title="Deactivate User"
          description={`Deactivate user "${deactivateState.username}"? They will no longer be able to log in.`}
          confirmLabel="Deactivate"
          variant="destructive"
          loading={deactivateMutation.isPending}
          onConfirm={() => void handleDeactivateConfirm()}
          onOpenChange={(open) => { if (!open) setDeactivateState(null); }}
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
git add src/MSOSync.Frontend/src/shared/api/users.ts
git add src/MSOSync.Frontend/src/features/users/schemas.ts
git add src/MSOSync.Frontend/src/features/users/mutations.ts
git add src/MSOSync.Frontend/src/features/users/UserDialog.tsx
git add src/MSOSync.Frontend/src/features/users/columns.ts
git add src/MSOSync.Frontend/src/features/users/UsersGrid.tsx
git add src/MSOSync.Frontend/src/features/users/UsersPage.tsx
git add src/MSOSync.Frontend/src/shared/components/actions/ActionMenu.tsx
git commit -m "feat(10d): add users create/edit/deactivate forms"
```
