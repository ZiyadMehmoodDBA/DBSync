import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { LockDto } from '../../shared/types';
import { formatRelativeTime } from '../../shared/utils/date';
import { ActionButton } from '../../shared/components/actions';

export function makeLocksColumns(onRelease: (lockName: string) => void): ColDef<LockDto>[] {
  return [
    { field: 'lockName', headerName: 'Lock Name', flex: 1, minWidth: 180 },
    { field: 'lockOwner', headerName: 'Owner', width: 200 },
    {
      field: 'lockTime',
      headerName: 'Held Since',
      width: 160,
      valueFormatter: (p) => formatRelativeTime(p.value as string),
    },
    {
      headerName: 'Duration',
      width: 140,
      valueGetter: (p) => {
        if (!p.data?.lockTime) return '';
        const diffMs = Date.now() - new Date(p.data.lockTime).getTime();
        const diffSec = Math.round(diffMs / 1000);
        if (diffSec < 60) return `${diffSec}s`;
        const diffMin = Math.round(diffSec / 60);
        if (diffMin < 60) return `${diffMin}m`;
        return `${Math.round(diffMin / 60)}h`;
      },
    },
    {
      headerName: 'Actions',
      width: 110,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<LockDto>) => {
        if (!p.data) return null;
        return ActionButton({
          label: 'Release',
          onClick: () => onRelease(p.data!.lockName),
          variant: 'destructive',
        });
      },
    },
  ];
}
