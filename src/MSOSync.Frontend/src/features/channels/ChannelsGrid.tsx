import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeChannelColumns } from './columns';
import { useChannels } from './hooks';
import type { ChannelDto } from '../../shared/types';

interface Props {
  quickFilterText?: string;
  onEdit: (row: ChannelDto) => void;
  onDelete: (row: ChannelDto) => void;
}

export function ChannelsGrid({ quickFilterText, onEdit, onDelete }: Props) {
  const { data, isLoading, error, refetch } = useChannels();
  const columns = useMemo(() => makeChannelColumns(onEdit, onDelete), [onEdit, onDelete]);
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
