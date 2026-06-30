import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { TriggerDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const triggerColumns: ColDef<TriggerDto>[] = [
  { field: 'triggerId', headerName: 'Trigger ID', width: 180 },
  { field: 'channelId', headerName: 'Channel', width: 150 },
  { field: 'schemaName', headerName: 'Schema', width: 120 },
  { field: 'tableName', headerName: 'Table', width: 150 },
  {
    field: 'captureInsert',
    headerName: 'Insert',
    width: 80,
    valueFormatter: (p) => (p.value ? '✓' : '—'),
  },
  {
    field: 'captureUpdate',
    headerName: 'Update',
    width: 80,
    valueFormatter: (p) => (p.value ? '✓' : '—'),
  },
  {
    field: 'captureDelete',
    headerName: 'Delete',
    width: 80,
    valueFormatter: (p) => (p.value ? '✓' : '—'),
  },
  {
    field: 'enabled',
    headerName: 'Enabled',
    width: 110,
    cellRenderer: (p: ICellRendererParams<TriggerDto>) =>
      StatusBadge({
        status: p.value ? 'Enabled' : 'Disabled',
        variant: p.value ? 'success' : 'neutral',
      }),
  },
  {
    field: 'createdTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
