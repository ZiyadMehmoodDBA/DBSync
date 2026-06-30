import { useState, useCallback, useMemo } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { makeOutgoingBatchColumns } from './columns';
import { useOutgoingBatches } from './hooks';
import { useRetryBatchMutation } from './mutations';

interface Props {
  filter: OutgoingBatchFilter;
  onFilterChange: (f: OutgoingBatchFilter) => void;
}

export function OutgoingBatchesGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useOutgoingBatches(filter);
  const retryMutation = useRetryBatchMutation();
  const [pendingBatchId, setPendingBatchId] = useState<number | null>(null);

  const handleRetry = useCallback(
    async (batchId: number) => {
      setPendingBatchId(batchId);
      try {
        await retryMutation.mutateAsync(batchId);
      } finally {
        setPendingBatchId(null);
      }
    },
    [retryMutation],
  );

  const columns = useMemo(
    () => makeOutgoingBatchColumns((batchId) => void handleRetry(batchId), pendingBatchId),
    [handleRetry, pendingBatchId],
  );

  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={columns}
      loading={isLoading}
      total={data?.total ?? 0}
      page={filter.page}
      pageSize={filter.pageSize}
      onPageChange={(p) => onFilterChange({ ...filter, page: p })}
      onPageSizeChange={(s) => onFilterChange({ ...filter, page: 1, pageSize: s })}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
