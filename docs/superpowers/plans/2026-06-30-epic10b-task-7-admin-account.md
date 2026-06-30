# Epic 10B — Task 7: Admin + Account Pages (Users, Parameters, Audit, Profile)

> Read the master plan first: `docs/superpowers/plans/2026-06-30-epic10b-data-tables.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

**Goal:** Replace the final 4 placeholder pages: Users (client-side, pageSize=200), Parameters (dual API + secret masking), Audit (server-side paginated), Profile (localStorage only).

**Files to create** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/users/columns.ts`
- `features/users/hooks.ts`
- `features/users/UsersGrid.tsx`
- `features/parameters/columns.ts`
- `features/parameters/hooks.ts`
- `features/parameters/ParametersGrid.tsx`
- `features/audit/columns.ts`
- `features/audit/hooks.ts`
- `features/audit/AuditFilters.tsx`
- `features/audit/AuditGrid.tsx`

Modify:
- `features/users/UsersPage.tsx` — replace PlaceholderPage
- `features/parameters/ParametersPage.tsx` — replace PlaceholderPage
- `features/audit/AuditPage.tsx` — replace PlaceholderPage
- `features/profile/ProfilePage.tsx` — replace PlaceholderPage

**Interfaces — Consumes (from Tasks 1 & 2):**
- `shared/api/users.ts` → `getUsers`
- `shared/api/parameters.ts` → `getParameters`, `getParameterDescriptors`
- `shared/api/audit.ts` → `getAuditLog`
- `shared/queryKeys.ts` → `queryKeys`
- `shared/types` → `UserSummaryDto`, `ParameterDto`, `ParameterDescriptorDto`, `AuditDto`, `AuditFilter`
- `shared/utils/date.ts` → `formatDateTime`, `formatRelativeTime`
- `shared/utils/status.ts` → (for enabled badge)
- `shared/components/data-display/DataGrid.tsx`
- `shared/components/data-display/ServerGrid.tsx`
- `shared/components/data-display/StatusBadge.tsx`
- `shared/constants/query.ts` → `DEFAULT_PAGE_SIZE`
- `features/auth/useAuth.ts` → `useAuth` (for Profile page)

---

### Users Page

Fetch strategy: call `getUsers({ page: 1, pageSize: 200 })` once. AG Grid handles client-side pagination of the result. Expected < 100 users in practice.

- [ ] **Step 1: Create `features/users/columns.ts`**

```typescript
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { UserSummaryDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const userColumns: ColDef<UserSummaryDto>[] = [
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
];
```

- [ ] **Step 2: Create `features/users/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getUsers } from '../../shared/api/users';

const ALL_USERS_FILTER = { page: 1, pageSize: 200 } as const;

export function useUsers() {
  return useQuery({
    queryKey: queryKeys.users(ALL_USERS_FILTER),
    queryFn: () => getUsers(ALL_USERS_FILTER),
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 3: Create `features/users/UsersGrid.tsx`**

```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { userColumns } from './columns';
import { useUsers } from './hooks';

interface Props { quickFilterText?: string; }

export function UsersGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useUsers();
  return (
    <DataGrid
      rowData={data?.data}
      columnDefs={userColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
```

- [ ] **Step 4: Replace `features/users/UsersPage.tsx`**

```tsx
import { useState } from 'react';
import { Input } from '../../components/ui/input';
import { UsersGrid } from './UsersGrid';

export function UsersPage() {
  const [search, setSearch] = useState('');
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Users</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search users…"
        className="max-w-xs"
      />
      <UsersGrid quickFilterText={search} />
    </div>
  );
}
```

---

### Parameters Page

Fetch both `getParameters()` and `getParameterDescriptors()` in parallel, join on `name` in the grid. Secret values always display as `••••••••`.

- [ ] **Step 5: Create `features/parameters/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getParameters, getParameterDescriptors } from '../../shared/api/parameters';

export function useParameters() {
  return useQuery({
    queryKey: queryKeys.parameters(),
    queryFn: getParameters,
    staleTime: 60_000,
    refetchOnWindowFocus: false,
  });
}

