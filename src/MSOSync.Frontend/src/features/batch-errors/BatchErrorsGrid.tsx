import type { BatchErrorFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { batchErrorColumns } from './columns';
import { useBatchErrors } from './hooks';

interface Props { filter: BatchErrorFilter; onFilterChange: (f: BatchErrorFilter) => void; }

export function BatchErrorsGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useBatchErrors(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={batchErrorColumns}
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
