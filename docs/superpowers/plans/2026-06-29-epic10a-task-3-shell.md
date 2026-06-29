# Task 3: Router + Layouts + Theme System + Placeholder Pages

**Part of:** [Epic 10A Plan](2026-06-29-epic10a-react-foundation.md)

**Goal:** Wire the full React Router v7 route tree, build `AppLayout` (sidebar + topbar with theme toggle and logout), `AuthLayout` (centered login card wrapper), and create 15 placeholder page components.

**Files:**
- Create: `src/MSOSync.Frontend/src/app/router.tsx`
- Create: `src/MSOSync.Frontend/src/app/providers.tsx`
- Create: `src/MSOSync.Frontend/src/app/layouts/AppLayout.tsx`
- Create: `src/MSOSync.Frontend/src/app/layouts/AuthLayout.tsx`
- Create: `src/MSOSync.Frontend/src/features/dashboard/DashboardPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/topology/TopologyPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/nodes/NodesPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/channels/ChannelsPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/triggers/TriggersPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/routers/RoutersPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/events/EventsPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/batches/IncomingBatchesPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/batches/OutgoingBatchesPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/batches/BatchErrorsPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/metrics/MetricsPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/users/UsersPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/parameters/ParametersPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/audit/AuditPage.tsx`
- Create: `src/MSOSync.Frontend/src/features/profile/ProfilePage.tsx`
- Modify: `src/MSOSync.Frontend/src/main.tsx`
- Modify: `src/MSOSync.Frontend/src/App.tsx`

**Interfaces:**
- Consumes (from Task 2):
  - `AuthProvider` from `features/auth/AuthProvider`
  - `AuthGuard` from `features/auth/AuthGuard`
  - `RootInitializer` from `features/auth/RootInitializer`
  - `LoginPage` from `features/auth/LoginPage`
  - `useAuth()` → `{ user, logout }` for topbar
- Consumes (from Task 1): shadcn `Button`, `Separator`, `Avatar`, `AvatarFallback` from `@/components/ui/*`; `cn` from `@/lib/utils`
- Produces: full working app shell; all 15 authenticated routes render their placeholder

---

- [ ] **Step 1: Create placeholder page factory**

Each placeholder page is identical in structure. Create all 15 now:

`src/features/dashboard/DashboardPage.tsx`:
```tsx
export function DashboardPage() {
  return <PlaceholderPage title="Dashboard" epic="10B" />;
}
```

But first define the reusable `PlaceholderPage` inside `src/shared/components/PlaceholderPage.tsx`:

```tsx
interface Props {
  title: string;
  epic: string;
}

export function PlaceholderPage({ title, epic }: Props) {
  return (
    <div className="flex flex-col gap-2 p-6">
      <h1 className="text-2xl font-semibold">{title}</h1>
      <p className="text-neutral-500 dark:text-neutral-400">
        Coming in Epic {epic}
      </p>
    </div>
  );
}
```

- [ ] **Step 2: Create all 15 placeholder pages**

`src/features/dashboard/DashboardPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function DashboardPage() { return <PlaceholderPage title="Dashboard" epic="10B" />; }
```

`src/features/topology/TopologyPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function TopologyPage() { return <PlaceholderPage title="Topology" epic="10C" />; }
```

`src/features/nodes/NodesPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function NodesPage() { return <PlaceholderPage title="Nodes" epic="10C" />; }
```

`src/features/channels/ChannelsPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function ChannelsPage() { return <PlaceholderPage title="Channels" epic="10C" />; }
```

`src/features/triggers/TriggersPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function TriggersPage() { return <PlaceholderPage title="Triggers" epic="10D" />; }
```

`src/features/routers/RoutersPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function RoutersPage() { return <PlaceholderPage title="Routers" epic="10D" />; }
```

`src/features/events/EventsPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function EventsPage() { return <PlaceholderPage title="Events" epic="10B" />; }
```

`src/features/batches/IncomingBatchesPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function IncomingBatchesPage() { return <PlaceholderPage title="Incoming Batches" epic="10B" />; }
```

`src/features/batches/OutgoingBatchesPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function OutgoingBatchesPage() { return <PlaceholderPage title="Outgoing Batches" epic="10B" />; }
```

`src/features/batches/BatchErrorsPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function BatchErrorsPage() { return <PlaceholderPage title="Batch Errors" epic="10B" />; }
```

`src/features/metrics/MetricsPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function MetricsPage() { return <PlaceholderPage title="Metrics" epic="10C" />; }
```

`src/features/users/UsersPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function UsersPage() { return <PlaceholderPage title="Users" epic="10D" />; }
```

`src/features/parameters/ParametersPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function ParametersPage() { return <PlaceholderPage title="Parameters" epic="10D" />; }
```

`src/features/audit/AuditPage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function AuditPage() { return <PlaceholderPage title="Audit" epic="10D" />; }
```

