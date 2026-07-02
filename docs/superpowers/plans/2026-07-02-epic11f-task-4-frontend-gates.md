# Task 4: Frontend Page Gates

**Part of:** Epic 11F — Fine-Grained RBAC  
**Spec:** `docs/superpowers/specs/2026-07-02-epic11f-rbac-design.md`  
**Depends on:** Task 3 (usePermissions, useHasPermission, PermissionKeys must exist)

## Files

**Create:**
- `src/MSOSync.Frontend/src/features/auth/PermissionDeniedPage.tsx`
- `src/MSOSync.Frontend/src/features/auth/PermissionGuard.tsx`

**Modify:**
- `src/MSOSync.Frontend/src/app/router.tsx` — wrap gated routes with `<PermissionGuard>`
- `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — sidebar permission gates
- `src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx` — add `canExport` prop
- `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx` — APPROVE_NODES gate on approve action
- `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx` — RETRY_BATCHES gate
- `src/MSOSync.Frontend/src/features/users/UsersPage.tsx` — MANAGE_USERS gate on CRUD actions
- `src/MSOSync.Frontend/src/features/locks/LocksPage.tsx` + `LocksGrid.tsx` — RELEASE_LOCKS gate (check both files; action may be in the grid)
- `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx` — EDIT_PARAMETERS gate on edit

**Also gate ExportMenu on these pages** (add `canExport` prop):
- `src/MSOSync.Frontend/src/features/events/EventsPage.tsx`
- `src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx`
- `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`
- `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`
- `src/MSOSync.Frontend/src/features/users/UsersPage.tsx`

## Interfaces Consumed (from Task 3)

```typescript
import { usePermissions, useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';
// adjust relative paths per file location
```

## Interfaces Produced (consumed by Task 5)

```typescript
// PermissionGuard — used in router.tsx for protected routes
<PermissionGuard permissionKey="MANAGE_USERS">
  <SomePage />
</PermissionGuard>
```

---

## Global Constraints

- TypeScript `erasableSyntaxOnly = true` — no `enum`
- All imports relative — no `@/`
- No new npm packages — no shadcn Tooltip (use native `title` attribute on wrapper `<span>`)
- Read every file before modifying — current structure matters
- Sidebar items: **hide** when user lacks permission (do not disable)
- Operational actions: **disable + native tooltip** via `<span title="..."><Button disabled>...</Button></span>` — never hide
- Route guards: render `<PermissionDeniedPage />` inline — do NOT redirect

---

## Action Gate Pattern

For disabled operational actions (not route guards or sidebar), use this pattern throughout:

```tsx
// canAction: boolean from useHasPermission(...)
{canAction ? (
  <Button onClick={handler}>Label</Button>
) : (
  <span title="You don't have permission to perform this action">
    <Button disabled>Label</Button>
  </span>
)}
```

A disabled HTML `<button>` doesn't fire hover events, so the `title` must be on the wrapping `<span>`.

---

- [ ] **Step 1: Create `src/MSOSync.Frontend/src/features/auth/PermissionDeniedPage.tsx`**

```tsx
import { ShieldOff } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { useNavigate } from 'react-router-dom';

export function PermissionDeniedPage() {
  const navigate = useNavigate();
  return (
    <div className="flex flex-col items-center justify-center h-full gap-4 p-6 text-center">
      <ShieldOff className="h-12 w-12 text-neutral-400" />
      <h2 className="text-xl font-semibold">Access Denied</h2>
      <p className="text-sm text-neutral-500 max-w-sm">
        You don't have permission to view this page. Contact your administrator to request access.
      </p>
      <Button variant="outline" onClick={() => navigate(-1)}>Go Back</Button>
    </div>
  );
}
```

- [ ] **Step 2: Create `src/MSOSync.Frontend/src/features/auth/PermissionGuard.tsx`**

```tsx
import type { ReactNode } from 'react';
import { usePermissions, useHasPermission } from '../../shared/hooks/usePermissions';
import type { PermissionKey } from '../../shared/types/permissions';
import { PermissionDeniedPage } from './PermissionDeniedPage';

interface Props {
  permissionKey: PermissionKey;
  children: ReactNode;
}

export function PermissionGuard({ permissionKey, children }: Props) {
  const { isLoading } = usePermissions();
  const can = useHasPermission(permissionKey);

  if (isLoading) return null;
  if (!can) return <PermissionDeniedPage />;
  return <>{children}</>;
}
```

Note: returning `null` while loading is acceptable because `AppLayout` already calls `usePermissions()` (from Task 3), which means by the time a child route renders, the data is almost certainly cached. A blank flash is tolerable; a redirect is not.

- [ ] **Step 3: Update `src/MSOSync.Frontend/src/app/router.tsx` — add PermissionGuard to gated routes**

Read `src/MSOSync.Frontend/src/app/router.tsx` first. Add these imports at the top:

```tsx
import { PermissionGuard } from '../features/auth/PermissionGuard';
import { PermissionKeys } from '../shared/types/permissions';
```

Wrap the following routes with `<PermissionGuard>`. Find each `{ path: '...', element: <...Page /> }` entry and replace:

**topology:**
```tsx
{ path: 'topology', element: <PermissionGuard permissionKey={PermissionKeys.ViewTopology}><TopologyPage /></PermissionGuard> },
```

**metrics:**
```tsx
{ path: 'metrics', element: <PermissionGuard permissionKey={PermissionKeys.ViewMetrics}><MetricsPage /></PermissionGuard> },
```

**audit:**
```tsx
{ path: 'audit', element: <PermissionGuard permissionKey={PermissionKeys.ViewAudit}><AuditPage /></PermissionGuard> },
```

**users:**
```tsx
{ path: 'users', element: <PermissionGuard permissionKey={PermissionKeys.ManageUsers}><UsersPage /></PermissionGuard> },
```

Leave all other routes (dashboard, events, nodes, parameters, locks, channels, triggers, routers, profile, etc.) unwrapped.

- [ ] **Step 4: Update `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx` — sidebar permission gates**

Read `AppLayout.tsx` fully before editing. The `NavItem` type and `NAV_GROUPS` array must be updated.

**Change 1** — add import for `useHasPermission` and `PermissionKeys`:

After the line `import { usePermissions } from '../../shared/hooks/usePermissions';` (added in Task 3), add:

```tsx
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';
import type { PermissionKey } from '../../shared/types/permissions';
```

**Change 2** — add `requiredPermission` field to `NavItem` type:

Find:
```tsx
type NavItem = { label: string; path: string; icon: React.ElementType };
```
Replace with:
```tsx
type NavItem = { label: string; path: string; icon: React.ElementType; requiredPermission?: PermissionKey };
```

**Change 3** — add `requiredPermission` to gated nav items in `NAV_GROUPS`. Find the existing `NAV_GROUPS` constant and update these specific entries (leave all others unchanged):

In the Operational group, update Metrics:
```tsx
{ label: 'Metrics', path: '/metrics', icon: BarChart2, requiredPermission: PermissionKeys.ViewMetrics },
```

In the Topology group, update Topology:
```tsx
{ label: 'Topology', path: '/topology', icon: Network, requiredPermission: PermissionKeys.ViewTopology },
```

In the Administration group, update Audit and Users:
```tsx
{ label: 'Users',      path: '/users',      icon: Users,     requiredPermission: PermissionKeys.ManageUsers },
{ label: 'Parameters', path: '/parameters', icon: Settings },
{ label: 'Audit',      path: '/audit',      icon: FileText,  requiredPermission: PermissionKeys.ViewAudit },
{ label: 'Locks',      path: '/locks',      icon: Lock },
```

**Change 4** — filter items in `NavGroup` component. The `NavGroup` component currently renders all `items` directly. Update it to call `useHasPermission` and filter:

Find:
```tsx
function NavGroup({ heading, items }: { heading: string; items: NavItem[] }) {
  return (
    <div className="flex flex-col gap-1">
      <p className="px-3 text-xs font-semibold uppercase tracking-wider text-neutral-500 dark:text-neutral-400 mb-1">
        {heading}
      </p>
      {items.map(({ label, path, icon: Icon }) => (
```

Replace with:
```tsx
function NavGroup({ heading, items }: { heading: string; items: NavItem[] }) {
  const canViewMetrics  = useHasPermission(PermissionKeys.ViewMetrics);
  const canViewTopology = useHasPermission(PermissionKeys.ViewTopology);
  const canViewAudit    = useHasPermission(PermissionKeys.ViewAudit);
  const canManageUsers  = useHasPermission(PermissionKeys.ManageUsers);

  const permMap: Record<PermissionKey, boolean> = {
    [PermissionKeys.ViewMetrics]:    canViewMetrics,
    [PermissionKeys.ViewTopology]:   canViewTopology,
    [PermissionKeys.ViewAudit]:      canViewAudit,
    [PermissionKeys.ManageUsers]:    canManageUsers,
    [PermissionKeys.ViewEvents]:     true,
    [PermissionKeys.ExportData]:     true,
    [PermissionKeys.RetryBatches]:   true,
    [PermissionKeys.ApproveNodes]:   true,
    [PermissionKeys.ReleaseLocks]:   true,
    [PermissionKeys.EditParameters]: true,
    [PermissionKeys.ManageTriggers]: true,
    [PermissionKeys.ManageRouters]:  true,
  };

  const visibleItems = items.filter(
    item => !item.requiredPermission || permMap[item.requiredPermission],
  );

  if (visibleItems.length === 0) return null;

  return (
    <div className="flex flex-col gap-1">
      <p className="px-3 text-xs font-semibold uppercase tracking-wider text-neutral-500 dark:text-neutral-400 mb-1">
        {heading}
      </p>
      {visibleItems.map(({ label, path, icon: Icon }) => (
```

The closing of the JSX stays the same — just change `items.map` to `visibleItems.map` and close the new JSX block correctly. The rest of the NavGroup return body is unchanged.

- [ ] **Step 5: Update `src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx` — add `canExport` prop**

Read `src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx` fully.

**Change 1** — add `canExport?: boolean` to `ExportMenuProps`:

Find:
```tsx
interface ExportMenuProps {
  resource: ExportResource;
  currentData: Record<string, unknown>[];
  queryParams: Record<string, string | number | boolean | undefined>;
  supportsAllRows?: boolean;
}
```
Replace with:
```tsx
interface ExportMenuProps {
  resource: ExportResource;
  currentData: Record<string, unknown>[];
  queryParams: Record<string, string | number | boolean | undefined>;
  supportsAllRows?: boolean;
  canExport?: boolean;
}
```

**Change 2** — destructure `canExport` with default `true`, and add early-return for the disabled state. Find:

```tsx
export function ExportMenu({
  resource,
  currentData,
  queryParams,
  supportsAllRows = true,
}: ExportMenuProps) {
  const {
```
Replace with:
```tsx
export function ExportMenu({
  resource,
  currentData,
  queryParams,
  supportsAllRows = true,
  canExport = true,
}: ExportMenuProps) {
  if (!canExport) {
    return (
      <span title="You don't have permission to export data">
        <Button variant="outline" size="sm" disabled>
          <Download className="mr-2 h-4 w-4" />
          Export
        </Button>
      </span>
    );
  }

  const {
```

- [ ] **Step 6: Add `canExport` to ExportMenu call sites**

For each page below, read the file, add the `useHasPermission` import, call `useHasPermission(PermissionKeys.ExportData)`, and pass the result as `canExport` to `ExportMenu`.

**EventsPage** (`src/MSOSync.Frontend/src/features/events/EventsPage.tsx`):

Add import:
```tsx
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';
```

In component body (after existing hooks):
```tsx
const canExport = useHasPermission(PermissionKeys.ExportData);
```

Pass to ExportMenu (find the ExportMenu JSX and add the prop):
```tsx
<ExportMenu
  resource="events"
  currentData={...}
  queryParams={...}
  canExport={canExport}
/>
```

**IncomingBatchesPage** (`src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx`):

Same pattern: add imports, `const canExport = useHasPermission(PermissionKeys.ExportData)`, pass `canExport={canExport}` to ExportMenu.

**OutgoingBatchesPage** (`src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx`):

Same pattern for ExportMenu. Also gate the "Retry All" button (Step 7).

**AuditPage** (`src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`):

Same pattern.

**NodesPage** (`src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`):

Same pattern. Also gate approve action (Step 8).

**UsersPage** (`src/MSOSync.Frontend/src/features/users/UsersPage.tsx`):

Same pattern. Also gate CRUD actions (Step 9).

- [ ] **Step 7: Gate "Retry All" in `OutgoingBatchesPage.tsx`**

Read `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx` fully.

In addition to `canExport`, add:
```tsx
const canRetry = useHasPermission(PermissionKeys.RetryBatches);
```

Find the "Retry All" button:
```tsx
          <Button
            variant="outline"
            onClick={() => void retryAllMutation.mutateAsync()}
            disabled={retryAllMutation.isPending}
          >
            {retryAllMutation.isPending ? 'Retrying…' : 'Retry All'}
          </Button>
```
Replace with:
```tsx
          {canRetry ? (
            <Button
              variant="outline"
              onClick={() => void retryAllMutation.mutateAsync()}
              disabled={retryAllMutation.isPending}
            >
              {retryAllMutation.isPending ? 'Retrying…' : 'Retry All'}
            </Button>
          ) : (
            <span title="You don't have permission to retry batches">
              <Button variant="outline" disabled>Retry All</Button>
            </span>
          )}
```

Note: the per-row retry button in `OutgoingBatchesGrid` is action-column based. Read `src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesGrid.tsx` to find where the retry action is triggered per row (likely in a column factory). Pass `canRetry` as a prop or check permission inside the grid. If the grid uses an `onRetry` callback prop, add a `canRetry: boolean` prop to the grid and disable the action button there with the same `<span title="...">` pattern.

- [ ] **Step 8: Gate "Approve" in `NodesPage.tsx`**

Read `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx` fully.

Currently the page uses `const isAdmin = user?.roles.includes('Admin') ?? false` (from `useAuth`) to show the "Add Node" button. Replace this coarse check with permission-specific checks:

Remove: `const { user } = useAuth();` and `const isAdmin = user?.roles.includes('Admin') ?? false;`

Add:
```tsx
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

// inside component:
const canExport   = useHasPermission(PermissionKeys.ExportData);
const canApprove  = useHasPermission(PermissionKeys.ApproveNodes);
const canManage   = useHasPermission(PermissionKeys.ManageUsers);
```

Replace `{isAdmin && (<Button onClick={() => setCreateOpen(true)}>Add Node</Button>)}` with:
```tsx
{canManage && (
  <Button onClick={() => setCreateOpen(true)}>Add Node</Button>
)}
```

The approve action is triggered via the grid columns. Look in `src/MSOSync.Frontend/src/features/nodes/NodesGrid.tsx` (or its column factory) for where the "Approve" action button is rendered and add a `canApprove: boolean` prop — when `false`, render the approve button disabled with `<span title="You don't have permission to approve nodes"><Button disabled>...</Button></span>`.

Pass `canApprove={canApprove}` from NodesPage to NodesGrid.

- [ ] **Step 9: Gate CRUD actions in `UsersPage.tsx`**

Read `src/MSOSync.Frontend/src/features/users/UsersPage.tsx` fully.

Add:
```tsx
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

const canExport       = useHasPermission(PermissionKeys.ExportData);
const canManageUsers  = useHasPermission(PermissionKeys.ManageUsers);
```

The page renders a "Create" button (or similar) and an edit/deactivate action. Gate those:
- "Add User" / "Create" button: wrap with `{canManageUsers && (<Button...>)}` — hide when lacking permission (sidebar already hides the Users route)
- The UsersGrid likely passes `onEdit` and `onDeactivate` callbacks. Pass `canManageUsers` to the grid so it can hide or disable action columns.

Look at `src/MSOSync.Frontend/src/features/users/UsersGrid.tsx` to find where action buttons are rendered and add a `canManage: boolean` prop.

- [ ] **Step 10: Gate release-lock action in `LocksPage.tsx` / `LocksGrid.tsx`**

Read `src/MSOSync.Frontend/src/features/locks/LocksPage.tsx` fully. It currently renders just `<LocksGrid />`. The release lock action is inside `LocksGrid.tsx`.

Read `src/MSOSync.Frontend/src/features/locks/LocksGrid.tsx` to find where the "Release" button/action is rendered.

In LocksPage, add:
```tsx
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

const canRelease = useHasPermission(PermissionKeys.ReleaseLocks);
```

Pass `canRelease={canRelease}` to `<LocksGrid canRelease={canRelease} />`.

In LocksGrid, accept the `canRelease: boolean` prop and when `false`, render the release button disabled:
```tsx
{canRelease ? (
  <Button size="sm" variant="outline" onClick={handleRelease}>Release</Button>
) : (
  <span title="You don't have permission to release locks">
    <Button size="sm" variant="outline" disabled>Release</Button>
  </span>
)}
```

- [ ] **Step 11: Gate edit action in `ParametersPage.tsx`**

Read `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx` fully.

Add:
```tsx
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/preferences';  // NOTE: this is PermissionKeys from permissions.ts

const canEdit = useHasPermission(PermissionKeys.EditParameters);
```

The page has an edit button or edit dialog trigger. Find where the edit action is surfaced (may be in a ParametersGrid). Gate it with `canEdit`:
- If it's a button in the page toolbar: wrap with canEdit check (disabled+tooltip if false)
- If it's in a grid column: pass `canEdit` as prop to the grid, disable the edit cell renderer when false

- [ ] **Step 12: Build check**

```pwsh
cd src/MSOSync.Frontend
npm run build 2>&1 | Select-Object -Last 20
```

Expected: 0 TypeScript errors. Fix type errors before proceeding. Common issues:
- `PermissionKey` not imported where needed
- NavGroup's `permMap` may need adjustments if not all PermissionKeys are used as keys

- [ ] **Step 13: Commit**

```pwsh
git add `
  src/MSOSync.Frontend/src/features/auth/PermissionDeniedPage.tsx `
  src/MSOSync.Frontend/src/features/auth/PermissionGuard.tsx `
  src/MSOSync.Frontend/src/app/router.tsx `
  src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx `
  src/MSOSync.Frontend/src/shared/components/ExportMenu.tsx `
  src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx `
  src/MSOSync.Frontend/src/features/nodes/NodesGrid.tsx `
  src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesPage.tsx `
  src/MSOSync.Frontend/src/features/outgoing-batches/OutgoingBatchesGrid.tsx `
  src/MSOSync.Frontend/src/features/users/UsersPage.tsx `
  src/MSOSync.Frontend/src/features/users/UsersGrid.tsx `
  src/MSOSync.Frontend/src/features/locks/LocksPage.tsx `
  src/MSOSync.Frontend/src/features/locks/LocksGrid.tsx `
  src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx `
  src/MSOSync.Frontend/src/features/events/EventsPage.tsx `
  src/MSOSync.Frontend/src/features/incoming-batches/IncomingBatchesPage.tsx `
  src/MSOSync.Frontend/src/features/audit/AuditPage.tsx

git commit -m "feat(11f): add PermissionDeniedPage + PermissionGuard + route gates + sidebar gates + action disabling"
```

## Status Report Format

Return:
```
Status: DONE
Commits: <sha>
Build: <result>
Concerns: <none or list>
```
