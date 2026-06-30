import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { lockColumns } from './columns';
import { useLocks } from './hooks';

export function LocksGrid() {
  const { data, isLoading, error, refetch } = useLocks();

  return (
    <DataGrid
      rowData={data}
      columnDefs={lockColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={500}
    />
  );
}
