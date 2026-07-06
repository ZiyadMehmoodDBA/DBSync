import { lazy, Suspense } from 'react';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';
import { NodeManagementProvider, useNodeManagement } from './NodeManagementProvider';
import { NODE_MANAGEMENT_TABS } from './types/tabs';
import type { TabId } from './types/tabs';
import { cn } from '../../lib/utils';

const OverviewTab = lazy(() =>
  import('./overview/components/OverviewTab').then(m => ({ default: m.OverviewTab })));
const RegistrationsTab = lazy(() =>
  import('./registrations/components/RegistrationsTab').then(m => ({ default: m.RegistrationsTab })));
const ProvisionTab = lazy(() =>
  import('./provision/components/ProvisionTab').then(m => ({ default: m.ProvisionTab })));
const NodesTab = lazy(() =>
  import('./nodes/components/NodesTab').then(m => ({ default: m.NodesTab })));
const GroupsTab = lazy(() =>
  import('./groups/components/GroupsTab').then(m => ({ default: m.GroupsTab })));

function TabBar() {
  const { activeTab, setActiveTab } = useNodeManagement();
  const canViewTopology = useHasPermission(PermissionKeys.ViewTopology);
  const canManageUsers  = useHasPermission(PermissionKeys.ManageUsers);

  const tabs: { id: TabId; label: string; visible: boolean }[] = [
    { id: NODE_MANAGEMENT_TABS.OVERVIEW,      label: 'Overview',      visible: canViewTopology },
    { id: NODE_MANAGEMENT_TABS.REGISTRATIONS, label: 'Registrations', visible: canViewTopology },
    { id: NODE_MANAGEMENT_TABS.PROVISION,     label: 'Provision',     visible: canManageUsers },
    { id: NODE_MANAGEMENT_TABS.NODES,         label: 'Nodes',         visible: canViewTopology },
    { id: NODE_MANAGEMENT_TABS.GROUPS,        label: 'Groups',        visible: canViewTopology },
  ];

  return (
    <div className="flex border-b border-neutral-200 dark:border-neutral-800 px-6">
      {tabs
        .filter(t => t.visible)
        .map(t => (
          <button
            key={t.id}
            onClick={() => setActiveTab(t.id)}
            className={cn(
              'px-4 py-3 text-sm font-medium border-b-2 transition-colors',
              activeTab === t.id
                ? 'border-blue-600 text-blue-600 dark:border-blue-400 dark:text-blue-400'
                : 'border-transparent text-neutral-500 dark:text-neutral-400 hover:text-neutral-700 dark:hover:text-neutral-300',
            )}
          >
            {t.label}
          </button>
        ))}
    </div>
  );
}

function TabContent() {
  const { activeTab } = useNodeManagement();

  return (
    <Suspense fallback={<div className="p-6 text-sm text-neutral-400">Loading…</div>}>
      {activeTab === NODE_MANAGEMENT_TABS.OVERVIEW       && <OverviewTab />}
      {activeTab === NODE_MANAGEMENT_TABS.REGISTRATIONS  && <RegistrationsTab />}
      {activeTab === NODE_MANAGEMENT_TABS.PROVISION      && <ProvisionTab />}
      {activeTab === NODE_MANAGEMENT_TABS.NODES          && <NodesTab />}
      {activeTab === NODE_MANAGEMENT_TABS.GROUPS         && <GroupsTab />}
    </Suspense>
  );
}

export function NodeManagementPage() {
  return (
    <NodeManagementProvider>
      <div className="flex flex-col h-full">
        <div className="px-6 pt-6 pb-2">
          <h1 className="text-xl font-semibold">Node Management</h1>
          <p className="text-sm text-neutral-500 dark:text-neutral-400 mt-1">
            Review registrations, approve nodes, and provision new sync nodes.
          </p>
        </div>
        <TabBar />
        <div className="flex-1 overflow-y-auto">
          <TabContent />
        </div>
      </div>
    </NodeManagementProvider>
  );
}
