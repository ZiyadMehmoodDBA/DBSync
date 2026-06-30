import { useMemo } from 'react';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { parameterColumns } from './columns';
import type { ParameterRow } from './columns';
import { useParameters, useParameterDescriptors } from './hooks';

export function ParametersGrid() {
  const { data: params, isLoading: paramsLoading, error: paramsError, refetch: refetchParams } = useParameters();
  const { data: descriptors } = useParameterDescriptors();

  const rows: ParameterRow[] | undefined = useMemo(() => {
    if (!params) return undefined;
    const descriptorMap = new Map((descriptors ?? []).map((d) => [d.name, d]));
    return params.map((p) => ({ ...p, descriptor: descriptorMap.get(p.name) }));
  }, [params, descriptors]);

  return (
    <DataGrid
      rowData={rows}
      columnDefs={parameterColumns}
      loading={paramsLoading}
      error={paramsError}
      onRetry={() => void refetchParams()}
      height={500}
    />
  );
}
