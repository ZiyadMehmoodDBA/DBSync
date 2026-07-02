# Task 3: Frontend Shared Infrastructure

**Part of:** Epic 11F — Fine-Grained RBAC  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11f-rbac-design.md`  
**Depends on:** Task 2 (API endpoints must exist)

## Files

**Create:**
- `src/MSOSync.Frontend/src/shared/types/permissions.ts`
- `src/MSOSync.Frontend/src/shared/api/permissions.ts`
- `src/MSOSync.Frontend/src/shared/hooks/usePermissions.ts`
- `src/MSOSync.Frontend/src/shared/hooks/useRoles.ts`

**Modify:**
- `src/MSOSync.Frontend/src/shared/types/index.ts` — add `export * from './permissions'`
- `src/MSOSync.Frontend/src/shared/queryKeys.ts` — add permissions/roles keys
- `src/MSOSync.Frontend/src/shared/signalr/types.ts` — add `PermissionEvent` interface
- `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts` — add `onPermissionEvent` option
- `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts` — add `routePermissionEvent`
- `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx` — wire PermissionEvent handler
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add `usePermissions()` prefetch

## Interfaces Produced (consumed by Tasks 4–5)

```typescript
// From usePermissions.ts
usePermissions(): UseQueryResult<EffectivePermissionsDto>
useHasPermission(key: PermissionKey): boolean  // fail-closed: returns false while loading

// From useRoles.ts
usePermissionCatalog(): UseQueryResult<PermissionDto[]>
useRoles(): UseQueryResult<RolePermissionsDto[]>
useRoleDetail(roleName: string): UseQueryResult<RolePermissionsDto>
useGrantPermission(): UseMutationResult
useRevokePermission(): UseMutationResult
useResetRole(): UseMutationResult
useCopyFrom(): UseMutationResult

// From types/permissions.ts
PermissionKeys.ViewEvents / .ViewMetrics / .ViewAudit / .ViewTopology /
  .ExportData / .RetryBatches / .ApproveNodes / .ReleaseLocks /
  .EditParameters / .ManageTriggers / .ManageRouters / .ManageUsers
type PermissionKey  (the union of all string values)
EffectivePermissionsDto  { role: string; permissions: PermissionKey[]; updatedAt: string }
PermissionDto            { permissionKey: PermissionKey; description: string; category: string }
RolePermissionsDto       { roleName: string; permissions: PermissionDto[] }

// From queryKeys.ts
queryKeys.permissions()          → ['permissions']
queryKeys.permissionCatalog()    → ['permission-catalog']
queryKeys.roles()                → ['roles']
queryKeys.role(name: string)     → ['roles', name]
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum` keyword; use `as const`
- All imports relative — no `@/` aliases
- No new npm packages
- API client base URL is `/api/v1` — use paths `/me/permissions`, `/permissions`, `/roles`, etc.
- `usePermissions()` must set `staleTime: Infinity, refetchOnWindowFocus: false`
- `useHasPermission()` must be fail-closed: return `false` while data is undefined
- Build env: frontend only (not relevant for this task)

---

- [ ] **Step 1: Create `src/MSOSync.Frontend/src/shared/types/permissions.ts`**

```typescript
export const PermissionKeys = {
  ViewEvents:     'VIEW_EVENTS',
  ViewMetrics:    'VIEW_METRICS',
  ViewAudit:      'VIEW_AUDIT',
  ViewTopology:   'VIEW_TOPOLOGY',
  ExportData:     'EXPORT_DATA',
  RetryBatches:   'RETRY_BATCHES',
  ApproveNodes:   'APPROVE_NODES',
  ReleaseLocks:   'RELEASE_LOCKS',
  EditParameters: 'EDIT_PARAMETERS',
  ManageTriggers: 'MANAGE_TRIGGERS',
  ManageRouters:  'MANAGE_ROUTERS',
  ManageUsers:    'MANAGE_USERS',
} as const;

