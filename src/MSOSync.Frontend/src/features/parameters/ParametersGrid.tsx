import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { makeParameterColumns } from './columns';
import type { ParameterRow } from './columns';
import { useParameters, useParameterDescriptors } from './hooks';

interface Props {
  quickFilterText?: string;
  onEdit: (row: ParameterRow) => void;
  canEdit?: boolean;
}

export function ParametersGrid({ quickFilterText, onEdit, canEdit = true }: Props) {
  const { data: params, isLoading: paramsLoading, error: paramsError, refetch: refetchParams } = useParameters();
  const { data: descriptors } = useParameterDescriptors();

  const rows: ParameterRow[] | undefined = useMemo(() => {
    if (!params) return undefined;
    const descriptorMap = new Map((descriptors ?? []).map((d) => [d.name, d]));
    return params.map((p) => ({ ...p, descriptor: descriptorMap.get(p.name) }));
  }, [params, descriptors]);

  const columns = useMemo(() => makeParameterColumns(onEdit, canEdit), [onEdit, canEdit]);

  return (
    <DataGrid
      rowData={rows}
      columnDefs={columns}
      loading={paramsLoading}
      error={paramsError}
      onRetry={() => void refetchParams()}
      quickFilterText={quickFilterText}
      height={500}
    />
  );
}
