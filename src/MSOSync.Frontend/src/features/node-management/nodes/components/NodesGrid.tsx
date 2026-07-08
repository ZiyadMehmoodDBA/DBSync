import { useState, useMemo, useCallback } from 'react';
import { useQuery } from '@tanstack/react-query';
import type { ICellRendererParams, ValueFormatterParams } from 'ag-grid-community';
import { getAllNodes } from '../../../../shared/api/nodes';
import type { NodeDto } from '../../../../shared/types';
import type { TransitionActionDto } from '../../../../shared/types/lifecycle';
import {
  LifecycleBadge, ConnectivityBadge, MaintenanceBadge,
} from '../../../../shared/components/node';
import { DataGrid } from '../../../../shared/components/data-display/DataGrid';
import { ConfirmDialog } from '../../../../shared/components/actions';
import { formatRelativeTime } from '../../../../shared/utils/date';
import { useHasPermission } from '../../../../shared/hooks/usePermissions';
import {
  useEnableNode, useDisableNode, useEndMaintenance, useForceCompleteDecommission,
} from '../../../../shared/hooks/useNodeLifecycle';
import { PermissionKeys } from '../../../../shared/types/permissions';
import { NodeActionsMenu } from './NodeActionsMenu';
import { MaintenanceDialog } from './MaintenanceDialog';
import { DecommissionWizard } from './DecommissionWizard';
import { NodeLifecyclePanel } from './NodeLifecyclePanel';
import { NodeConfigurationTab } from '../../../configuration/NodeConfigurationTab';
import { Tabs, TabsList, TabsTrigger, TabsContent } from '../../../../components/ui/tabs';
import type { LifecycleAction } from '../../../../shared/types/lifecycle';

type DialogState =
  | { kind: 'confirm'; nodeId: string; action: TransitionActionDto }
  | { kind: 'maintenance'; nodeId: string }
  | { kind: 'decommission'; nodeId: string; nodeName: string }
  | null;

// AG Grid React component cell renderer for NodeActionsMenu (hooks-safe)
interface ActionCellProps extends ICellRendererParams<NodeDto> {
  canManage: boolean;
  onAction: (nodeId: string, action: TransitionActionDto) => void;
}

function NodeActionsMenuCell({ data, canManage, onAction }: ActionCellProps) {
  if (!data) return null;
  return (
    <NodeActionsMenu
      nodeId={data.nodeId}
      canManage={canManage}
      onAction={onAction}
    />
  );
}

interface Props {
  selectedNodeId: string | null;
  onSelectNode: (nodeId: string | null) => void;
}

