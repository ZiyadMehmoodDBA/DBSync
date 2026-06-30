import type { ColDef } from 'ag-grid-community';
import type { AuditDto } from '../../shared/types';
import { formatDateTime } from '../../shared/utils/date';

export const auditColumns: ColDef<AuditDto>[] = [
  { field: 'auditId', headerName: 'Audit ID', width: 100 },
  { field: 'username', headerName: 'Username', width: 160 },
  { field: 'actionName', headerName: 'Action', width: 200 },
  { field: 'objectName', headerName: 'Object', flex: 1, minWidth: 150 },
  { field: 'correlationId', headerName: 'Correlation ID', width: 180 },
  {
    field: 'createTime',
    headerName: 'Created',
    width: 165,
    valueFormatter: (p) => (p.value ? formatDateTime(p.value as string) : '—'),
  },
];
