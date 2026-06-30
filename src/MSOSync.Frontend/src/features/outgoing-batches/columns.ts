import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { OutgoingBatchDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { batchStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const outgoingBatchColumns: ColDef<OutgoingBatchDto>[] = [
  { field: 'batchId', headerName: 'Batch ID', width: 110 },
  { field: 'nodeId', headerName: 'Node', width: 160 },
  { field: 'channelId', headerName: 'Channel', width: 130 },
  {
    field: 'status',
    headerName: 'Status',
    width: 120,
    cellRenderer: (p: ICellRendererParams<OutgoingBatchDto>) =>
      p.value ? StatusBadge({ status: p.value as string, variant: batchStatusVariant(p.value as string) }) : null,
  },
  { field: 'rowCount', headerName: 'Rows', width: 80 },
  { field: 'retryCount', headerName: 'Retries', width: 85 },
  { field: 'createTime', headerName: 'Created', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  { field: 'sentTime', headerName: 'Sent', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  { field: 'ackTime', headerName: 'Ack', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  {
    field: 'error',
    headerName: 'Error',
    flex: 1,
    minWidth: 200,
    valueFormatter: (p) => {
      const v = p.value as string | undefined;
      return v && v.length > 80 ? v.slice(0, 80) + '…' : (v ?? '');
    },
  },
];