export type PermissionKey = (typeof PermissionKeys)[keyof typeof PermissionKeys];

export interface EffectivePermissionsDto {
  role: string;
  permissions: PermissionKey[];
  updatedAt: string;
}

export interface PermissionDto {
  permissionKey: PermissionKey;
  description: string;
  category: string;
}

export interface RolePermissionsDto {
  roleName: string;
  permissions: PermissionDto[];
}
```

- [ ] **Step 2: Create `src/MSOSync.Frontend/src/shared/api/permissions.ts`**

The API client lives at `src/MSOSync.Frontend/src/shared/api/client.ts` with `baseURL: '/api/v1'`. Import from `./client`.

```typescript
import client from './client';
import type { EffectivePermissionsDto, PermissionDto, RolePermissionsDto, PermissionKey } from '../types/permissions';

export async function getMyPermissions(): Promise<EffectivePermissionsDto> {
  return client.get<EffectivePermissionsDto>('/me/permissions').then(r => r.data);
}

export async function getPermissionCatalog(): Promise<PermissionDto[]> {
  return client.get<PermissionDto[]>('/permissions').then(r => r.data);
}

export async function getRoles(): Promise<RolePermissionsDto[]> {
  return client.get<RolePermissionsDto[]>('/roles').then(r => r.data);
}

export async function getRoleDetail(roleName: string): Promise<RolePermissionsDto> {
  return client.get<RolePermissionsDto>(`/roles/${roleName}`).then(r => r.data);
}

export async function grantPermission(roleName: string, key: PermissionKey): Promise<void> {
  await client.put(`/roles/${roleName}/permissions/${key}`);
}

export async function revokePermission(roleName: string, key: PermissionKey): Promise<void> {
  await client.delete(`/roles/${roleName}/permissions/${key}`);
}

export async function resetRole(roleName: string): Promise<void> {
  await client.post(`/roles/${roleName}/reset`);
}

export async function copyFrom(targetRole: string, sourceRole: string): Promise<void> {
  await client.post(`/roles/${targetRole}/copy-from/${sourceRole}`);
}
```

- [ ] **Step 3: Update `src/MSOSync.Frontend/src/shared/types/index.ts`**

Read the file first. It ends with `export * from './preferences';`. Add one line after it:

```typescript
export * from './permissions';
```

- [ ] **Step 4: Update `src/MSOSync.Frontend/src/shared/queryKeys.ts`**

Read the file first. The `queryKeys` object currently ends with `userPreferences: () => ['user-preferences'] as const,`. Add four new entries after it (before the closing `}`):

```typescript
  permissions:      () => ['permissions'] as const,
  permissionCatalog: () => ['permission-catalog'] as const,
  roles:             () => ['roles'] as const,
  role:              (name: string) => ['roles', name] as const,
