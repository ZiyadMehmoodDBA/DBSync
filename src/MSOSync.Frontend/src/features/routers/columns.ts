import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { RouterDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const routerColumns: ColDef<RouterDto>[] = [
  { field: 'routerId', headerName: 'Router ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  { field: 'sourceGroupId', headerName: 'Source Group', width: 160 },
  { field: 'targetGroupId', headerName: 'Target Group', width: 160 },
  {
    field: 'channelIds',
    headerName: 'Channels',
    width: 200,
    valueFormatter: (p) => {
      const ids = p.value as string[] | undefined;
      return ids ? ids.join(', ') : '—';
    },
  },
  {
    field: 'enabled',
    headerName: 'Enabled',
    width: 110,
    cellRenderer: (p: ICellRendererParams<RouterDto>) =>
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
