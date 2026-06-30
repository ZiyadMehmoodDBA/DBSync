import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { IncomingBatchSummaryDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { batchStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const incomingBatchColumns: ColDef<IncomingBatchSummaryDto>[] = [
  { field: 'batchId', headerName: 'Batch ID', width: 110 },
  { field: 'sourceNodeId', headerName: 'Source Node', width: 160 },
  { field: 'channelId', headerName: 'Channel', width: 130 },
  {
    field: 'status',
    headerName: 'Status',
    width: 120,
    cellRenderer: (p: ICellRendererParams<IncomingBatchSummaryDto>) =>
      p.value ? StatusBadge({ status: p.value as string, variant: batchStatusVariant(p.value as string) }) : null,
  },
  { field: 'rowCount', headerName: 'Rows', width: 80 },
  {
    field: 'receivedTime',
    headerName: 'Received',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
  {
    field: 'appliedTime',
    headerName: 'Applied',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
  {
    field: 'applyTimeMs',
    headerName: 'Apply Time',
    width: 110,
    valueFormatter: (p) => (p.value != null ? formatLatency(p.value as number) : '—'),
  },
];
