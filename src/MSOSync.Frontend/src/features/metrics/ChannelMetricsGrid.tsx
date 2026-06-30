import type { ColDef } from 'ag-grid-community';
import type { ChannelMetricsDto } from '../../shared/types';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { formatQueueDepth, formatPercent } from '../../shared/utils/numbers';
import { useChannelMetrics } from './hooks';

const channelMetricColumns: ColDef<ChannelMetricsDto>[] = [
  { field: 'channelId', headerName: 'Channel ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  {
    field: 'queueDepth',
    headerName: 'Queue Depth',
    width: 130,
    valueFormatter: (p) => formatQueueDepth(p.value as number),
  },
  {
    field: 'throughputPerMinute',
    headerName: 'Throughput/min',
    width: 150,
    valueFormatter: (p) => String(p.value ?? 0),
  },
  {
    field: 'errorRate',
    headerName: 'Error Rate',
    width: 120,
    valueFormatter: (p) => formatPercent(p.value as number),
  },
];

export function ChannelMetricsGrid() {
  const { data, isLoading, error, refetch } = useChannelMetrics();
  return (
    <DataGrid
      rowData={data}
      columnDefs={channelMetricColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={300}
    />
  );
}
