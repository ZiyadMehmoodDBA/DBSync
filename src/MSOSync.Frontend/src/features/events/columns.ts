import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { EventSummaryDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const eventColumns: ColDef<EventSummaryDto>[] = [
  { field: 'eventId', headerName: 'Event ID', width: 100 },
  { field: 'triggerId', headerName: 'Trigger', width: 150 },
  { field: 'sourceNodeId', headerName: 'Source Node', width: 160 },
  { field: 'channelId', headerName: 'Channel', width: 130 },
  { field: 'eventType', headerName: 'Type', width: 90 },
  { field: 'tableName', headerName: 'Table', width: 150 },
  { field: 'batchId', headerName: 'Batch ID', width: 100 },
  {
    field: 'createTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
  {
    field: 'isProcessed',
    headerName: 'Processed',
    width: 110,
    cellRenderer: (p: ICellRendererParams<EventSummaryDto>) =>
      p.value
        ? StatusBadge({ status: 'Yes', variant: 'success' })
        : StatusBadge({ status: 'No', variant: 'neutral' }),
  },
];
