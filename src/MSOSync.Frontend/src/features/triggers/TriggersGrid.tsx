import { useCallback, useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeTriggersColumns } from './columns';
import { useTriggers } from './hooks';
import { useVerifyTriggerMutation } from './mutations';
import type { TriggerDto } from '../../shared/types';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

interface Props {
  quickFilterText?: string;
  onAction: (triggerId: string, action: ConfirmableAction) => void;
  onEdit: (trigger: TriggerDto) => void;
  onDelete: (trigger: TriggerDto) => void;
}

export function TriggersGrid({ quickFilterText, onAction, onEdit, onDelete }: Props) {
  const { data, isLoading, error, refetch } = useTriggers();
  const verifyMutation = useVerifyTriggerMutation();

  const onVerify = useCallback(
    (triggerId: string) => {
      void verifyMutation.mutateAsync(triggerId);
    },
    [verifyMutation],
  );

  const columns = useMemo(
    () => makeTriggersColumns(onAction, onVerify, onEdit, onDelete),
    [onAction, onVerify, onEdit, onDelete],
  );

  return (
    <DataGrid
      rowData={data}
      columnDefs={columns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
