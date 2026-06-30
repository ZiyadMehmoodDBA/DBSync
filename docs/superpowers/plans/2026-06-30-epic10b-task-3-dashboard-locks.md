# Epic 10B — Task 3: Dashboard + Locks + Router/Sidebar Update

> Read the master plan first: `docs/superpowers/plans/2026-06-30-epic10b-data-tables.md`
> Spec: `docs/superpowers/specs/2026-06-30-epic10b-data-tables-design.md`

**Goal:** Replace DashboardPage placeholder with live summary cards + activity feed (30s auto-refresh). Create LocksPage. Add `/locks` route and Locks sidebar entry.

**Files to create/modify** (all under `src/MSOSync.Frontend/src/`):

Create:
- `features/dashboard/hooks.ts`
- `features/dashboard/SummaryCards.tsx`
- `features/dashboard/ActivityFeed.tsx`
- `features/locks/columns.ts`
- `features/locks/hooks.ts`
- `features/locks/LocksGrid.tsx`
- `features/locks/LocksPage.tsx`

Modify:
- `features/dashboard/DashboardPage.tsx` — replace PlaceholderPage
- `app/router.tsx` — add `/locks` route
- `app/layouts/AppLayout.tsx` — add Locks to Administration sidebar group

**Interfaces — Consumes (from Tasks 1 & 2):**
- `shared/api/dashboard.ts` → `getDashboardSummary`, `getDashboardActivity`
- `shared/api/locks.ts` → `getLocks`
- `shared/queryKeys.ts` → `queryKeys`
- `shared/constants/query.ts` → `DASHBOARD_REFRESH_MS`
- `shared/types` → `DashboardSummaryDto`, `ActivityItemDto`, `LockDto`
- `shared/utils/date.ts` → `formatDateTime`, `formatRelativeTime`
- `shared/utils/numbers.ts` → `formatQueueDepth`
- `shared/components/data-display/SummaryCard.tsx`
- `shared/components/data-display/DataGrid.tsx`
- `shared/components/data-display/StatusBadge.tsx`
- `shared/components/feedback/ErrorState.tsx`

**React Query import:** `import { useQuery } from '@tanstack/react-query'`

---

- [ ] **Step 1: Create `features/dashboard/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getDashboardSummary, getDashboardActivity } from '../../shared/api/dashboard';
import { DASHBOARD_REFRESH_MS } from '../../shared/constants/query';

export function useDashboardSummary() {
  return useQuery({
    queryKey: queryKeys.dashboardSummary(),
    queryFn: getDashboardSummary,
    refetchInterval: DASHBOARD_REFRESH_MS,
    refetchIntervalInBackground: false,
    refetchOnWindowFocus: true,
  });
}

export function useDashboardActivity(page: number) {
  return useQuery({
    queryKey: queryKeys.dashboardActivity(page),
    queryFn: () => getDashboardActivity(page, 20),
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 2: Create `features/dashboard/SummaryCards.tsx`**

```tsx
import { Skeleton } from '../../components/ui/skeleton';
import { SummaryCard } from '../../shared/components/data-display/SummaryCard';
import { ErrorState } from '../../shared/components/feedback/ErrorState';
import { formatRelativeTime } from '../../shared/utils/date';
import { formatQueueDepth } from '../../shared/utils/numbers';
import { useDashboardSummary } from './hooks';

