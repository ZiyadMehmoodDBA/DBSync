import { useState } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
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
  User,
  Sun,
  Moon,
  LogOut,
} from 'lucide-react';
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

export function AppLayout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();

  const [isDark, setIsDark] = useState(() =>
    document.documentElement.classList.contains('dark')
  );

  function handleThemeToggle() {
    const next = !isDark;
    document.documentElement.classList.toggle('dark', next);
    localStorage.setItem('msosync.theme', next ? 'dark' : 'light');
    setIsDark(next);
  }

  const handleLogout = async () => {
    await logout();
    navigate('/login', { replace: true });
  };

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
            <Button variant="ghost" size="icon" onClick={handleThemeToggle} aria-label="Toggle theme">
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
