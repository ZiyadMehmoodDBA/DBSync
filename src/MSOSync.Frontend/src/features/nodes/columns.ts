import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { NodeDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { nodeStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const nodeColumns: ColDef<NodeDto>[] = [
  { field: 'nodeId', headerName: 'Node ID', width: 180 },
  { field: 'groupId', headerName: 'Group', width: 150 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  {
    field: 'status',
    headerName: 'Status',
    width: 130,
    cellRenderer: (p: ICellRendererParams<NodeDto>) =>
      p.value
        ? StatusBadge({ status: p.value as string, variant: nodeStatusVariant(p.value as string) })
        : null,
  },
  {
    field: 'syncEnabled',
    headerName: 'Sync',
    width: 90,
    valueFormatter: (p) => (p.value ? 'Yes' : 'No'),
  },
  {
    field: 'lastHeartbeat',
    headerName: 'Last Heartbeat',
    width: 150,
    valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : '—'),
  },
  {
    field: 'probeLatencyMs',
    headerName: 'Latency',
    width: 100,
    valueFormatter: (p) => (p.value != null ? formatLatency(p.value as number) : '—'),
  },
  {
    field: 'createdTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
