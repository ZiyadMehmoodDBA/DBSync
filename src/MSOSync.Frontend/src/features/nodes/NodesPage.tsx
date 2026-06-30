import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { ConfirmDialog } from '../../shared/components/actions';
import { NodesGrid } from './NodesGrid';
import {
  useEnableNodeMutation,
  useDisableNodeMutation,
  useApproveRegistrationMutation,
} from './mutations';

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

export function NodesPage() {
  const [search, setSearch] = useState('');
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null);

  const enableMutation = useEnableNodeMutation();
  const disableMutation = useDisableNodeMutation();
  const approveMutation = useApproveRegistrationMutation();

  const onAction = useCallback((nodeId: string, action: NodeAction) => {
    setConfirmState({ nodeId, action });
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

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Nodes</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search nodes…"
        className="max-w-xs"
      />
      <NodesGrid quickFilterText={search} onAction={onAction} />
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
