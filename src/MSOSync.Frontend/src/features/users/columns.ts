import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { UserSummaryDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

export function makeUserColumns(
  onEdit: (row: UserSummaryDto) => void,
  onDeactivate: (row: UserSummaryDto) => void,
  currentUsername?: string,
): ColDef<UserSummaryDto>[] {
  return [
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
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<UserSummaryDto>) => {
        if (!p.data) return null;
        const row = p.data;
        const isSelf = currentUsername != null && row.username === currentUsername;
        return ActionMenu({
          items: [
            { label: 'Edit', onClick: () => onEdit(row) },
            {
              label: 'Deactivate',
              onClick: () => onDeactivate(row),
              variant: 'destructive',
              disabled: isSelf,
            },
          ],
        });
      },
    },
  ];
}
