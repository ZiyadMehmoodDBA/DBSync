# Task 12: Navigation Restructure — Routes, Sidebar, Role-Based Redirect, Legacy Redirects

**Epic:** 12C System Administration Center
**Depends on:** Task 11 (backend routes exist), existing router.tsx and AppLayout.tsx
**Blocks:** Tasks 13–17 (all new pages need routes registered)

---

## Goal

Restructure `router.tsx` and `AppLayout.tsx` to implement the Operations Center shell navigation. Add a role-based redirect component. Register all new feature routes. Wire legacy redirect routes so existing bookmarks keep working.

---

## Step 1 — Read current router.tsx

- [ ] Open `src/MSOSync.Frontend/src/app/router.tsx` and read the full file. Note every existing route path and which component it imports.

---

## Step 2 — Read current AppLayout.tsx

- [ ] Open `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`. Note the existing `NAV_GROUPS` array, all icon imports at the top, and the rendering logic for nav items with headings.

---

## Step 3 — Read RootInitializer.tsx

- [ ] Open `src/MSOSync.Frontend/src/features/auth/RootInitializer.tsx`. Note how `useAuth()` is called and how the redirect currently works.

---

## Step 4 — Read permissions.ts

- [ ] Open `src/MSOSync.Frontend/src/shared/types/permissions.ts`. Verify `PermissionKeys.ManageConfigurations` is exported. If it is missing, add:

```typescript
ManageConfigurations = 'ManageConfigurations',
```

to the `PermissionKeys` enum before proceeding.

---

## Step 5 — Add RoleBasedRedirect component to router.tsx

- [ ] In `router.tsx`, add this component above the `router` constant (import `useAuth` from wherever it is currently imported in other feature files):

```typescript
function RoleBasedRedirect() {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  const isViewerOnly =
    user.roles.includes('VIEWER') &&
    !user.roles.includes('ADMIN') &&
    !user.roles.includes('OPERATOR');
  return <Navigate to={isViewerOnly ? '/dashboard/summary' : '/overview'} replace />;
}
```

---

## Step 6 — Replace the index route in router.tsx

- [ ] Find the line:

```typescript
{ index: true, element: <Navigate to="/dashboard" replace /> },
```

Replace it with:

```typescript
{ index: true, element: <RoleBasedRedirect /> },
```

---

## Step 7 — Add lazy imports for new pages in router.tsx

- [ ] Add the following import statements at the top of `router.tsx` alongside existing page imports. Use the same import style (static or lazy) already used in the file. If the file uses `React.lazy`, use that; otherwise use direct imports:

```typescript
import { OverviewPage } from '@/features/overview/OverviewPage';
import { JobsPage } from '@/features/operations/jobs/JobsPage';
import { HealthPage } from '@/features/operations/health/HealthPage';
import { FeatureFlagsPage } from '@/features/administration/feature-flags/FeatureFlagsPage';
import { SettingsPage } from '@/features/administration/settings/SettingsPage';
import { RetentionPage } from '@/features/administration/retention/RetentionPage';
import { LicensePage } from '@/features/administration/license/LicensePage';
import { DiagnosticsPage } from '@/features/administration/diagnostics/DiagnosticsPage';
```

Note: These files do not exist yet (Tasks 13–17 create them). TypeScript will error until those tasks are done. If you want a zero-error build at this step, create empty placeholder files:

```typescript
// src/MSOSync.Frontend/src/features/overview/OverviewPage.tsx
export function OverviewPage() { return <div>Overview — coming soon</div>; }
```

Do the same for each new page file listed above.

---

## Step 8 — Add new routes inside the AuthGuard/AppLayout children array

- [ ] Inside the `children` array of the `AppLayout` element, add the following routes. Insert them after the existing routes but before the closing bracket:

```typescript
// Overview
{ path: 'overview', element: <OverviewPage /> },

// Operations group
{ path: 'operations/jobs', element: <JobsPage /> },
{ path: 'operations/health', element: <HealthPage /> },
{
  path: 'operations/activity',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ViewAuditLog}>
      <AuditPage />
    </PermissionGuard>
  ),
},

// Dashboard sub-route
{ path: 'dashboard/summary', element: <DashboardPage /> },

// Administration group
{
  path: 'administration/users',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManageUsers}>
      <UsersPage />
    </PermissionGuard>
  ),
},
{
  path: 'administration/roles',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManageRoles}>
      <RolesPage />
    </PermissionGuard>
  ),
},
{
  path: 'administration/feature-flags',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}>
      <FeatureFlagsPage />
    </PermissionGuard>
  ),
},
{
  path: 'administration/settings',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}>
      <SettingsPage />
    </PermissionGuard>
  ),
},
{
  path: 'administration/retention',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}>
      <RetentionPage />
    </PermissionGuard>
  ),
},
{
  path: 'administration/license',
  element: <LicensePage />,
},
{
  path: 'administration/diagnostics',
  element: (
    <PermissionGuard permissionKey={PermissionKeys.ViewSystemHealth}>
      <DiagnosticsPage />
    </PermissionGuard>
  ),
},
```

Note: Replace `PermissionKeys.ViewAuditLog`, `PermissionKeys.ManageUsers`, `PermissionKeys.ManageRoles`, and `PermissionKeys.ViewSystemHealth` with whatever names actually exist in `permissions.ts`. Read the file in Step 4 to confirm exact names.

---

## Step 9 — Add legacy redirect routes

- [ ] Inside the same `AppLayout` children array, add redirect routes that map old paths to new paths:

