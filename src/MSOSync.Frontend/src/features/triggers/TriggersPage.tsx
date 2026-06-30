import { useState, useCallback } from 'react';
import { Input } from '../../components/ui/input';
import { ConfirmDialog } from '../../shared/components/actions';
import { TriggersGrid } from './TriggersGrid';
import {
  useEnableTriggerMutation,
  useDisableTriggerMutation,
  useRebuildTriggerMutation,
} from './mutations';

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

  const enableMutation = useEnableTriggerMutation();
  const disableMutation = useDisableTriggerMutation();
  const rebuildMutation = useRebuildTriggerMutation();

  const onAction = useCallback((triggerId: string, action: ConfirmableAction) => {
    setConfirmState({ triggerId, action });
  }, []);

  const isPending =
    enableMutation.isPending || disableMutation.isPending || rebuildMutation.isPending;

  const handleConfirm = async () => {
    if (!confirmState) return;
    const { triggerId, action } = confirmState;
    if (action === 'enable') await enableMutation.mutateAsync(triggerId);
    else if (action === 'disable') await disableMutation.mutateAsync(triggerId);
    else await rebuildMutation.mutateAsync(triggerId);
    setConfirmState(null);
  };

  const config = confirmState ? CONFIRM_CONFIG[confirmState.action] : null;

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Triggers</h1>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search triggers…"
        className="max-w-xs"
      />
      <TriggersGrid quickFilterText={search} onAction={onAction} />
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
    </div>
  );
}