`src/features/profile/ProfilePage.tsx`:
```tsx
import { PlaceholderPage } from '../../shared/components/PlaceholderPage';
export function ProfilePage() { return <PlaceholderPage title="Profile" epic="10D" />; }
```

- [ ] **Step 3: Create `src/app/layouts/AuthLayout.tsx`**

Centered card wrapper for unauthenticated pages (currently only `/login`):

```tsx
import { Outlet } from 'react-router-dom';

export function AuthLayout() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-neutral-50 dark:bg-neutral-950 p-4">
      <Outlet />
    </div>
  );
}
```

- [ ] **Step 4: Create `src/app/layouts/AppLayout.tsx`**

Full shell with sidebar navigation and topbar.

```tsx
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import { LayoutDashboard, Network, Server, Cable, Zap, GitBranch, Activity, ArrowDownCircle, ArrowUpCircle, AlertTriangle, BarChart2, Users, Settings, FileText, User, Sun, Moon, LogOut } from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Separator } from '../../components/ui/separator';
import { Avatar, AvatarFallback } from '../../components/ui/avatar';
import { useAuth } from '../../features/auth/useAuth';
import { cn } from '../../lib/utils';

type NavItem = { label: string; path: string; icon: React.ElementType };

const NAV_GROUPS: { heading: string; items: NavItem[] }[] = [
  {
    heading: 'Operational',
    items: [
      { label: 'Dashboard',        path: '/dashboard',        icon: LayoutDashboard },
      { label: 'Events',           path: '/events',           icon: Activity },
      { label: 'Incoming Batches', path: '/incoming-batches', icon: ArrowDownCircle },
      { label: 'Outgoing Batches', path: '/outgoing-batches', icon: ArrowUpCircle },
      { label: 'Batch Errors',     path: '/batch-errors',     icon: AlertTriangle },
      { label: 'Metrics',          path: '/metrics',          icon: BarChart2 },
    ],
  },
  {
    heading: 'Topology',
    items: [
      { label: 'Topology',  path: '/topology',  icon: Network },
      { label: 'Nodes',     path: '/nodes',     icon: Server },
      { label: 'Channels',  path: '/channels',  icon: Cable },
      { label: 'Triggers',  path: '/triggers',  icon: Zap },
      { label: 'Routers',   path: '/routers',   icon: GitBranch },
    ],
  },
  {
    heading: 'Administration',
    items: [
      { label: 'Users',      path: '/users',      icon: Users },
      { label: 'Parameters', path: '/parameters', icon: Settings },
      { label: 'Audit',      path: '/audit',      icon: FileText },
    ],
  },
  {
    heading: 'Account',
    items: [
      { label: 'Profile', path: '/profile', icon: User },
    ],
  },
];

function NavGroup({ heading, items }: { heading: string; items: NavItem[] }) {
  return (
    <div className="flex flex-col gap-1">
      <p className="px-3 text-xs font-semibold uppercase tracking-wider text-neutral-500 dark:text-neutral-400 mb-1">
        {heading}
      </p>
      {items.map(({ label, path, icon: Icon }) => (
        <NavLink
          key={path}
          to={path}
          className={({ isActive }) =>
            cn(
              'flex items-center gap-2 rounded-md px-3 py-2 text-sm transition-colors',
              isActive
                ? 'bg-neutral-100 dark:bg-neutral-800 text-neutral-900 dark:text-neutral-100 font-medium'
                : 'text-neutral-600 dark:text-neutral-400 hover:bg-neutral-50 dark:hover:bg-neutral-800/50',
            )
          }
        >
          <Icon className="h-4 w-4 shrink-0" />
          {label}
        </NavLink>
      ))}
    </div>
  );
}

function toggleTheme() {
  const next = document.documentElement.classList.contains('dark') ? 'light' : 'dark';
  document.documentElement.classList.toggle('dark', next === 'dark');
  localStorage.setItem('msosync.theme', next);
}

export function AppLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

  const isDark = document.documentElement.classList.contains('dark');

  return (
    <div className="flex h-screen overflow-hidden bg-white dark:bg-neutral-950">
      {/* Sidebar */}
      <aside className="flex w-60 shrink-0 flex-col border-r border-neutral-200 dark:border-neutral-800 overflow-y-auto">
        {/* Logo */}
        <div className="flex h-14 items-center px-4 font-bold text-lg shrink-0">
          MSOSync
        </div>
        <Separator />
        <nav className="flex flex-col gap-4 p-3 flex-1">
          {NAV_GROUPS.map((g) => (
            <NavGroup key={g.heading} heading={g.heading} items={g.items} />
          ))}
        </nav>
      </aside>

      {/* Main */}
      <div className="flex flex-1 flex-col overflow-hidden">
        {/* Topbar */}
        <header className="flex h-14 shrink-0 items-center justify-between border-b border-neutral-200 dark:border-neutral-800 px-4">
          <span className="text-sm font-medium text-neutral-600 dark:text-neutral-400">
            MSOSync Operations Console
          </span>
          <div className="flex items-center gap-2">
            <Button variant="ghost" size="icon" onClick={toggleTheme} aria-label="Toggle theme">
              {isDark ? <Sun className="h-4 w-4" /> : <Moon className="h-4 w-4" />}
            </Button>
            <Avatar className="h-8 w-8">
              <AvatarFallback className="text-xs">
                {user?.username.slice(0, 2).toUpperCase() ?? '??'}
              </AvatarFallback>
            </Avatar>
            <span className="text-sm text-neutral-700 dark:text-neutral-300">
              {user?.username ?? ''}
            </span>
            <Button variant="ghost" size="icon" onClick={handleLogout} aria-label="Sign out">
              <LogOut className="h-4 w-4" />
            </Button>
          </div>
        </header>

        {/* Page content */}
        <main className="flex-1 overflow-y-auto">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
```