```

- [ ] **Step 5: Add `PermissionEvent` to `src/MSOSync.Frontend/src/shared/signalr/types.ts`**

Read the file first. It exports `OperationsEvent`, `OperationsEventType`, etc. Add at the end:

```typescript
export interface PermissionEvent {
  roleName: string;
  action: string;
  occurredAt: string;
}
```

- [ ] **Step 6: Update `src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts`**

Read the file first. Make two changes:

**Change 1** — import `PermissionEvent` in the types import:

Find:
```typescript
import type { ConnectionState, OperationsEvent } from './types';
```
Replace with:
```typescript
import type { ConnectionState, OperationsEvent, PermissionEvent } from './types';
```

**Change 2** — add `onPermissionEvent` to `UseSignalROptions`:

Find:
```typescript
interface UseSignalROptions {
  getAccessToken: () => string | null;
  isAuthenticated: boolean;
  queryClient: QueryClient;
  onEvent: (event: OperationsEvent) => void;
}
```
Replace with:
```typescript
interface UseSignalROptions {
  getAccessToken: () => string | null;
  isAuthenticated: boolean;
  queryClient: QueryClient;
  onEvent: (event: OperationsEvent) => void;
  onPermissionEvent?: (event: PermissionEvent) => void;
}
```

**Change 3** — destructure `onPermissionEvent` in the function signature and register the listener. Find the function signature:

```typescript
export function useSignalR({
  getAccessToken,
  isAuthenticated,
  queryClient,
  onEvent,
}: UseSignalROptions) {
```
Replace with:
```typescript
export function useSignalR({
  getAccessToken,
  isAuthenticated,
  queryClient,
  onEvent,
  onPermissionEvent,
}: UseSignalROptions) {
```

**Change 4** — register the `PermissionEvent` listener. Find the line:

```typescript
    conn.on('OperationsEvent', (event: OperationsEvent) => {
      onEvent(event);
    });
```
Replace with:
```typescript
    conn.on('OperationsEvent', (event: OperationsEvent) => {
      onEvent(event);
    });

    conn.on('PermissionEvent', (event: PermissionEvent) => {
      onPermissionEvent?.(event);
    });
```

- [ ] **Step 7: Add `routePermissionEvent` to `src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts`**

Read the file first. Add the import of `PermissionEvent` to the existing types import:

Find:
```typescript
import { OperationsEventType, type OperationsEvent } from './types';
```
Replace with:
```typescript
import { OperationsEventType, type OperationsEvent, type PermissionEvent } from './types';
```

Add at the end of the file (after `invalidateOperational`):

```typescript
export async function routePermissionEvent(
  queryClient: QueryClient,
  _event: PermissionEvent,
): Promise<void> {
  await Promise.all([
    queryClient.invalidateQueries({ queryKey: ['permissions'] }),
    queryClient.invalidateQueries({ queryKey: ['roles'] }),
  ]);
}
```

- [ ] **Step 8: Update `src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx`**

Read the file first. Make these changes:

**Change 1** — update the `routeToCache` import to also import `routePermissionEvent`:

Find:
```typescript
import { routeToCache } from './eventRouter';
```
Replace with:
```typescript
import { routeToCache, routePermissionEvent } from './eventRouter';
```

**Change 2** — import `PermissionEvent`:

Find:
```typescript
import type { OperationsEvent } from './types';
```
Replace with:
```typescript
import type { OperationsEvent, PermissionEvent } from './types';
```

**Change 3** — add `handlePermissionEvent` callback inside `SignalRProvider`, right after `handleEvent`:

Find:
```typescript
  const handleEvent = useCallback(
    (event: OperationsEvent) => {
      void routeToCache(queryClient, event);
      routeToToast(event);
    },
    [queryClient],
  );
```
Replace with:
```typescript
  const handleEvent = useCallback(
    (event: OperationsEvent) => {
      void routeToCache(queryClient, event);
      routeToToast(event);
    },
    [queryClient],
  );

  const handlePermissionEvent = useCallback(
    (event: PermissionEvent) => {
      void routePermissionEvent(queryClient, event);
    },
    [queryClient],
  );
```

**Change 4** — pass `onPermissionEvent` to `useSignalR`:

Find:
```typescript
  const { connectionState, lastConnectedAt, lastDisconnectedAt } = useSignalR({
    getAccessToken,
    isAuthenticated: accessToken !== null,
    queryClient,
    onEvent: handleEvent,
  });
```
Replace with:
```typescript
  const { connectionState, lastConnectedAt, lastDisconnectedAt } = useSignalR({
    getAccessToken,
    isAuthenticated: accessToken !== null,
    queryClient,
    onEvent: handleEvent,
    onPermissionEvent: handlePermissionEvent,
  });
```

- [ ] **Step 9: Create `src/MSOSync.Frontend/src/shared/hooks/usePermissions.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { getMyPermissions } from '../api/permissions';
import { queryKeys } from '../queryKeys';
import type { PermissionKey } from '../types/permissions';

export function usePermissions() {
  return useQuery({
    queryKey: queryKeys.permissions(),
    queryFn:  getMyPermissions,
    staleTime: Infinity,
    refetchOnWindowFocus: false,
  });
}

export function useHasPermission(key: PermissionKey): boolean {
  const { data } = usePermissions();
  if (data === undefined) return false;
  return data.permissions.includes(key);
}
```

- [ ] **Step 10: Create `src/MSOSync.Frontend/src/shared/hooks/useRoles.ts`**

```typescript
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getPermissionCatalog,
  getRoles,
  getRoleDetail,
  grantPermission,
  revokePermission,
  resetRole,
  copyFrom,
} from '../api/permissions';
import { queryKeys } from '../queryKeys';
import type { PermissionKey } from '../types/permissions';

export function usePermissionCatalog() {
  return useQuery({
    queryKey: queryKeys.permissionCatalog(),
    queryFn:  getPermissionCatalog,
    staleTime: Infinity,
  });
}

export function useRoles() {
  return useQuery({
    queryKey: queryKeys.roles(),
    queryFn:  getRoles,
  });
}

export function useRoleDetail(roleName: string) {
  return useQuery({
    queryKey: queryKeys.role(roleName),
    queryFn:  () => getRoleDetail(roleName),
  });
}

export function useGrantPermission() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ roleName, key }: { roleName: string; key: PermissionKey }) =>
      grantPermission(roleName, key),
    onSuccess: (_data, { roleName }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(roleName) });
    },
  });
}