export function NodesGrid({ selectedNodeId, onSelectNode }: Props) {
  const canManage = useHasPermission(PermissionKeys.ManageNodeLifecycle);
  const [includeDecommissioned, setIncludeDecommissioned] = useState(false);
  const [dialog, setDialog] = useState<DialogState>(null);

  const { data: allNodes, isLoading, error, refetch } = useQuery({
    queryKey: ['nodes'],
    queryFn: ({ signal }) => getAllNodes({ signal }),
  });

  const enableMutation = useEnableNode();
  const disableMutation = useDisableNode();
  const endMaintenanceMutation = useEndMaintenance();
  const forceCompleteDecommissionMutation = useForceCompleteDecommission();

  const nodeNameOf = useCallback((nodeId: string) => {
    const n = allNodes?.find(n => n.nodeId === nodeId);
    return n ? (n.nodeName ?? n.nodeId) : nodeId;
  }, [allNodes]);

  const execute = useCallback((nodeId: string, action: LifecycleAction) => {
    switch (action) {
      case 'Enable':     enableMutation.mutate({ nodeId }); break;
      case 'Disable':    disableMutation.mutate({ nodeId }); break;
      case 'EndMaintenance': endMaintenanceMutation.mutate({ nodeId }); break;
      case 'ForceCompleteDecommission': forceCompleteDecommissionMutation.mutate({ nodeId }); break;
    }
  }, [enableMutation, disableMutation, endMaintenanceMutation, forceCompleteDecommissionMutation]);

  const onAction = useCallback((nodeId: string, action: TransitionActionDto) => {
    if (action.action === 'Decommission') {
      setDialog({ kind: 'decommission', nodeId, nodeName: nodeNameOf(nodeId) });
    } else if (action.action === 'StartMaintenance') {
      setDialog({ kind: 'maintenance', nodeId });
    } else if (action.requiresConfirmation) {
      setDialog({ kind: 'confirm', nodeId, action });
    } else {
      execute(nodeId, action.action);
    }
  }, [nodeNameOf, execute]);

  const nodes = useMemo(() => {
    if (!allNodes) return [];
    return includeDecommissioned
      ? allNodes
      : allNodes.filter(n => n.lifecycleState !== 'Decommissioned');
  }, [allNodes, includeDecommissioned]);

  const columnDefs = useMemo(() => [
    { field: 'nodeId' as const,     headerName: 'Node ID',    width: 180 },
    { field: 'groupId' as const,    headerName: 'Group',      width: 120 },
    {
      field: 'lifecycleState' as const, headerName: 'Lifecycle', width: 170,
      cellRenderer: (p: ICellRendererParams<NodeDto>) =>
        p.data ? <LifecycleBadge state={p.data.lifecycleState} /> : null,
    },
    {
      field: 'connectivityStatus' as const, headerName: 'Connectivity', width: 150,
      cellRenderer: (p: ICellRendererParams<NodeDto>) =>
        p.data ? <ConnectivityBadge status={p.data.connectivityStatus} /> : null,
    },
    {
      field: 'maintenanceMode' as const, headerName: 'Maintenance', width: 130,
      cellRenderer: (p: ICellRendererParams<NodeDto>) =>
        p.data ? <MaintenanceBadge active={p.data.maintenanceMode} /> : null,
    },
    {
      field: 'lastHeartbeat' as const, headerName: 'Last Heartbeat', width: 150,
      valueFormatter: (p: ValueFormatterParams<NodeDto>) =>
        (p.value ? formatRelativeTime(p.value as string) : '—'),
    },
    {
      headerName: 'Actions', width: 80, sortable: false,
      cellRenderer: (p: ICellRendererParams<NodeDto>) => (
        <NodeActionsMenuCell {...p} canManage={canManage} onAction={onAction} />
      ),
    },
  ], [canManage, onAction]);

  const isPendingConfirm =
    enableMutation.isPending || disableMutation.isPending ||
    endMaintenanceMutation.isPending || forceCompleteDecommissionMutation.isPending;

  const confirmDialog = dialog?.kind === 'confirm' ? dialog : null;

  return (
    <div className="flex h-full overflow-hidden">
      <div className="flex flex-1 flex-col overflow-hidden">
        <div className="flex items-center gap-3 border-b px-4 py-2 dark:border-neutral-800">
          <label className="flex items-center gap-1.5 text-sm text-neutral-600 dark:text-neutral-400">
            <input
              type="checkbox"
              checked={includeDecommissioned}
              onChange={(e) => setIncludeDecommissioned(e.target.checked)}
            />
            Include decommissioned
          </label>
        </div>
        <div
          className="flex-1 overflow-hidden"
          onClick={(e) => {
            // Row click: only if not clicking action button area
            const target = e.target as HTMLElement;
            if (!target.closest('button') && !target.closest('[role="menu"]')) {
              const row = target.closest('[row-id]') as HTMLElement | null;
              if (row) {
                const rowId = row.getAttribute('row-id');
                if (rowId) onSelectNode(rowId === selectedNodeId ? null : rowId);
              }
            }
          }}
        >
          <DataGrid
            rowData={nodes}
            columnDefs={columnDefs}
            loading={isLoading}
            error={error}
            onRetry={() => void refetch()}
            height={600}
          />
        </div>
      </div>
      {selectedNodeId && (
        <div className="w-96 shrink-0 border-l overflow-y-auto dark:border-neutral-800">
          <div className="flex items-center justify-between border-b px-4 py-2 dark:border-neutral-800">
            <span className="font-medium text-sm">{selectedNodeId}</span>
            <button
              type="button"
              onClick={() => onSelectNode(null)}
              className="text-neutral-400 hover:text-neutral-600 text-xs"
            >
              Close
            </button>
          </div>
          <Tabs defaultValue="lifecycle">
            <TabsList className="mx-4 mt-2">
              <TabsTrigger value="lifecycle">Lifecycle</TabsTrigger>
              <TabsTrigger value="configuration">Configuration</TabsTrigger>
            </TabsList>
            <TabsContent value="lifecycle">
              <NodeLifecyclePanel nodeId={selectedNodeId} />
            </TabsContent>
            <TabsContent value="configuration">
              <NodeConfigurationTab nodeId={selectedNodeId} />
            </TabsContent>
          </Tabs>
        </div>
      )}

      {/* Confirm dialog for simple actions */}
      {confirmDialog && (
        <ConfirmDialog
          open
          title={`${confirmDialog.action.action} Node`}
          description={`Confirm action "${confirmDialog.action.action}" on node "${confirmDialog.nodeId}"?`}
          confirmLabel={confirmDialog.action.action}
          variant={confirmDialog.action.dangerLevel === 'Critical' ? 'destructive' : 'default'}
          loading={isPendingConfirm}
          onConfirm={() => {
            execute(confirmDialog.nodeId, confirmDialog.action.action);
            setDialog(null);
          }}
          onOpenChange={(open) => { if (!open) setDialog(null); }}
        />
      )}

      {/* Maintenance dialog */}
      {dialog?.kind === 'maintenance' && (
        <MaintenanceDialog
          nodeId={dialog.nodeId}
          open
          onOpenChange={(open) => { if (!open) setDialog(null); }}
        />
      )}

      {/* Decommission wizard */}
      {dialog?.kind === 'decommission' && (
        <DecommissionWizard
          nodeId={dialog.nodeId}
          nodeName={dialog.nodeName}
          open
          onOpenChange={(open) => { if (!open) setDialog(null); }}
        />
      )}
    </div>
  );
}
