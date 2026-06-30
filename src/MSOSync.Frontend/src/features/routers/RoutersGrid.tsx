import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeRouterColumns } from './columns';
import { useRouters } from './hooks';
import type { RouterDto } from '../../shared/types';

interface Props {
  quickFilterText?: string;
  onEdit: (row: RouterDto) => void;
  onDelete: (row: RouterDto) => void;
}

export function RoutersGrid({ quickFilterText, onEdit, onDelete }: Props) {
  const { data, isLoading, error, refetch } = useRouters();
  const columns = useMemo(() => makeRouterColumns(onEdit, onDelete), [onEdit, onDelete]);
  return (
    <DataGrid
      rowData={data}
      columnDefs={columns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
