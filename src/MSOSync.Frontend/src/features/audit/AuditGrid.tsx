import type { AuditFilter } from '../../shared/types';
import { ServerGrid } from '../../shared/components/data-display/ServerGrid';
import { auditColumns } from './columns';
import { useAuditLog } from './hooks';

interface Props { filter: AuditFilter; onFilterChange: (f: AuditFilter) => void; }

export function AuditGrid({ filter, onFilterChange }: Props) {
  const { data, isLoading, error, refetch } = useAuditLog(filter);
  return (
    <ServerGrid
      rowData={data?.data}
      columnDefs={auditColumns}
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
