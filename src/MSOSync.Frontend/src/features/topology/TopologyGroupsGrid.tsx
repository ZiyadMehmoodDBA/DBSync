import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { topologyGroupColumns } from './columns';
import { useTopologyGroups } from './hooks';

export function TopologyGroupsGrid() {
  const { data, isLoading, error, refetch } = useTopologyGroups();
  return (
    <DataGrid
      rowData={data}
      columnDefs={topologyGroupColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={400}
    />
  );
}
