import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeUserColumns } from './columns';
import { useUsers } from './hooks';
import type { UserSummaryDto } from '../../shared/types';

interface Props {
  quickFilterText?: string;
  onEdit: (row: UserSummaryDto) => void;
  onDeactivate: (row: UserSummaryDto) => void;
  currentUsername?: string;
  paginationPageSize?: number;
  canManage?: boolean;
}

export function UsersGrid({ quickFilterText, onEdit, onDeactivate, currentUsername, paginationPageSize, canManage = true }: Props) {
  const { data, isLoading, error, refetch } = useUsers();
  const columns = useMemo(
    () => makeUserColumns(onEdit, onDeactivate, currentUsername, canManage),
    [onEdit, onDeactivate, currentUsername, canManage],
  );
  return (
    <DataGrid
      rowData={data?.data}
      columnDefs={columns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
      paginationPageSize={paginationPageSize}
    />
  );
}
