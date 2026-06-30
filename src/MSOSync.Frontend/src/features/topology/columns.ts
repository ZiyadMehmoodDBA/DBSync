import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { TopologyGroupDto } from '../../shared/types';
import { connectivityStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';

export const topologyGroupColumns: ColDef<TopologyGroupDto>[] = [
  { field: 'groupId', headerName: 'Group ID', width: 180 },
  { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
  { field: 'totalNodes', headerName: 'Total', width: 90 },
  { field: 'reachableNodes', headerName: 'Reachable', width: 110 },
  { field: 'degradedNodes', headerName: 'Degraded', width: 110 },
  { field: 'unreachableNodes', headerName: 'Unreachable', width: 120 },
  { field: 'unknownNodes', headerName: 'Unknown', width: 100 },
  {
    field: 'connectivityStatus',
    headerName: 'Status',
    width: 130,
    cellRenderer: (p: ICellRendererParams<TopologyGroupDto>) =>
      p.value
        ? StatusBadge({
            status: p.value as string,
            variant: connectivityStatusVariant(p.value as string),
          })
        : null,
  },
];