export function SummaryCards() {
  const { data, error, isLoading, refetch } = useDashboardSummary();

  if (error) return <ErrorState error={error} onRetry={() => void refetch()} />;

  if (isLoading) {
    return (
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        {Array.from({ length: 6 }).map((_, i) => (
          <Skeleton key={i} className="h-24 rounded-lg" />
        ))}
      </div>
    );
  }

  return (
    <div className="flex flex-col gap-2">
      <div className="grid grid-cols-2 gap-4 md:grid-cols-3 lg:grid-cols-6">
        <SummaryCard title="Total Nodes" value={data?.totalNodes ?? '—'} />
        <SummaryCard title="Reachable" value={data?.reachableNodes ?? '—'} variant="success" />
        <SummaryCard title="Degraded" value={data?.degradedNodes ?? '—'} variant="warning" />
        <SummaryCard title="Unreachable" value={data?.unreachableNodes ?? '—'} variant="danger" />
        <SummaryCard title="Events Today" value={data?.eventsToday ?? '—'} />
        <SummaryCard
          title="Queue Depth"
          value={data ? formatQueueDepth(data.queueDepth) : '—'}
        />
      </div>
      {data?.generatedAt && (
        <p className="text-xs text-neutral-500 dark:text-neutral-400">
          Updated {formatRelativeTime(data.generatedAt)}
        </p>
      )}
    </div>
  );
}
```

- [ ] **Step 3: Create `features/dashboard/ActivityFeed.tsx`**

The activity feed fetches page 1 with pageSize=20. Data is shown in a DataGrid (AG Grid handles client-side display of the 20 items).

```tsx
import type { ColDef } from 'ag-grid-community';
import type { ActivityItemDto } from '../../shared/types';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { formatDateTime } from '../../shared/utils/date';
import { useDashboardActivity } from './hooks';

const columns: ColDef<ActivityItemDto>[] = [
  { field: 'createTime', headerName: 'Time', valueFormatter: (p) => formatDateTime(p.value as string), width: 160 },
  { field: 'type', headerName: 'Type', width: 120 },
  { field: 'description', headerName: 'Description', flex: 1, minWidth: 200 },
  { field: 'nodeId', headerName: 'Node', width: 150 },
];

export function ActivityFeed() {
  const { data, isLoading, error, refetch } = useDashboardActivity(1);

  return (
    <div className="flex flex-col gap-2">
      <h2 className="text-base font-semibold">Recent Activity</h2>
      <DataGrid
        rowData={data?.data}
        columnDefs={columns}
        loading={isLoading}
        error={error}
        onRetry={() => void refetch()}
        height={320}
      />
    </div>
  );
}
```

- [ ] **Step 4: Replace `features/dashboard/DashboardPage.tsx`**

```tsx
import { SummaryCards } from './SummaryCards';
import { ActivityFeed } from './ActivityFeed';

