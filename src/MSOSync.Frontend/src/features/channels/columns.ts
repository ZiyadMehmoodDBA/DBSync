import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { ChannelDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const channelColumns: ColDef<ChannelDto>[] = [
  { field: 'channelId', headerName: 'Channel ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  { field: 'description', headerName: 'Description', flex: 1, minWidth: 200 },
  {
    field: 'enabled',
    headerName: 'Enabled',
    width: 110,
    cellRenderer: (p: ICellRendererParams<ChannelDto>) =>
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
