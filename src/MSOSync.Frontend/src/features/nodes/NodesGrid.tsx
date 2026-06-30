import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { nodeColumns } from './columns';
import { useNodes } from './hooks';

interface Props {
  quickFilterText?: string;
}

export function NodesGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useNodes();
  return (
    <DataGrid
      rowData={data}
      columnDefs={nodeColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
