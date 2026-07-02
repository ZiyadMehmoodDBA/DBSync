import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeNodeColumns } from './columns';
import type { NodeDto } from '../../shared/types';

type NodeAction = 'enable' | 'disable' | 'approve';

interface Props {
  rowData: NodeDto[] | undefined;
  isLoading?: boolean;
  error?: unknown;
  onRetry?: () => void;
  quickFilterText?: string;
  onAction: (nodeId: string, action: NodeAction) => void;
  onEdit: (node: NodeDto) => void;
  paginationPageSize?: number;
  canApprove?: boolean;
}

export function NodesGrid({
  rowData,
  isLoading = false,
  error,
  onRetry,
  quickFilterText,
  onAction,
  onEdit,
  paginationPageSize,
  canApprove = true,
}: Props) {
  const columns = useMemo(() => makeNodeColumns(onAction, onEdit, canApprove), [onAction, onEdit, canApprove]);
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
