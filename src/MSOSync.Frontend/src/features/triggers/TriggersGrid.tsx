import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { triggerColumns } from './columns';
import { useTriggers } from './hooks';

interface Props { quickFilterText?: string; }

export function TriggersGrid({ quickFilterText }: Props) {
  const { data, isLoading, error, refetch } = useTriggers();
  return (
    <DataGrid
      rowData={data}
      columnDefs={triggerColumns}
      loading={isLoading}
      error={error}
      onRetry={() => void refetch()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
