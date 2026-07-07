import { useState } from 'react';
import { useHasPermission } from '../../../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../../../shared/types/permissions';
import { NodesGrid } from './NodesGrid';

export function NodesTab() {
  const [selectedNodeId, setSelectedNodeId] = useState<string | null>(null);
  // Gate: child NodesGrid also checks internally; this is for the outer layout.
  const _canManage = useHasPermission(PermissionKeys.ManageNodeLifecycle);
  void _canManage; // consumed within NodesGrid

  return (
    <div className="flex flex-col h-full overflow-hidden">
      <NodesGrid selectedNodeId={selectedNodeId} onSelectNode={setSelectedNodeId} />
    </div>
  );
}
