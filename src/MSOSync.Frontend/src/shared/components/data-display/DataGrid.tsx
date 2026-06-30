import { AgGridReact } from 'ag-grid-react';
import type { ColDef } from 'ag-grid-community';
import { ErrorState } from '../feedback/ErrorState';
import { EmptyState } from '../feedback/EmptyState';

interface Props<T extends object> {
  rowData: T[] | undefined;
  columnDefs: ColDef<T>[];
  loading?: boolean;
  height?: string | number;
  quickFilterText?: string;
  error?: unknown;
  onRetry?: () => void;
}

export function DataGrid<T extends object>({
  rowData,
  columnDefs,
  loading = false,
  height = '100%',
  quickFilterText,
  error,
  onRetry,
}: Props<T>) {
  if (error) return <ErrorState error={error} onRetry={onRetry} />;

  const isEmpty = !loading && (rowData?.length ?? 0) === 0;

  return (
    <div className="flex flex-col gap-2">
      <div className="ag-theme-quartz w-full" style={{ height }}>
        <AgGridReact
          rowData={rowData ?? []}
          columnDefs={columnDefs}
          loading={loading}
          quickFilterText={quickFilterText}
          pagination
          paginationPageSize={20}
          defaultColDef={{ sortable: true, filter: true, resizable: true }}
        />
      </div>
      {isEmpty && <EmptyState />}
    </div>
  );
}
