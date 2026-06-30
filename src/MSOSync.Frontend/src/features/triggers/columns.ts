import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { TriggerDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { StatusBadge } from '../../shared/components/data-display/StatusBadge';
import { ActionMenu } from '../../shared/components/actions';

type ConfirmableAction = 'enable' | 'disable' | 'rebuild';

export function makeTriggersColumns(
  onAction: (triggerId: string, action: ConfirmableAction) => void,
  onVerify: (triggerId: string) => void,
  onEdit: (trigger: TriggerDto) => void,
  onDelete: (trigger: TriggerDto) => void,
): ColDef<TriggerDto>[] {
  return [
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
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<TriggerDto>) => {
        if (!p.data) return null;
        const { triggerId } = p.data;
        const trigger = p.data;
        return ActionMenu({
          items: [
            { label: 'Enable', onClick: () => onAction(triggerId, 'enable') },
            {
              label: 'Disable',
              onClick: () => onAction(triggerId, 'disable'),
              variant: 'destructive',
            },
            {
              label: 'Rebuild',
              onClick: () => onAction(triggerId, 'rebuild'),
              variant: 'destructive',
            },
            { label: 'Verify', onClick: () => onVerify(triggerId) },
            { label: 'Edit', onClick: () => onEdit(trigger) },
            { label: 'Delete', onClick: () => onDelete(trigger), variant: 'destructive' },
          ],
        });
      },
    },
  ];
}
