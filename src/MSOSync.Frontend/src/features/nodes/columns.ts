import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { NodeDto } from '../../shared/types';
import { formatRelativeTime } from '../../shared/utils/date';
import { LifecycleBadge } from '../../shared/components/node/LifecycleBadge';
import { ActionMenu } from '../../shared/components/actions';

export function makeNodeColumns(
  onEdit: (node: NodeDto) => void,
): ColDef<NodeDto>[] {
  return [
    { field: 'nodeId', headerName: 'Node ID', width: 180 },
    { field: 'groupId', headerName: 'Group', width: 150 },
    { field: 'syncUrl', headerName: 'Sync URL', flex: 1, minWidth: 150 },
    {
      field: 'lifecycleState',
      headerName: 'Lifecycle',
      width: 160,
      cellRenderer: (p: ICellRendererParams<NodeDto>) =>
        p.data ? LifecycleBadge({ state: p.data.lifecycleState }) : null,
    },
    {
      field: 'transportMode',
      headerName: 'Mode',
      width: 90,
    },
    {
      field: 'lastHeartbeat',
      headerName: 'Last Heartbeat',
      width: 150,
      valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : '—'),
    },
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<NodeDto>) => {
        if (!p.data) return null;
        const node = p.data;
        return ActionMenu({
          items: [
            { label: 'Edit', onClick: () => onEdit(node) },
          ],
        });
      },
    },
  ];
}
