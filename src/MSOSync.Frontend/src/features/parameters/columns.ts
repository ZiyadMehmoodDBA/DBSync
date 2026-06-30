import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import type { ParameterDto, ParameterDescriptorDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';
import { ActionMenu } from '../../shared/components/actions';

export interface ParameterRow extends ParameterDto {
  descriptor?: ParameterDescriptorDto;
}

export function makeParameterColumns(
  onEdit: (row: ParameterRow) => void,
): ColDef<ParameterRow>[] {
  return [
    { field: 'name', headerName: 'Name', width: 220 },
    {
      field: 'value',
      headerName: 'Value',
      flex: 1,
      minWidth: 200,
      valueFormatter: (p) => {
        const row = p.data as ParameterRow | undefined;
        return row?.isSecret ? '••••••••' : (p.value as string ?? '');
      },
    },
    {
      headerName: 'Description',
      flex: 1,
      minWidth: 200,
      valueGetter: (p) => p.data?.descriptor?.description ?? '—',
    },
    {
      field: 'isSecret',
      headerName: 'Secret',
      width: 90,
      valueFormatter: (p) => (p.value ? 'Yes' : 'No'),
    },
    {
      headerName: 'Restart Req.',
      width: 120,
      valueGetter: (p) => (p.data?.descriptor?.requiresRestart ? 'Yes' : 'No'),
    },
    {
      headerName: 'Dynamic',
      width: 100,
      valueGetter: (p) => (p.data?.descriptor?.isDynamic ? 'Yes' : 'No'),
    },
    {
      field: 'updatedTime',
      headerName: 'Updated',
      width: 165,
      valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
    },
    {
      headerName: 'Actions',
      width: 90,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<ParameterRow>) => {
        if (!p.data) return null;
        const row = p.data;
        return ActionMenu({
          items: [{ label: 'Edit', onClick: () => onEdit(row) }],
        });
      },
    },
  ];
}
