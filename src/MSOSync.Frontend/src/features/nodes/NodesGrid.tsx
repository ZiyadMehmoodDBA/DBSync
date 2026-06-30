import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeNodeColumns } from './columns';
import { useNodes } from './hooks';
import type { NodeDto } from '../../shared/types';

type NodeAction = 'enable' | 'disable' | 'approve';

interface Props {
  quickFilterText?: string;
  onAction: (nodeId: string, action: NodeAction) => void;
  onEdit: (node: NodeDto) => void;
}

export function NodesGrid({ quickFilterText, onAction, onEdit }: Props) {
  const { data, isLoading, error, refetch } = useNodes();
  const columns = useMemo(() => makeNodeColumns(onAction, onEdit), [onAction, onEdit]);
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
