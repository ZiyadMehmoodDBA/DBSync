import { AgGridReact } from 'ag-grid-react';
import type { ColDef } from 'ag-grid-community';
import { ErrorState } from '../feedback/ErrorState';
import { EmptyState } from '../feedback/EmptyState';
import { Button } from '../../../components/ui/button';

interface Props<T extends object> {
  rowData: T[] | undefined;
  columnDefs: ColDef<T>[];
  loading?: boolean;
  total: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
  height?: string | number;
  error?: unknown;
  onRetry?: () => void;
}

export function ServerGrid<T extends object>({
  rowData,
  columnDefs,
  loading = false,
  total,
  page,
  pageSize,
  onPageChange,
  onPageSizeChange,
  height = 500,
  error,
  onRetry,
}: Props<T>) {
  if (error) return <ErrorState error={error} onRetry={onRetry} />;

  const totalPages = Math.max(1, Math.ceil(total / pageSize));
  const startRow = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const endRow = Math.min(page * pageSize, total);

  return (
    <div className="flex flex-col gap-2">
      <div className="ag-theme-quartz w-full" style={{ height }}>
        <AgGridReact
          rowData={rowData ?? []}
          columnDefs={columnDefs}
          loading={loading}
          defaultColDef={{ sortable: false, filter: false, resizable: true }}
        />
      </div>
      {!loading && total === 0 && <EmptyState />}
      <div className="flex items-center justify-between px-1 text-sm text-neutral-600 dark:text-neutral-400">
        <span>
          {total === 0
            ? 'No results'
            : `Showing ${startRow}–${endRow} of ${total.toLocaleString()}`}
        </span>
        <div className="flex items-center gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(page - 1)}
            disabled={page <= 1 || loading}
          >
            ← Prev
          </Button>
          <span className="text-xs">
            Page {page} of {totalPages}
          </span>
          <Button
            variant="outline"
            size="sm"
            onClick={() => onPageChange(page + 1)}
            disabled={page >= totalPages || loading}
          >
            Next →
          </Button>
          <select
            value={pageSize}
            onChange={(e) => {
              onPageSizeChange(Number(e.target.value));
            }}
            className="rounded border border-neutral-200 dark:border-neutral-700 bg-white dark:bg-neutral-900 px-2 py-1 text-xs"
          >
            <option value={20}>20 / page</option>
            <option value={50}>50 / page</option>
            <option value={100}>100 / page</option>
          </select>
        </div>
      </div>
    </div>
  );
}
