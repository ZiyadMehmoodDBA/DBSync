import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { NodeDto } from '../../shared/types';
import { formatDateTime, formatRelativeTime } from '../../shared/utils/date';
import { formatLatency } from '../../shared/utils/numbers';
import { nodeStatusVariant } from '../../shared/utils/status';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

type NodeAction = 'enable' | 'disable' | 'approve';

export function makeNodeColumns(
  onAction: (nodeId: string, action: NodeAction) => void,
  onEdit: (node: NodeDto) => void,
): ColDef<NodeDto>[] {
  return [
    { field: 'nodeId', headerName: 'Node ID', width: 180 },
    { field: 'groupId', headerName: 'Group', width: 150 },
    { field: 'name', headerName: 'Name', flex: 1, minWidth: 150 },
    {
      field: 'status',
      headerName: 'Status',
      width: 130,
      cellRenderer: (p: ICellRendererParams<NodeDto>) =>
        p.value
          ? StatusBadge({ status: p.value as string, variant: nodeStatusVariant(p.value as string) })
          : null,
    },
    {
      field: 'syncEnabled',
      headerName: 'Sync',
      width: 90,
      valueFormatter: (p) => (p.value ? 'Yes' : 'No'),
    },
    {
      field: 'lastHeartbeat',
      headerName: 'Last Heartbeat',
      width: 150,
      valueFormatter: (p) => (p.value ? formatRelativeTime(p.value as string) : '—'),
    },
    {
      field: 'probeLatencyMs',
      headerName: 'Latency',
      width: 100,
      valueFormatter: (p) => (p.value != null ? formatLatency(p.value as number) : '—'),
    },
    {
      field: 'createdTime',
      headerName: 'Created',
      width: 165,
      valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
    },
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<NodeDto>) => {
        if (!p.data) return null;
        const { nodeId } = p.data;
        const node = p.data;
        return ActionMenu({
          items: [
            { label: 'Edit', onClick: () => onEdit(node) },
            { label: 'Enable', onClick: () => onAction(nodeId, 'enable') },
            {
              label: 'Disable',
              onClick: () => onAction(nodeId, 'disable'),
              variant: 'destructive',
            },
            { label: 'Approve Registration', onClick: () => onAction(nodeId, 'approve') },
          ],
        });
      },
    },
  ];
}