export function useParameterDescriptors() {
  return useQuery({
    queryKey: queryKeys.parameterDescriptors(),
    queryFn: getParameterDescriptors,
    staleTime: 300_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 6: Create `features/parameters/columns.ts`**

The parameter grid joins ParameterDto with ParameterDescriptorDto by name. Define a merged row type and columns that use `valueGetter` to pull from descriptor data passed in via context.

```typescript
import type { ColDef } from 'ag-grid-community';
import type { ParameterDto, ParameterDescriptorDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';

export interface ParameterRow extends ParameterDto {
  descriptor?: ParameterDescriptorDto;
}

export const parameterColumns: ColDef<ParameterRow>[] = [
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
];
```

- [ ] **Step 7: Create `features/parameters/ParametersGrid.tsx`**

```tsx
import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { parameterColumns } from './columns';
import type { ParameterRow } from './columns';
import { useParameters, useParameterDescriptors } from './hooks';

export function ParametersGrid() {
  const { data: params, isLoading: paramsLoading, error: paramsError, refetch: refetchParams } = useParameters();
  const { data: descriptors } = useParameterDescriptors();

  const rows: ParameterRow[] | undefined = useMemo(() => {
    if (!params) return undefined;
    const descriptorMap = new Map((descriptors ?? []).map((d) => [d.name, d]));
    return params.map((p) => ({ ...p, descriptor: descriptorMap.get(p.name) }));
  }, [params, descriptors]);

  return (
    <DataGrid
      rowData={rows}
      columnDefs={parameterColumns}
      loading={paramsLoading}
      error={paramsError}
      onRetry={() => void refetchParams()}
      height={500}
    />
  );
}
```

- [ ] **Step 8: Replace `features/parameters/ParametersPage.tsx`**

```tsx
import { ParametersGrid } from './ParametersGrid';

export function ParametersPage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Parameters</h1>
      <p className="text-sm text-neutral-500 dark:text-neutral-400">
        Secret values are masked. Edit actions available in Epic 10C.
      </p>
      <ParametersGrid />
    </div>
  );
}
```

---

### Audit Page

Server-side paginated with username/actionName/date filters.

- [ ] **Step 9: Create `features/audit/columns.ts`**

```typescript
import type { ColDef } from 'ag-grid-community';
import type { AuditDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';

export const auditColumns: ColDef<AuditDto>[] = [
  { field: 'auditId', headerName: 'Audit ID', width: 100 },
  { field: 'username', headerName: 'Username', width: 160 },
  { field: 'actionName', headerName: 'Action', width: 200 },
  { field: 'objectName', headerName: 'Object', flex: 1, minWidth: 150 },
  { field: 'correlationId', headerName: 'Correlation ID', width: 180 },
  {
    field: 'createTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
```

- [ ] **Step 10: Create `features/audit/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import type { AuditFilter } from '../../shared/types';
import { queryKeys } from '../../shared/queryKeys';
import { getAuditLog } from '../../shared/api/audit';

export function useAuditLog(filter: AuditFilter) {
  return useQuery({
    queryKey: queryKeys.auditLog(filter),
    queryFn: () => getAuditLog(filter),
    placeholderData: (prev) => prev,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 11: Create `features/audit/AuditFilters.tsx`**

```tsx
import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import { Input } from '../../components/ui/input';
import { Label } from '../../components/ui/label';
import { Button } from '../../components/ui/button';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

interface Props { onFilter: (filter: AuditFilter) => void; }

export function AuditFilters({ onFilter }: Props) {
  const [username, setUsername] = useState('');
  const [actionName, setActionName] = useState('');
  const [from, setFrom] = useState('');
  const [to, setTo] = useState('');

  function handleApply() {
    onFilter({
      username: username || undefined,
      actionName: actionName || undefined,
      from: from || undefined,
      to: to || undefined,
      page: 1,
      pageSize: DEFAULT_PAGE_SIZE,
    });
  }

  function handleReset() {
    setUsername(''); setActionName(''); setFrom(''); setTo('');
    onFilter({ page: 1, pageSize: DEFAULT_PAGE_SIZE });
  }

  return (
    <div className="flex flex-wrap items-end gap-3 p-4 bg-neutral-50 dark:bg-neutral-900 rounded-lg">
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Username</Label>
        <Input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="username" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">Action</Label>
        <Input value={actionName} onChange={(e) => setActionName(e.target.value)} placeholder="action name" className="h-8 w-40 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">From</Label>
        <Input type="date" value={from} onChange={(e) => setFrom(e.target.value)} className="h-8 text-sm" />
      </div>
      <div className="flex flex-col gap-1">
        <Label className="text-xs">To</Label>
        <Input type="date" value={to} onChange={(e) => setTo(e.target.value)} className="h-8 text-sm" />
      </div>
      <Button size="sm" onClick={handleApply}>Apply</Button>
      <Button size="sm" variant="outline" onClick={handleReset}>Reset</Button>
    </div>
  );
}
```

- [ ] **Step 12: Create `features/audit/AuditGrid.tsx`**

```tsx
import type { AuditFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { auditColumns } from './columns';
import { useAuditLog } from './hooks';

interface Props { filter: AuditFilter; onFilterChange: (f: AuditFilter) => void; }

export function AuditGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useAuditLog(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={auditColumns}
      loading={isLoading}
      total={data?.total ?? 0}
      page={filter.page}
      pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
```

- [ ] **Step 13: Replace `features/audit/AuditPage.tsx`**

```tsx
import { useState } from 'react';
import type { AuditFilter } from '../../shared/types';
import { AuditFilters } from './AuditFilters';
import { AuditGrid } from './AuditGrid';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: AuditFilter = { page: 1, pageSize: DEFAULT_PAGE_SIZE };

export function AuditPage() {
  const [filter, setFilter] = useState<AuditFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Audit Log</h1>
      <AuditFilters onFilter={setFilter} />
      <AuditGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
```

---

### Profile Page

No API call. Reads `useAuth()` for current user. Displays username, roles, and token expiry countdown.

- [ ] **Step 14: Replace `features/profile/ProfilePage.tsx`**

```tsx
import { useAuth } from '../auth/useAuth';
import { Card, CardContent, CardHeader, CardTitle } from '../../components/ui/card';
import { Badge } from '../../components/ui/badge';

function tokenExpiryLabel(expiresAt: string): string {
  const diffMs = new Date(expiresAt).getTime() - Date.now();
  if (diffMs <= 0) return 'Expired';
  const diffMin = Math.floor(diffMs / 60_000);
  if (diffMin < 1) return 'Less than 1 minute';
  if (diffMin < 60) return `${diffMin} minutes`;
  const diffHr = Math.floor(diffMin / 60);
  return `${diffHr} hour${diffHr !== 1 ? 's' : ''}`;
}

export function ProfilePage() {
  const { user } = useAuth();

  if (!user) {
    return (
      <div className="p-6">
        <p className="text-neutral-500 dark:text-neutral-400">Not signed in.</p>
      </div>
    );
  }

  return (
    <div className="p-6 max-w-lg">
      <h1 className="text-2xl font-semibold mb-6">Profile</h1>
      <Card>
        <CardHeader>
          <CardTitle className="text-lg">{user.username}</CardTitle>
        </CardHeader>
        <CardContent className="flex flex-col gap-4">
          <div>
            <p className="text-sm font-medium text-neutral-500 dark:text-neutral-400 mb-1">Roles</p>
            <div className="flex flex-wrap gap-2">
              {user.roles.length > 0
                ? user.roles.map((role) => (
                    <Badge key={role} variant="secondary">
                      {role}
                    </Badge>
                  ))
                : <span className="text-sm text-neutral-500">No roles assigned</span>}
            </div>
          </div>
          <div>
            <p className="text-sm font-medium text-neutral-500 dark:text-neutral-400 mb-1">
              Token expires in
            </p>
            <p className="text-sm">{tokenExpiryLabel(user.expiresAt)}</p>
          </div>
        </CardContent>
      </Card>
    </div>
  );
}
```

- [ ] **Step 15: Verify build + lint + tests pass**

Run from `src/MSOSync.Frontend/`:
```
npm run build
```
Expected: exits 0.

```
npm run lint
```
Expected: 0 errors.

```
npm test
```
Expected: 12/12 pass.

- [ ] **Step 16: Final acceptance check**

Verify all PlaceholderPage usages are gone (except Topology has no PlaceholderPage — it has a real implementation). Run:
```
grep -r "PlaceholderPage" src/MSOSync.Frontend/src/features/
```
Expected: 0 matches (all placeholder imports have been replaced).

- [ ] **Step 17: Commit**

```
git add src/MSOSync.Frontend/src/features/users/
git add src/MSOSync.Frontend/src/features/parameters/
git add src/MSOSync.Frontend/src/features/audit/
git add src/MSOSync.Frontend/src/features/profile/ProfilePage.tsx
git commit -m "feat(10b): wire users, parameters, audit, and profile pages"
```

---

## Report Contract

Write report to the path specified by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files created/modified (count)
- Build result
- Lint result
- Test result (N/12 pass)
- PlaceholderPage grep result (0 matches expected)
- Any concerns
