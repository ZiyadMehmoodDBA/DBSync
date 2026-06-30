import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { BatchErrorDetailDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import type { StatusVariant } from '../../shared/utils/status';

function severityVariant(severity: string): StatusVariant {
  switch (severity.toUpperCase()) {
    case 'CRITICAL': return 'danger';
    case 'WARNING': return 'warning';
    case 'INFO': return 'neutral';
    default: return 'neutral';
  }
}

export const batchErrorColumns: ColDef<BatchErrorDetailDto>[] = [
  { field: 'errorId', headerName: 'Error ID', width: 100 },
  { field: 'batchId', headerName: 'Batch ID', width: 110 },
  { field: 'conflictType', headerName: 'Conflict Type', width: 150 },
  {
    field: 'severity',
    headerName: 'Severity',
    width: 120,
    cellRenderer: (p: ICellRendererParams<BatchErrorDetailDto>) =>
      p.value ? StatusBadge({ status: p.value as string, variant: severityVariant(p.value as string) }) : null,
  },
  { field: 'createTime', headerName: 'Created', width: 165, valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—') },
  {
    field: 'detail',
    headerName: 'Detail',
    flex: 1,
    minWidth: 200,
    valueFormatter: (p) => {
      const v = p.value as string | undefined;
      return v && v.length > 100 ? v.slice(0, 100) + '…' : (v ?? '');
    },
  },
];
