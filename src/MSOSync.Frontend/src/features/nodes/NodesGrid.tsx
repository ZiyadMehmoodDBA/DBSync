import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeNodeColumns } from './columns';
import type { NodeDto } from '../../shared/types';

interface Props {
  rowData: NodeDto[] | undefined;
  isLoading?: boolean;
  error?: unknown;
  onRetry?: () => void;
  quickFilterText?: string;
  onEdit: (node: NodeDto) => void;
  paginationPageSize?: number;
}

export function NodesGrid({
  rowData,
  isLoading = false,
  error,
  onRetry,
  quickFilterText,
  onEdit,
  paginationPageSize,
}: Props) {
  const columns = useMemo(() => makeNodeColumns(onEdit), [onEdit]);
  return (
    <DataGrid
      rowData={rowData}
      columnDefs={columns}
      loading={isLoading}
      error={error}
      onRetry={onRetry}
      quickFilterText={quickFilterText}
      height={500}
      paginationPageSize={paginationPageSize}
    />
  );
}
