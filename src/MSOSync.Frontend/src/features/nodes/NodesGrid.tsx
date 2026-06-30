import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeNodeColumns } from './columns';
import { useNodes } from './hooks';

type NodeAction = 'enable' | 'disable' | 'approve';

interface Props {
  quickFilterText?: string;
  onAction: (nodeId: string, action: NodeAction) => void;
}

export function NodesGrid({ quickFilterText, onAction }: Props) {
  const { data, isLoading, error, refetch } = useNodes();
  const columns = useMemo(() => makeNodeColumns(onAction), [onAction]);
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
