import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { channelColumns } from './columns';
import { useChannels } from './hooks';

interface Props { quickFilterText?: string; }

export function ChannelsGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useChannels();
  return (
    <DataGrid
      rowData={data}
      columnDefs={channelColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
