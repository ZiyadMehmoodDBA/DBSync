import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { Button } from '../../components/ui/button';
import { ConfirmDialog } from '../../shared/components/actions';
import { TriggersGrid } from './TriggersGrid';
import { TriggerDialog } from './TriggerDialog';
import {
  useEnableTriggerMutation,
  useDisableTriggerMutation,
  useRebuildTriggerMutation,
  useDeleteTriggerMutation,
} from './mutations';
import { toast } from 'sonner';
import { getErrorMessage } from '../../shared/utils/error';
import type { TriggerDto } from '../../shared/types';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

interface ConfirmState {
  triggerId: string;
  action: ConfirmableAction;
}

const CONFIRM_CONFIG: Record<
  ConfirmableAction,
  {
    title: string;
    description: (triggerId: string) => string;
    confirmLabel: string;
    variant: 'default' | 'destructive';
  }
> = {
  enable: {
    title: 'Enable Trigger',
    description: (id) => `Enable trigger "${id}"?`,
    confirmLabel: 'Enable',
    variant: 'default',
  },
  disable: {
    title: 'Disable Trigger',
    description: (id) => `Disable trigger "${id}"?`,
    confirmLabel: 'Disable',
    variant: 'destructive',
  },
  rebuild: {
    title: 'Rebuild Trigger',
    description: () => 'This will drop and recreate the database trigger. Are you sure?',
    confirmLabel: 'Rebuild',
    variant: 'destructive',
  },
};

export function TriggersPage() {
  const [search, setSearch] = useState('');
  const [confirmState, setConfirmState] = useState<ConfirmState | null>(null);
  const [createOpen, setCreateOpen] = useState(false);
  const [editState, setEditState] = useState<TriggerDto | null>(null);
  const [deleteState, setDeleteState] = useState<TriggerDto | null>(null);

  const enableMutation = useEnableTriggerMutation();
  const disableMutation = useDisableTriggerMutation();
  const rebuildMutation = useRebuildTriggerMutation();
  const deleteMutation = useDeleteTriggerMutation();

  const onAction = useCallback((triggerId: string, action: ConfirmableAction) => {
    setConfirmState({ triggerId, action });
  }, []);

  const onEdit = useCallback((trigger: TriggerDto) => { setEditState(trigger); }, []);
  const onDelete = useCallback((trigger: TriggerDto) => { setDeleteState(trigger); }, []);

  const isPending =
    enableMutation.isPending || disableMutation.isPending || rebuildMutation.isPending;

  const handleConfirm = async () => {
    if (!confirmState) return;
    const { triggerId, action } = confirmState;
    try {
      if (action === 'enable') await enableMutation.mutateAsync(triggerId);
      else if (action === 'disable') await disableMutation.mutateAsync(triggerId);
      else await rebuildMutation.mutateAsync(triggerId);
    } finally {
      setConfirmState(null);
    }
  };

  const handleDeleteConfirm = async () => {
    if (!deleteState) return;
    try {
      await deleteMutation.mutateAsync(deleteState.triggerId);
      toast.success(`Trigger "${deleteState.triggerId}" deleted`);
    } catch (error) {
      toast.error(getErrorMessage(error));
    } finally {
      setDeleteState(null);
    }
  };

  const config = confirmState ? CONFIRM_CONFIG[confirmState.action] : null;

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Triggers</h1>
        <Button onClick={() => setCreateOpen(true)}>Add Trigger</Button>
      </div>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search triggers…"
        className="max-w-xs"
      />
      <TriggersGrid
        quickFilterText={search}
        onAction={onAction}
        onEdit={onEdit}
        onDelete={onDelete}
      />
      <TriggerDialog
        open={createOpen}
        mode="create"
        onOpenChange={setCreateOpen}
      />
      {editState && (
        <TriggerDialog
          open={!!editState}
          mode="edit"
          initialValues={editState}
          onOpenChange={(open) => { if (!open) setEditState(null); }}
        />
      )}
      {config && confirmState && (
        <ConfirmDialog
          open
          title={config.title}
          description={config.description(confirmState.triggerId)}
          confirmLabel={config.confirmLabel}
          variant={config.variant}
          loading={isPending}
          onConfirm={() => void handleConfirm()}
          onOpenChange={(open) => {
            if (!open) setConfirmState(null);
          }}
        />
      )}
      {deleteState && (
        <ConfirmDialog
          open
          title="Delete Trigger"
          description={`Delete trigger "${deleteState.triggerId}"? This will drop the database trigger. This cannot be undone.`}
          confirmLabel="Delete"
          variant="destructive"
          loading={deleteMutation.isPending}
          onConfirm={() => void handleDeleteConfirm()}
          onOpenChange={(open) => { if (!open) setDeleteState(null); }}
        />
      )}
    </div>
  );
}
