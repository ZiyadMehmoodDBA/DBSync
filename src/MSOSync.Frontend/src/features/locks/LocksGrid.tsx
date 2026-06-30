import { useState, useCallback, useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { ConfirmDialog } from '../../shared/components/actions';
import { makeLocksColumns } from './columns';
import { useReleaseLockMutation } from './mutations';
import { useLocks } from './hooks';

export function LocksGrid() {
  const { data, isLoading, error, refetch } = useLocks();
  const releaseMutation = useReleaseLockMutation();
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [pendingLockName, setPendingLockName] = useState<string | null>(null);

  const openConfirm = useCallback((lockName: string) => {
    setPendingLockName(lockName);
    setConfirmOpen(true);
  }, []);

  const columns = useMemo(() => makeLocksColumns(openConfirm), [openConfirm]);

  const handleConfirm = async () => {
    if (!pendingLockName) return;
    await releaseMutation.mutateAsync(pendingLockName);
    setConfirmOpen(false);
    setPendingLockName(null);
  };

  return (
    <>
      <DataGrid
        rowData={data}
        columnDefs={columns}
        loading={isLoading}
        error={error}
        onRetry={() => void refetch()}
        height={500}
      />
      <ConfirmDialog
        open={confirmOpen}
        title="Release Lock"
        description={`Release lock "${pendingLockName ?? ''}"? This may affect active processes.`}
        confirmLabel="Release"
        variant="destructive"
        loading={releaseMutation.isPending}
        onConfirm={() => void handleConfirm()}
        onOpenChange={(open) => {
          if (!open) setPendingLockName(null);
          setConfirmOpen(open);
        }}
      />
    </>
  );
}
