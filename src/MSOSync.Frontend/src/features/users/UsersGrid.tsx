import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { userColumns } from './columns';
import { useUsers } from './hooks';

interface Props { quickFilterText?: string; }

export function UsersGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useUsers();
  return (
    <DataGrid
      rowData={data?.data}
      columnDefs={userColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