- [ ] **Step 5: Create `src/app/providers.tsx`**

```tsx
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { type ReactNode } from 'react';
import { AuthProvider } from '../features/auth/AuthProvider';

const queryClient = new QueryClient({
  defaultOptions: { queries: { retry: 1, staleTime: 30_000 } },
});

export function Providers({ children }: { children: ReactNode }) {
  return (
    <QueryClientProvider client={queryClient}>
      <AuthProvider>{children}</AuthProvider>
    </QueryClientProvider>
  );
}
```

- [ ] **Step 6: Create `src/app/router.tsx`**

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
              { path: 'profile',          element: <ProfilePage /> },
            ],
          },
        ],
      },
    ],
  },
]);
```

- [ ] **Step 7: Update `src/main.tsx`**

Replace the entire file:

```tsx
import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { RouterProvider } from 'react-router-dom';
import './index.css';
import { router } from './app/router';
import { Providers } from './app/providers';

// Prevent flash of incorrect theme — runs synchronously before React mounts
const savedTheme = localStorage.getItem('msosync.theme') ?? 'light';
document.documentElement.classList.toggle('dark', savedTheme === 'dark');

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <Providers>
      <RouterProvider router={router} />
    </Providers>
  </StrictMode>,
);
```

- [ ] **Step 8: Remove `src/App.tsx`**

`App.tsx` is no longer needed — `main.tsx` now renders `RouterProvider` directly. Delete `src/App.tsx`.

```powershell
Remove-Item src/MSOSync.Frontend/src/App.tsx
```

- [ ] **Step 9: Verify dev server**

```powershell
npm run dev
```

Open `http://localhost:5173`. Expected behavior:
- Redirected to `/login`
- `AuthLayout` centered card visible
- `LoginPage` form renders with username/password fields and "Sign in" button
- Entering wrong credentials shows "Invalid username or password."
- Entering correct credentials (requires `.NET API` running on `:5000`) navigates to `/dashboard`
- Sidebar shows all navigation groups
- Clicking any nav item renders the corresponding placeholder page
- Theme toggle button switches light/dark and persists on reload

- [ ] **Step 10: Verify theme persistence (mandatory acceptance criterion)**

1. Toggle to dark mode
2. Hard-reload the page (`Ctrl+Shift+R`)
3. Expected: page renders dark immediately — NO white flash before dark styles apply

- [ ] **Step 11: Verify production build**

```powershell
npm run build
npm run lint
```

Expected: 0 TypeScript errors, 0 ESLint errors. `dist/` contains `index.html`.

- [ ] **Step 12: Commit**

```powershell
git add src/MSOSync.Frontend/src/app/
git add src/MSOSync.Frontend/src/features/dashboard/
git add src/MSOSync.Frontend/src/features/topology/
git add src/MSOSync.Frontend/src/features/nodes/
git add src/MSOSync.Frontend/src/features/channels/
git add src/MSOSync.Frontend/src/features/triggers/
git add src/MSOSync.Frontend/src/features/routers/
git add src/MSOSync.Frontend/src/features/events/
git add src/MSOSync.Frontend/src/features/batches/
git add src/MSOSync.Frontend/src/features/metrics/
git add src/MSOSync.Frontend/src/features/users/
git add src/MSOSync.Frontend/src/features/parameters/
git add src/MSOSync.Frontend/src/features/audit/
git add src/MSOSync.Frontend/src/features/profile/
git add src/MSOSync.Frontend/src/shared/components/PlaceholderPage.tsx
git add src/MSOSync.Frontend/src/main.tsx
git rm src/MSOSync.Frontend/src/App.tsx
git commit -m "feat(10a): add router, AppLayout, AuthLayout, theme system, 15 placeholder pages"
```