export function useRevokePermission() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ roleName, key }: { roleName: string; key: PermissionKey }) =>
      revokePermission(roleName, key),
    onSuccess: (_data, { roleName }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(roleName) });
    },
  });
}

export function useResetRole() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (roleName: string) => resetRole(roleName),
    onSuccess: (_data, roleName) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(roleName) });
    },
  });
}

export function useCopyFrom() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ targetRole, sourceRole }: { targetRole: string; sourceRole: string }) =>
      copyFrom(targetRole, sourceRole),
    onSuccess: (_data, { targetRole }) => {
      void queryClient.invalidateQueries({ queryKey: queryKeys.role(targetRole) });
      void queryClient.invalidateQueries({ queryKey: queryKeys.roles() });
    },
  });
}
```

- [ ] **Step 11: Update `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — add permissions prefetch**

Read the file first. The component body already starts with `usePreferences()`. Add `usePermissions()` on the next line.

Add import for `usePermissions`:

Find the import block containing `usePreferences`:
```typescript
import { usePreferences, usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
```
Replace with:
```typescript
import { usePreferences, usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { usePermissions } from '../../shared/hooks/usePermissions';
```

Then inside the `AppLayout` function body, find:
```typescript
  // Prefetch preferences for the whole session (staleTime: Infinity means one fetch)
  usePreferences();
```
Replace with:
```typescript
  // Prefetch preferences and permissions for the whole session
  usePreferences();
  usePermissions();
```

- [ ] **Step 12: Build check**

```pwsh
cd src/MSOSync.Frontend
npm run build 2>&1 | Select-Object -Last 15
```

Expected: 0 TypeScript errors. Fix any type errors before proceeding.

- [ ] **Step 13: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/shared/types/permissions.ts `
  src/MSOSync.Frontend/src/shared/api/permissions.ts `
  src/MSOSync.Frontend/src/shared/hooks/usePermissions.ts `
  src/MSOSync.Frontend/src/shared/hooks/useRoles.ts `
  src/MSOSync.Frontend/src/shared/types/index.ts `
  src/MSOSync.Frontend/src/shared/queryKeys.ts `
  src/MSOSync.Frontend/src/shared/signalr/types.ts `
  src/MSOSync.Frontend/src/shared/signalr/useSignalR.ts `
  src/MSOSync.Frontend/src/shared/signalr/eventRouter.ts `
  src/MSOSync.Frontend/src/shared/signalr/SignalRProvider.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx

git commit -m "feat(11f): add permissions types + API + usePermissions + useRoles + SignalR PermissionEvent handler"
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Build: <result>
Concerns: <none or list>
```
