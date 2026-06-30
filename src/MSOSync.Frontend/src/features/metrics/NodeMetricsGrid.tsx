import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { NodeMetricsDto } from '../../shared/types';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { nodeStatusVariant } from '../../shared/utils/status';
import { formatRelativeTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { useNodeMetrics } from './hooks';

const nodeMetricColumns: ColDef<NodeMetricsDto>[] = [
  { field: 'nodeId', headerName: 'Node ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 140 },
  {
    field: 'status',
    headerName: 'Status',
    width: 130,
    cellRenderer: (p: ICellRendererParams<NodeMetricsDto>) =>
      p.value
        ? StatusBadge({ status: p.value as string, variant: nodeStatusVariant(p.value as string) })
        : null,
  },
  { field: 'pendingEvents', headerName: 'Pending', width: 100 },
  { field: 'batchesSent', headerName: 'Sent', width: 90 },
  { field: 'batchesReceived', headerName: 'Received', width: 100 },
  {
    field: 'lastHeartbeat',
    headerName: 'Last HB',
    width: 140,
    valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : '—'),
  },
  {
    field: 'probeLatencyMs',
    headerName: 'Latency',
    width: 100,
    valueFormatter: (p) => (p.value != null ? formatLatency(p.value as number) : '—'),
  },
];

export function NodeMetricsGrid() {
  const { data, isLoading, error, refetch } = useNodeMetrics();
  return (
    <DataGrid
      rowData={data}
      columnDefs={nodeMetricColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      height={350}
    />
  );
}
