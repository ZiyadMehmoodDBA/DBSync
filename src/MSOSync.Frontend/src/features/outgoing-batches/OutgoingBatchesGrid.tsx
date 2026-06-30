import type { OutgoingBatchFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { outgoingBatchColumns } from './columns';
import { useOutgoingBatches } from './hooks';

interface Props { filter: OutgoingBatchFilter; onFilterChange: (f: OutgoingBatchFilter) => void; }

export function OutgoingBatchesGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useOutgoingBatches(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={outgoingBatchColumns}
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