```typescript
// Legacy redirects — keep bookmarks working
{ path: 'audit', element: <Navigate to="/operations/activity" replace /> },
{ path: 'admin/users', element: <Navigate to="/administration/users" replace /> },
{ path: 'admin/roles', element: <Navigate to="/administration/roles" replace /> },
{ path: 'dashboard', element: <Navigate to="/dashboard/summary" replace /> },
{ path: 'users', element: <Navigate to="/administration/users" replace /> },
{ path: 'parameters', element: <Navigate to="/administration/settings" replace /> },
```

Note: If `/dashboard` already has a route for `DashboardPage`, replace that route definition with the redirect. The real Dashboard is now at `/dashboard/summary`.

---

## Step 10 — Update AppLayout.tsx icon imports

- [ ] At the top of `AppLayout.tsx`, replace the existing lucide-react import block with one that includes all icons needed for the new nav. Keep any icons already imported. Add the new ones:

```typescript
import {
  LayoutDashboard,
  Server,
  Settings2,
  Briefcase,
  HeartPulse,
  Activity,
  GitBranch,
  FileCode,
  Network,
  BarChart2,
  Users,
  Shield,
  Flag,
  SlidersHorizontal,
  Archive,
  FileText,
  Stethoscope,
  PieChart,
} from 'lucide-react';
```

---

## Step 11 — Replace NAV_GROUPS in AppLayout.tsx

- [ ] Find the existing `NAV_GROUPS` constant and replace its entire value with:

```typescript
const NAV_GROUPS = [
  {
    heading: null,
    items: [
      { label: 'Overview', path: '/overview', icon: LayoutDashboard },
    ],
  },
  {
    heading: 'Operations',
    items: [
      { label: 'Nodes', path: '/operations/nodes', icon: Server },
      { label: 'Configuration', path: '/operations/configuration', icon: Settings2 },
      { label: 'Jobs', path: '/operations/jobs', icon: Briefcase },
      { label: 'Health', path: '/operations/health', icon: HeartPulse },
      { label: 'Activity', path: '/operations/activity', icon: Activity },
    ],
  },
  {
    heading: 'Platform',
    items: [
      { label: 'Node Management', path: '/node-management', icon: GitBranch },
      { label: 'Configuration', path: '/configuration/templates', icon: FileCode },
      { label: 'Topology', path: '/topology', icon: Network },
      { label: 'Monitoring', path: '/monitoring', icon: BarChart2 },
    ],
  },
  {
    heading: 'Administration',
    items: [
      { label: 'Users', path: '/administration/users', icon: Users },
      { label: 'Roles', path: '/administration/roles', icon: Shield },
      { label: 'Feature Flags', path: '/administration/feature-flags', icon: Flag },
      { label: 'Settings', path: '/administration/settings', icon: SlidersHorizontal },
      { label: 'Retention', path: '/administration/retention', icon: Archive },
      { label: 'License', path: '/administration/license', icon: FileText },
      { label: 'Diagnostics', path: '/administration/diagnostics', icon: Stethoscope },
    ],
  },
  {
    heading: null,
    items: [
      { label: 'Dashboard', path: '/dashboard/summary', icon: PieChart },
    ],
  },
];
```

---

## Step 12 — Update NAV_GROUPS rendering to handle null headings

- [ ] Find the section of `AppLayout.tsx` that renders group headings. It likely looks like:

```typescript
{NAV_GROUPS.map((group) => (
  <div key={group.heading}>
    {group.heading && <p className="...">{group.heading}</p>}
    {group.items.map(...)}
  </div>
))}
```

Change the `key` prop from `group.heading` (which would collide when `null`) to an index-based key:

```typescript
{NAV_GROUPS.map((group, groupIndex) => (
  <div key={groupIndex}>
    {group.heading && (
      <p className="px-3 py-1 text-xs font-semibold uppercase tracking-wider text-muted-foreground">
        {group.heading}
      </p>
    )}
    {group.items.map((item) => (
      // existing NavLink rendering unchanged
    ))}
  </div>
))}
```

---

## Step 13 — Build and check TypeScript errors

- [ ] Run:

```powershell
cd src/MSOSync.Frontend && npm run build 2>&1
```

Expected errors at this step: "Cannot find module" for new page imports (OverviewPage, JobsPage, etc.) — these are expected and will be resolved in Tasks 13–17. If you created placeholder files in Step 7, there should be zero errors.

Unexpected errors to fix now:
- Any error in `router.tsx` or `AppLayout.tsx` syntax
- Any icon name that does not exist in lucide-react (check spelling)
- Any `PermissionKeys` value that does not exist

---

## Step 14 — Verify navigation renders

- [ ] Run the dev server:

```powershell
cd src/MSOSync.Frontend && npm run dev
```

- [ ] Open browser at `http://localhost:5173`. Log in as ADMIN. Verify sidebar shows all 5 groups with correct labels and icons.
- [ ] Log in as VIEWER. Verify redirect lands at `/dashboard/summary`.
- [ ] Navigate to old URL `/audit`. Verify browser URL changes to `/operations/activity`.
- [ ] Navigate to old URL `/parameters`. Verify browser URL changes to `/administration/settings`.

---

## Step 15 — Commit

- [ ] Stage only the modified files:

```powershell
git add src/MSOSync.Frontend/src/app/router.tsx
git add src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx
git add src/MSOSync.Frontend/src/features/auth/RootInitializer.tsx
git add src/MSOSync.Frontend/src/shared/types/permissions.ts
# Also stage any placeholder page files created in Step 7
```

- [ ] Commit:

```powershell
git commit -m "feat(12C-12): nav restructure, role-based redirect, legacy redirects"
```
