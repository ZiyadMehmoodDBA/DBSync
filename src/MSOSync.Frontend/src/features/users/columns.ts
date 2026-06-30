import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { UserSummaryDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const userColumns: ColDef<UserSummaryDto>[] = [
  { field: 'userId', headerName: 'User ID', width: 90 },
  { field: 'username', headerName: 'Username', flex: 1, minWidth: 150 },
  {
    field: 'roles',
    headerName: 'Roles',
    width: 200,
    valueFormatter: (p) => {
      const roles = p.value as string[] | undefined;
      return roles ? roles.join(', ') : '—';
    },
  },
  {
    field: 'enabled',
    headerName: 'Status',
    width: 110,
    cellRenderer: (p: ICellRendererParams<UserSummaryDto>) =>
      StatusBadge({
        status: p.value ? 'Active' : 'Disabled',
        variant: p.value ? 'success' : 'neutral',
      }),
  },
  {
    field: 'createdTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
  {
    field: 'lastLoginTime',
    headerName: 'Last Login',
    width: 150,
    valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : 'Never'),
  },
];
