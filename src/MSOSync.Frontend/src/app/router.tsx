import { createBrowserRouter, Navigate } from 'react-router-dom';
import { RootInitializer } from '../features/auth/RootInitializer';
import { AuthGuard } from '../features/auth/AuthGuard';
import { LoginPage } from '../features/auth/LoginPage';
import { PermissionGuard } from '../features/auth/PermissionGuard';
import { AuthLayout } from './layouts/AuthLayout';
import { AppLayout } from './layouts/AppLayout';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { TopologyPage } from '../features/topology/TopologyPage';
import { ChannelsPage } from '../features/channels/ChannelsPage';
import { TriggersPage } from '../features/triggers/TriggersPage';
import { RoutersPage } from '../features/routers/RoutersPage';
import { EventsPage } from '../features/events/EventsPage';
import { IncomingBatchesPage } from '../features/incoming-batches/IncomingBatchesPage';
import { OutgoingBatchesPage } from '../features/outgoing-batches/OutgoingBatchesPage';
import { BatchErrorsPage } from '../features/batch-errors/BatchErrorsPage';
import { MetricsPage } from '../features/metrics/MetricsPage';
import { UsersPage } from '../features/users/UsersPage';
import { AuditPage } from '../features/audit/AuditPage';
import { ProfilePage } from '../features/profile/ProfilePage';
import { LocksPage } from '../features/locks/LocksPage';
import { RolesPage } from '../features/administration/RolesPage';
import { DownloadsPage } from '../features/downloads/DownloadsPage';
import { NodeManagementPage } from '../features/node-management/NodeManagementPage';
import { NodesPage } from '../features/nodes/NodesPage';
import { TemplatesPage } from '../features/configuration/TemplatesPage';
import { AssignmentsPage } from '../features/configuration/AssignmentsPage';
import { DriftPage } from '../features/configuration/DriftPage';
import { PermissionKeys } from '../shared/types/permissions';
import { useAuth } from '../features/auth/useAuth';
// New pages (Tasks 13–17 will flesh these out)
import { OverviewPage } from '../features/overview/OverviewPage';
import { JobsPage } from '../features/operations/jobs/JobsPage';
import { HealthPage } from '../features/operations/health/HealthPage';
import { FeatureFlagsPage } from '../features/administration/feature-flags/FeatureFlagsPage';
import { SettingsPage } from '../features/administration/settings/SettingsPage';
import { RetentionPage } from '../features/administration/retention/RetentionPage';
import { LicensePage } from '../features/administration/license/LicensePage';
import { DiagnosticsPage } from '../features/administration/diagnostics/DiagnosticsPage';
import { NotificationsPage } from '../features/notifications/NotificationsPage';
import { PluginsPage } from '../features/plugins/PluginsPage';
// ClusterPage: brief called for lazy() + <Suspense>, but ALL other pages in this
// router use eager imports (no lazy() anywhere in this file). Using eager import
// here to match the established pattern. Convert the whole router to lazy-loading
// as a dedicated refactor if code-splitting becomes a priority.
import ClusterPage from '../features/operations/cluster/ClusterPage';
import TimelinePage from '../features/operations/timeline/TimelinePage';

function RoleBasedRedirect() {
  const { user } = useAuth();
  if (!user) return <Navigate to="/login" replace />;
  const isViewerOnly =
    user.roles.includes('VIEWER') &&
    !user.roles.includes('ADMIN') &&
    !user.roles.includes('OPERATOR');
  return <Navigate to={isViewerOnly ? '/dashboard/summary' : '/overview'} replace />;
}

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
              { index: true, element: <RoleBasedRedirect /> },

              // Overview
              { path: 'overview', element: <OverviewPage /> },

              // Operations group
              { path: 'operations/nodes',         element: <PermissionGuard permissionKey={PermissionKeys.ViewTopology}><NodesPage /></PermissionGuard> },
              { path: 'operations/jobs',          element: <JobsPage /> },
              { path: 'operations/cluster',       element: <ClusterPage /> },
              { path: 'operations/health',        element: <HealthPage /> },
              {
                path: 'operations/activity',
                element: (
                  <PermissionGuard permissionKey={PermissionKeys.ViewAudit}>
                    <AuditPage />
                  </PermissionGuard>
                ),
              },
              { path: 'operations/timeline', element: <TimelinePage /> },
              {
                path: 'operations/configuration',
                element: (
                  <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}>
                    <TemplatesPage />
                  </PermissionGuard>
                ),
              },

              // Dashboard sub-route (Viewer landing)
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
                  <PermissionGuard permissionKey={PermissionKeys.ManageUsers}>
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
              { path: 'administration/license', element: <LicensePage /> },
              {
                path: 'administration/diagnostics',
                element: (
                  <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}>
                    <DiagnosticsPage />
                  </PermissionGuard>
                ),
              },
              {
                path: 'administration/plugins',
                element: (
                  <PermissionGuard permissionKey={PermissionKeys.ManagePlugins}>
                    <PluginsPage />
                  </PermissionGuard>
                ),
              },

              // Existing routes (unchanged)
              { path: 'events',           element: <EventsPage /> },
              { path: 'incoming-batches', element: <IncomingBatchesPage /> },
              { path: 'outgoing-batches', element: <OutgoingBatchesPage /> },
              { path: 'batch-errors',     element: <BatchErrorsPage /> },
              { path: 'metrics',          element: <PermissionGuard permissionKey={PermissionKeys.ViewMetrics}><MetricsPage /></PermissionGuard> },
              { path: 'topology',         element: <PermissionGuard permissionKey={PermissionKeys.ViewTopology}><TopologyPage /></PermissionGuard> },
              { path: 'node-management',  element: <PermissionGuard permissionKey={PermissionKeys.ViewTopology}><NodeManagementPage /></PermissionGuard> },
              { path: 'channels',         element: <ChannelsPage /> },
              { path: 'triggers',         element: <TriggersPage /> },
              { path: 'routers',          element: <RoutersPage /> },
              { path: 'users',            element: <Navigate to="/administration/users" replace /> },
              { path: 'parameters',       element: <Navigate to="/administration/settings" replace /> },
              { path: 'locks',            element: <LocksPage /> },
              { path: 'downloads',        element: <PermissionGuard permissionKey={PermissionKeys.ExportData}><DownloadsPage /></PermissionGuard> },
              { path: 'profile',          element: <ProfilePage /> },
              { path: 'notifications',    element: <NotificationsPage /> },
              { path: 'configuration/templates',   element: <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}><TemplatesPage /></PermissionGuard> },
              { path: 'configuration/assignments', element: <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}><AssignmentsPage /></PermissionGuard> },
              { path: 'configuration/drift',       element: <PermissionGuard permissionKey={PermissionKeys.ManageConfigurations}><DriftPage /></PermissionGuard> },

              // Legacy redirects — keep bookmarks working
              { path: 'audit',        element: <Navigate to="/operations/activity" replace /> },
              { path: 'admin/users',  element: <Navigate to="/administration/users" replace /> },
              { path: 'admin/roles',  element: <Navigate to="/administration/roles" replace /> },
              { path: 'dashboard',    element: <Navigate to="/dashboard/summary" replace /> },
              { path: 'nodes',        element: <Navigate to="/operations/nodes" replace /> },
            ],
          },
        ],
      },
    ],
  },
]);
