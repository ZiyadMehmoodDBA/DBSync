import { useState, useRef, useEffect } from 'react';
import { NavLink, Outlet, useNavigate } from 'react-router-dom';
import {
  LayoutDashboard,
  Network,
  Server,
  GitBranch,
  Activity,
  AlertTriangle,
  BarChart2,
  Users,
  FileText,
  ShieldCheck,
  Sun,
  Moon,
  LogOut,
  Cpu,
  Settings2,
  Briefcase,
  HeartPulse,
  Flag,
  SlidersHorizontal,
  Archive,
  Stethoscope,
  PieChart,
  Package,
  Monitor,
  Calendar,
  TrendingUp,
  ShieldAlert,
  Gauge,
} from 'lucide-react';
import { Button } from '../../components/ui/button';
import { Separator } from '../../components/ui/separator';
import { Avatar, AvatarFallback } from '../../components/ui/avatar';
import { useAuth } from '../../features/auth/useAuth';
import { cn } from '../../lib/utils';
import { useSignalRContext } from '../../shared/signalr/context';
import { usePreferences, usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { usePermissions, useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';
import type { PermissionKey } from '../../shared/types/permissions';
import { PreferenceKeys } from '../../shared/types/preferences';
import type { Theme } from '../../shared/types/preferences';
import { NotificationBell } from '../../features/notifications/NotificationBell';

type NavItem = { label: string; path: string; icon: React.ElementType; requiredPermission?: PermissionKey };

const NAV_GROUPS: { heading: string | null; items: NavItem[] }[] = [
  {
    heading: null,
    items: [
      { label: 'Overview', path: '/overview', icon: LayoutDashboard },
    ],
  },
  {
    heading: 'Operations',
    items: [
      { label: 'Nodes',         path: '/operations/nodes',         icon: Server,      requiredPermission: PermissionKeys.ViewTopology },
      { label: 'Configuration', path: '/operations/configuration', icon: Settings2,   requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Jobs',          path: '/operations/jobs',          icon: Briefcase },
      { label: 'Cluster',        path: '/operations/cluster',              icon: Monitor },
      { label: 'Health Trends', path: '/operations/cluster/health-trends', icon: TrendingUp },
      { label: 'Recovery',     path: '/operations/cluster/recovery',      icon: ShieldAlert },
      { label: 'Diagnostics', path: '/operations/cluster/diagnostics',   icon: Gauge },
      { label: 'Health',        path: '/operations/health',               icon: HeartPulse },
      { label: 'Activity',      path: '/operations/activity',      icon: Activity,    requiredPermission: PermissionKeys.ViewAudit },
      { label: 'Timeline',      path: '/operations/timeline',      icon: Calendar },
    ],
  },
  {
    heading: 'Platform',
    items: [
      { label: 'Node Management', path: '/node-management',            icon: GitBranch,  requiredPermission: PermissionKeys.ViewTopology },
      { label: 'Templates',       path: '/configuration/templates',    icon: Cpu,        requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Assignments',     path: '/configuration/assignments',  icon: Server,     requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Drift',           path: '/configuration/drift',        icon: AlertTriangle, requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Topology',        path: '/topology',                   icon: Network,    requiredPermission: PermissionKeys.ViewTopology },
      { label: 'Metrics',         path: '/metrics',                    icon: BarChart2,  requiredPermission: PermissionKeys.ViewMetrics },
    ],
  },
  {
    heading: 'Administration',
    items: [
      { label: 'Users',         path: '/administration/users',         icon: Users,           requiredPermission: PermissionKeys.ManageUsers },
      { label: 'Roles',         path: '/administration/roles',         icon: ShieldCheck,     requiredPermission: PermissionKeys.ManageUsers },
      { label: 'Feature Flags', path: '/administration/feature-flags', icon: Flag,            requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Settings',      path: '/administration/settings',      icon: SlidersHorizontal, requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Retention',     path: '/administration/retention',     icon: Archive,         requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'License',       path: '/administration/license',       icon: FileText },
      { label: 'Diagnostics',   path: '/administration/diagnostics',   icon: Stethoscope,     requiredPermission: PermissionKeys.ManageConfigurations },
      { label: 'Plugins',       path: '/administration/plugins',       icon: Package,         requiredPermission: PermissionKeys.ManagePlugins },
    ],
  },
  {
    heading: null,
    items: [
      { label: 'Dashboard', path: '/dashboard/summary', icon: PieChart },
    ],
  },
];

function SignalRIndicator() {
  const { connectionState } = useSignalRContext();

  if (connectionState === 'connected') return null;

  const isReconnecting = connectionState === 'reconnecting';

  return (
    <div
      className={cn(
        'flex items-center gap-1.5 rounded-md px-2 py-1 text-xs font-medium',
        isReconnecting
          ? 'text-amber-600 dark:text-amber-400'
          : 'text-red-600 dark:text-red-400',
      )}
      aria-live="polite"
      aria-label={isReconnecting ? 'Reconnecting to server' : 'Disconnected from server'}
    >
      <span
        className={cn(
          'h-2 w-2 rounded-full shrink-0',
          isReconnecting ? 'bg-amber-500' : 'bg-red-500',
        )}
      />
      {isReconnecting ? 'Reconnecting…' : 'Offline'}
    </div>
  );
}

function NavGroup({ heading, items }: { heading: string | null; items: NavItem[] }) {
  const canViewMetrics         = useHasPermission(PermissionKeys.ViewMetrics);
  const canViewTopology        = useHasPermission(PermissionKeys.ViewTopology);
  const canViewAudit           = useHasPermission(PermissionKeys.ViewAudit);
  const canManageUsers         = useHasPermission(PermissionKeys.ManageUsers);
  const canExportData          = useHasPermission(PermissionKeys.ExportData);
  const canManageConfigurations = useHasPermission(PermissionKeys.ManageConfigurations);
  const canManagePlugins        = useHasPermission(PermissionKeys.ManagePlugins);

  const permMap: Record<PermissionKey, boolean> = {
    [PermissionKeys.ViewMetrics]:          canViewMetrics,
    [PermissionKeys.ViewTopology]:         canViewTopology,
    [PermissionKeys.ViewAudit]:            canViewAudit,
    [PermissionKeys.ManageUsers]:          canManageUsers,
    [PermissionKeys.ExportData]:           canExportData,
    [PermissionKeys.ManageConfigurations]: canManageConfigurations,
    [PermissionKeys.ManagePlugins]:        canManagePlugins,
    [PermissionKeys.ViewEvents]:           true,
    [PermissionKeys.RetryBatches]:         true,
    [PermissionKeys.ApproveNodes]:         true,
    [PermissionKeys.ReleaseLocks]:         true,
    [PermissionKeys.EditParameters]:       true,
    [PermissionKeys.ManageTriggers]:       true,
    [PermissionKeys.ManageRouters]:        true,
    [PermissionKeys.ProvisionNodes]:       true,
    [PermissionKeys.ManageNodeLifecycle]:  true,
  };

  const visibleItems = items.filter(
    item => !item.requiredPermission || permMap[item.requiredPermission],
  );

  if (visibleItems.length === 0) return null;

  return (
    <div className="flex flex-col gap-1">
      {heading && (
        <p className="px-3 text-xs font-semibold uppercase tracking-wider text-neutral-500 dark:text-neutral-400 mb-1">
          {heading}
        </p>
      )}
      {visibleItems.map(({ label, path, icon: Icon }) => (
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

  // Prefetch preferences and permissions for the whole session
  usePreferences();
  usePermissions();

  // Read saved theme preference; fall back to current localStorage value
  const localTheme = (localStorage.getItem('msosync.theme') as Theme | null) ?? 'light';
  const savedTheme = usePreference<Theme>(PreferenceKeys.theme, localTheme);
  const { mutate: setPref } = useSetPreference();

  const [isDark, setIsDark] = useState<boolean>(localTheme === 'dark');

  // Sync to backend-saved theme once it loads
  const themeApplied = useRef(false);
  useEffect(() => {
    if (!themeApplied.current && savedTheme !== localTheme) {
      const dark = savedTheme === 'dark';
      setIsDark(dark);
      document.documentElement.classList.toggle('dark', dark);
      themeApplied.current = true;
    }
  }, [savedTheme, localTheme]);

  function handleThemeToggle() {
    const next = !isDark;
    document.documentElement.classList.toggle('dark', next);
    localStorage.setItem('msosync.theme', next ? 'dark' : 'light');
    setIsDark(next);
    setPref({ key: PreferenceKeys.theme, value: next ? 'dark' : 'light' });
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
          {NAV_GROUPS.map((g, groupIndex) => (
            <NavGroup key={groupIndex} heading={g.heading} items={g.items} />
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
            <SignalRIndicator />
            {/* Notification bell */}
            <NotificationBell />
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
