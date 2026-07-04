import { createBrowserRouter, Navigate } from 'react-router-dom';
import { RootInitializer } from '../features/auth/RootInitializer';
import { AuthGuard } from '../features/auth/AuthGuard';
import { LoginPage } from '../features/auth/LoginPage';
import { PermissionGuard } from '../features/auth/PermissionGuard';
import { AuthLayout } from './layouts/AuthLayout';
import { AppLayout } from './layouts/AppLayout';
import { DashboardPage } from '../features/dashboard/DashboardPage';
import { TopologyPage } from '../features/topology/TopologyPage';
import { NodesPage } from '../features/nodes/NodesPage';
import { ChannelsPage } from '../features/channels/ChannelsPage';
import { TriggersPage } from '../features/triggers/TriggersPage';
import { RoutersPage } from '../features/routers/RoutersPage';
import { EventsPage } from '../features/events/EventsPage';
import { IncomingBatchesPage } from '../features/incoming-batches/IncomingBatchesPage';
import { OutgoingBatchesPage } from '../features/outgoing-batches/OutgoingBatchesPage';
import { BatchErrorsPage } from '../features/batch-errors/BatchErrorsPage';
import { MetricsPage } from '../features/metrics/MetricsPage';
import { UsersPage } from '../features/users/UsersPage';
import { ParametersPage } from '../features/parameters/ParametersPage';
import { AuditPage } from '../features/audit/AuditPage';
import { ProfilePage } from '../features/profile/ProfilePage';
import { LocksPage } from '../features/locks/LocksPage';
import { RolesPage } from '../features/administration/RolesPage';
import { DownloadsPage } from '../features/downloads/DownloadsPage';
import { PermissionKeys } from '../shared/types/permissions';

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
              { path: 'metrics',          element: <PermissionGuard permissionKey={PermissionKeys.ViewMetrics}><MetricsPage /></PermissionGuard> },
              { path: 'topology',         element: <PermissionGuard permissionKey={PermissionKeys.ViewTopology}><TopologyPage /></PermissionGuard> },
              { path: 'nodes',            element: <NodesPage /> },
              { path: 'channels',         element: <ChannelsPage /> },
              { path: 'triggers',         element: <TriggersPage /> },
              { path: 'routers',          element: <RoutersPage /> },
              { path: 'users',            element: <PermissionGuard permissionKey={PermissionKeys.ManageUsers}><UsersPage /></PermissionGuard> },
              { path: 'administration/roles', element: <PermissionGuard permissionKey={PermissionKeys.ManageUsers}><RolesPage /></PermissionGuard> },
              { path: 'parameters',       element: <ParametersPage /> },
              { path: 'audit',            element: <PermissionGuard permissionKey={PermissionKeys.ViewAudit}><AuditPage /></PermissionGuard> },
              { path: 'locks',            element: <LocksPage /> },
              { path: 'downloads',        element: <PermissionGuard permissionKey={PermissionKeys.ExportData}><DownloadsPage /></PermissionGuard> },
              { path: 'profile',          element: <ProfilePage /> },
            ],
          },
        ],
      },
    ],
  },
]);
