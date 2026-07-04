import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { NodesGrid } from './NodesGrid';
import { NodeDialog } from './NodeDialog';
import { CreateNodeDialog } from './CreateNodeDialog';
import {
  useEnableNodeMutation,
  useDisableNodeMutation,
  useApproveRegistrationMutation,
} from './mutations';
import { useNodes } from './hooks';
import { ExportMenu } from '../../shared/components/ExportMenu';
import type { NodeDto } from '../../shared/types';
import { usePreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

type NodeAction = 'enable' | 'disable' | 'approve';

interface ConfirmState {
  nodeId: string;
  action: NodeAction;
}

const CONFIRM_CONFIG: Record<
  NodeAction,
  {
    title: string;
    description: (nodeId: string) => string;
    confirmLabel: string;
    variant: 'default' | 'destructive';
  }
> = {
  enable: {
    title: 'Enable Node',
    description: (id) => `Enable node "${id}"? It will resume participating in sync.`,
    confirmLabel: 'Enable',
    variant: 'default',
  },
  disable: {
    title: 'Disable Node',
    description: (id) => `Disable node "${id}"? It will stop participating in sync.`,
    confirmLabel: 'Disable',
    variant: 'destructive',
  },
  approve: {
    title: 'Approve Registration',
    description: (id) => `Approve registration request for node "${id}"?`,
    confirmLabel: 'Approve',
    variant: 'default',
  },
};

const PAGE_SIZE = 50;

export function NodesPage() {
  const [search, setSearch] = useState('');
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null);
  const [editState, setEditState] = useState<NodeDto | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [pageNumber, setPageNumber] = useState(1);

  const savedPageSize = usePreference<number>(PreferenceKeys.nodesPageSize, 25);

  const canExport  = useHasPermission(PermissionKeys.ExportData);
  const canApprove = useHasPermission(PermissionKeys.ApproveNodes);
  const canManage  = useHasPermission(PermissionKeys.ManageUsers);

  const { data: nodesData, isLoading, error, refetch } = useNodes(pageNumber, PAGE_SIZE);
  const nodes = nodesData?.data;
  const totalCount = nodesData?.total ?? 0;

  const enableMutation = useEnableNodeMutation();
  const disableMutation = useDisableNodeMutation();
  const approveMutation = useApproveRegistrationMutation();

  const onAction = useCallback((nodeId: string, action: NodeAction) => {
    setConfirmState({ nodeId, action });
  }, []);

  const onEdit = useCallback((node: NodeDto) => {
    setEditState(node);
  }, []);

  const isPending =
    enableMutation.isPending || disableMutation.isPending || approveMutation.isPending;

  const handleConfirm = async () => {
    if (!confirmState) return;
    const { nodeId, action } = confirmState;
    try {
      if (action === 'enable') await enableMutation.mutateAsync(nodeId);
      else if (action === 'disable') await disableMutation.mutateAsync(nodeId);
      else await approveMutation.mutateAsync(nodeId);
    } finally {
      setConfirmState(null);
    }
  };

  const config = confirmState ? CONFIRM_CONFIG[confirmState.action] : null;

  const rangeStart = totalCount > 0 ? (pageNumber - 1) * PAGE_SIZE + 1 : 0;
  const rangeEnd = Math.min(pageNumber * PAGE_SIZE, totalCount);

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Nodes</h1>
        <ExportMenu
          resource="nodes"
          currentData={(nodes ?? []) as unknown as Record<string, unknown>[]}
          queryParams={{}}
          supportsAllRows={false}
          canExport={canExport}
        />
      </div>
      <div className="flex items-center gap-2">
        <Input
          value={search}
          onChange={(e) => setSearch(e.target.value)}
          placeholder="Search nodes…"
          className="max-w-xs"
        />
        {canManage && (
          <Button onClick={() => setCreateOpen(true)}>Add Node</Button>
        )}
      </div>
      <NodesGrid
        rowData={nodes}
        isLoading={isLoading}
        error={error}
        onRetry={() => void refetch()}
        quickFilterText={search}
        onAction={onAction}
        onEdit={onEdit}
        paginationPageSize={savedPageSize}
        canApprove={canApprove}
      />
      {!isLoading && totalCount > 0 && (
        <div className="flex items-center justify-between px-2 py-3 border-t text-sm text-muted-foreground">
          <span>
            Showing {rangeStart}–{rangeEnd} of {totalCount}
          </span>
          <div className="flex gap-2">
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPageNumber(p => p - 1)}
              disabled={pageNumber === 1}
            >
              Previous
            </Button>
            <Button
              variant="outline"
              size="sm"
              onClick={() => setPageNumber(p => p + 1)}
              disabled={pageNumber * PAGE_SIZE >= totalCount}
            >
              Next
            </Button>
          </div>
        </div>
      )}
      {editState && (
        <NodeDialog
          open={!!editState}
          initialValues={editState}
          onOpenChange={(open) => {
            if (!open) setEditState(null);
          }}
        />
      )}
      <CreateNodeDialog open={createOpen} onOpenChange={setCreateOpen} />
      {config && confirmState && (
        <ConfirmDialog
          open
          title={config.title}
          description={config.description(confirmState.nodeId)}
          confirmLabel={config.confirmLabel}
          variant={config.variant}
          loading={isPending}
          onConfirm={() => void handleConfirm()}
          onOpenChange={(open) => {
            if (!open) setConfirmState(null);
          }}
        />
      )}
    </div>
  );
}
