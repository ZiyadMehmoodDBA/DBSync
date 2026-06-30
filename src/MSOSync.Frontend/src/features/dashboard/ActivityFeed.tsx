import type { ColDef } from 'ag-grid-community';
import type { ActivityItemDto } from '../../shared/types';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { formatDateTime } from '../../shared/utils/date';
import { useDashboardActivity } from './hooks';

const columns: ColDef<ActivityItemDto>[] = [
  { field: 'createTime', headerName: 'Time', valueFormatter: (p) => formatDateTime(p.value as string), width: 160 },
  { field: 'type', headerName: 'Type', width: 120 },
  { field: 'description', headerName: 'Description', flex: 1, minWidth: 200 },
  { field: 'nodeId', headerName: 'Node', width: 150 },
];

export function ActivityFeed() {
  const { data, isLoading, error, refetch } = useDashboardActivity(1);

  return (
    <div className="flex flex-col gap-2">
      <h2 className="text-base font-semibold">Recent Activity</h2>
      <DataGrid
        rowData={data?.data}
        columnDefs={columns}
        loading={isLoading}
        error={error}
        onRetry={() => void refetch()}
        height={320}
      />
    </div>
  );
}
