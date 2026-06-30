import type { IncomingBatchFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { incomingBatchColumns } from './columns';
import { useIncomingBatches } from './hooks';

interface Props {
  filter: IncomingBatchFilter;
  onFilterChange: (f: IncomingBatchFilter) => void;
}

export function IncomingBatchesGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useIncomingBatches(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={incomingBatchColumns}
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
