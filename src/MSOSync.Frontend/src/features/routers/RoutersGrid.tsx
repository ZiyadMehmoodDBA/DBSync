import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { routerColumns } from './columns';
import { useRouters } from './hooks';

interface Props { quickFilterText?: string; }

export function RoutersGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useRouters();
  return (
    <DataGrid
      rowData={data}
      columnDefs={routerColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