export function DashboardPage() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <h1 className="text-2xl font-semibold">Dashboard</h1>
      <SummaryCards />
      <ActivityFeed />
    </div>
  );
}
```

- [ ] **Step 5: Create `features/locks/columns.ts`**

```typescript
import type { ColDef } from 'ag-grid-community';
import type { LockDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';

export const lockColumns: ColDef<LockDto>[] = [
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
];
```

- [ ] **Step 6: Create `features/locks/hooks.ts`**

```typescript
import { useQuery } from '@tanstack/react-query';
import { queryKeys } from '../../shared/queryKeys';
import { getLocks } from '../../shared/api/locks';

export function useLocks() {
  return useQuery({
    queryKey: queryKeys.locks(),
    queryFn: getLocks,
    staleTime: 30_000,
    refetchOnWindowFocus: false,
  });
}
```

- [ ] **Step 7: Create `features/locks/LocksGrid.tsx`**

```tsx
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { lockColumns } from './columns';
import { useLocks } from './hooks';

export function LocksGrid() {
  const { data, isLoading, error, refetch } = useLocks();

  return (
    <DataGrid
      rowData={data}
      columnDefs={lockColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
```

- [ ] **Step 8: Create `features/locks/LocksPage.tsx`**

```tsx
import { LocksGrid } from './LocksGrid';

export function LocksPage() {
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Locks</h1>
      <p className="text-sm text-neutral-500 dark:text-neutral-400">
        Active distributed locks. Release actions available in Epic 10C.
      </p>
      <LocksGrid />
    </div>
  );
}
```

- [ ] **Step 9: Update `app/router.tsx` — add `/locks` route**

Current `router.tsx` has these imports and routes. Add the LocksPage import and route:

```tsx
import { createBrowserRouter, Navigate } from 'react-router-dom';
import { RootInitializer } from '../features/auth/RootInitializer';
import { AuthGuard } from '../features/auth/AuthGuard';
import { LoginPage } from '../features/auth/LoginPage';
import { AuthLayout } from './layouts/AuthLayout';
import { AppLayout } from './layouts/AppLayout';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { TopologyPage } from '../features/topology/TopologyPage';
import { NodesPage } from '../features/nodes/NodesPage';
import { ChannelsPage } from '../features/channels/ChannelsPage';
import { TriggersPage } from '../features/triggers/TriggersPage';
import { RoutersPage } from '../features/routers/RoutersPage';
import { EventsPage } from '../features/events/EventsPage';
import { IncomingBatchesPage } from '../features/batches/IncomingBatchesPage';
import { OutgoingBatchesPage } from '../features/batches/OutgoingBatchesPage';
import { BatchErrorsPage } from '../features/batches/BatchErrorsPage';
import { MetricsPage } from '../features/metrics/MetricsPage';
import { UsersPage } from '../features/users/UsersPage';
import { ParametersPage } from '../features/parameters/ParametersPage';
import { AuditPage } from '../features/audit/AuditPage';
import { ProfilePage } from '../features/profile/ProfilePage';
import { LocksPage } from '../features/locks/LocksPage';

export const router = createBrowserRouter([
  {
    path: '/',
    element: <RootInitializer />,
    children: [
      {
        element: <AuthLayout />,
        children: [
          { path: 'login', element: <LoginPage /> },
        ],
      },
      {
        element: <AuthGuard />,
        children: [
          {
            element: <AppLayout />,
            children: [
              { index: true, element: <Navigate to="/dashboard" replace /> },
              { path: 'dashboard',        element: <DashboardPage /> },
              { path: 'events',           element: <EventsPage /> },
              { path: 'incoming-batches', element: <IncomingBatchesPage /> },
              { path: 'outgoing-batches', element: <OutgoingBatchesPage /> },
              { path: 'batch-errors',     element: <BatchErrorsPage /> },
              { path: 'metrics',          element: <MetricsPage /> },
              { path: 'topology',         element: <TopologyPage /> },
              { path: 'nodes',            element: <NodesPage /> },
              { path: 'channels',         element: <ChannelsPage /> },
              { path: 'triggers',         element: <TriggersPage /> },
              { path: 'routers',          element: <RoutersPage /> },
              { path: 'users',            element: <UsersPage /> },
              { path: 'parameters',       element: <ParametersPage /> },
              { path: 'audit',            element: <AuditPage /> },
              { path: 'locks',            element: <LocksPage /> },
              { path: 'profile',          element: <ProfilePage /> },
            ],
          },
        ],
      },
    ],
  },
]);
```

- [ ] **Step 10: Update `app/layouts/AppLayout.tsx` — add Locks to Administration sidebar**

Find the `NAV_GROUPS` array in `AppLayout.tsx`. The Administration group currently has:
```typescript
{
  heading: 'Administration',
  items: [
    { label: 'Users',      path: '/users',      icon: Users },
    { label: 'Parameters', path: '/parameters', icon: Settings },
    { label: 'Audit',      path: '/audit',      icon: FileText },
  ],
},
```

Add `Lock` icon import and `Locks` entry:

At the top of the file, add `Lock` to the lucide-react import:
```typescript
import {
  LayoutDashboard,
  Network,
  Server,
  Cable,
  Zap,
  GitBranch,
  Activity,
  ArrowDownCircle,
  ArrowUpCircle,
  AlertTriangle,
  BarChart2,
  Users,
  Settings,
  FileText,
  Lock,
  User,
  Sun,
  Moon,
  LogOut,
} from 'lucide-react';
```

Update the Administration group:
```typescript
{
  heading: 'Administration',
  items: [
    { label: 'Users',      path: '/users',      icon: Users },
    { label: 'Parameters', path: '/parameters', icon: Settings },
    { label: 'Audit',      path: '/audit',      icon: FileText },
    { label: 'Locks',      path: '/locks',      icon: Lock },
  ],
},
```

- [ ] **Step 11: Verify build + lint + tests pass**

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

- [ ] **Step 12: Commit**

```
git add src/MSOSync.Frontend/src/features/dashboard/
git add src/MSOSync.Frontend/src/features/locks/
git add src/MSOSync.Frontend/src/app/router.tsx
git add src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git commit -m "feat(10b): wire dashboard live data + add locks page + update router/sidebar"
```

---

## Report Contract

Write report to the path specified by the controller. Include:
- Status: DONE / DONE_WITH_CONCERNS / BLOCKED
- Files created/modified (count)
- Build result
- Lint result
- Test result (N/12 pass)
- Any concerns
