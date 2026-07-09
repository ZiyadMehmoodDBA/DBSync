import { useState, useCallback, useMemo, useEffect } from 'react';
import { useNavigate } from 'react-router-dom';
import type { ColDef, ICellRendererParams } from 'ag-grid-community';
import { DataGrid } from '@/shared/components/data-display/DataGrid';
import { useOperations, useCancelOperation, useRetryOperation } from '@/shared/hooks/useOperations';
import { OperationStatusBadge } from './components/OperationStatusBadge';
import { OperationProgressCell } from './components/OperationProgressCell';
import { ConfirmDialog } from '@/shared/components/actions/ConfirmDialog';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select';
import type { OperationDto, OperationFilter, OperationType, OperationStatus } from '@/shared/types/operations';
import { formatRelativeTime } from '@/shared/utils/date';

// --- Helpers ---

function duration(startedAt: string, completedAt: string | null): string {
  if (!completedAt) return '—';
  try {
    const ms = new Date(completedAt).getTime() - new Date(startedAt).getTime();
    if (ms < 0) return '—';
    if (ms < 1000) return `${ms}ms`;
    const s = Math.floor(ms / 1000);
    if (s < 60) return `${s}s`;
    const m = Math.floor(s / 60);
    return `${m}m ${s % 60}s`;
  } catch {
    return '—';
  }
}

const TYPE_BADGE_COLORS: Record<string, string> = {
  Export:       'bg-violet-100 text-violet-800',
  Rollout:      'bg-blue-100 text-blue-800',
  Decommission: 'bg-orange-100 text-orange-800',
  Recovery:     'bg-teal-100 text-teal-800',
};

const ALL_TYPES: OperationType[] = ['Export', 'Rollout', 'Decommission', 'Recovery'];
const ALL_STATUSES: OperationStatus[] = ['Pending', 'Running', 'Completed', 'Failed', 'Cancelled'];

// --- Component ---

export function JobsPage() {
  const navigate = useNavigate();
  const [filter, setFilter] = useState<OperationFilter>({ pageSize: 50 });
  const [selectedType, setSelectedType] = useState<string>('all');
  const [selectedStatus, setSelectedStatus] = useState<string>('all');
  const [cancelTarget, setCancelTarget] = useState<string | null>(null);
  const [cursor, setCursor] = useState<string | undefined>(undefined);
  const [rows, setRows] = useState<OperationDto[]>([]);

  const { data, isLoading, isFetching, error, refetch } = useOperations({ ...filter, cursor });
  const cancelMutation = useCancelOperation();
  const retryMutation = useRetryOperation();

  // Accumulate rows: reset on filter change, append when loading more via cursor
  useEffect(() => {
    if (!data?.items) return;
    if (cursor === undefined) {
      setRows(data.items);
    } else {
      setRows((prev) => [...prev, ...data.items]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [data]);

  const applyFilters = useCallback((type: string, status: string) => {
    setCursor(undefined);
    setFilter({
      pageSize: 50,
      types:    type   !== 'all' ? [type   as OperationType]   : undefined,
      statuses: status !== 'all' ? [status as OperationStatus] : undefined,
    });
  }, []);

  const columnDefs = useMemo<ColDef<OperationDto>[]>(() => [
    {
      field: 'operationType',
      headerName: 'Type',
      width: 130,
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        const color = TYPE_BADGE_COLORS[p.data.operationType] ?? '';
        return <Badge className={`text-xs ${color}`}>{p.data.operationType}</Badge>;
      },
    },
    {
      field: 'status',
      headerName: 'Status',
      width: 130,
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        return <OperationStatusBadge status={p.data.status} result={p.data.result} />;
      },
    },
    {
      headerName: 'Progress',
      width: 180,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        return (
          <OperationProgressCell
            status={p.data.status}
            progressPercent={p.data.progressPercent}
            progressMessage={p.data.progressMessage}
          />
        );
      },
    },
    {
      field: 'source',
      headerName: 'Source',
      width: 150,
      cellRenderer: (p: ICellRendererParams<OperationDto>) =>
        p.data ? <span className="text-xs">{p.data.source}</span> : null,
    },
    {
      field: 'summary',
      headerName: 'Summary',
      flex: 1,
      minWidth: 180,
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        return (
          <span className="truncate text-sm">{p.data.summary ?? '—'}</span>
        );
      },
    },
    {
      field: 'queuePosition',
      headerName: 'Queue',
      width: 80,
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        if (p.data.status === 'Pending' && p.data.queuePosition != null) {
          return <span className="text-xs text-muted-foreground">#{p.data.queuePosition}</span>;
        }
        return null;
      },
    },
    {
      field: 'initiatedBy',
      headerName: 'Initiated by',
      width: 160,
      cellRenderer: (p: ICellRendererParams<OperationDto>) =>
        p.data ? <span className="text-xs">{p.data.initiatedBy ?? '—'}</span> : null,
    },
    {
      field: 'startedAt',
      headerName: 'Started',
      width: 130,
      sort: 'desc',
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        return (
          <span className="text-xs text-muted-foreground" title={p.data.startedAt}>
            {formatRelativeTime(p.data.startedAt)}
          </span>
        );
      },
    },
    {
      headerName: 'Duration',
      width: 100,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        return (
          <span className="text-xs text-muted-foreground">
            {duration(p.data.startedAt, p.data.completedAt)}
          </span>
        );
      },
    },
    {
      headerName: '',
      width: 140,
      sortable: false,
      cellRenderer: (p: ICellRendererParams<OperationDto>) => {
        if (!p.data) return null;
        const op = p.data;
        return (
          <div className="flex items-center gap-1">
            {op.canCancel && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs text-destructive hover:text-destructive"
                onClick={(e) => {
                  e.stopPropagation();
                  setCancelTarget(op.operationId);
                }}
                disabled={cancelMutation.isPending}
              >
                Cancel
              </Button>
            )}
            {op.canRetry && (
              <Button
                variant="ghost"
                size="sm"
                className="h-7 px-2 text-xs"
                onClick={(e) => {
                  e.stopPropagation();
                  retryMutation.mutate(op.operationId);
                }}
                disabled={retryMutation.isPending}
              >
                Retry
              </Button>
            )}
          </div>
        );
      },
    },
  ], [cancelMutation.isPending, retryMutation.isPending]);

  return (
    <div className="flex flex-col gap-4 p-6">
      {/* Header */}
      <div>
        <h1 className="text-2xl font-semibold tracking-tight">Jobs</h1>
        <p className="text-sm text-muted-foreground">
          System operations — exports, rollouts, lifecycle events
        </p>
      </div>

      {/* Filter bar */}
      <div className="flex items-center gap-3">
        <Select
          value={selectedType}
          onValueChange={(v) => {
            setSelectedType(v);
            applyFilters(v, selectedStatus);
          }}
        >
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder="All types" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All types</SelectItem>
            {ALL_TYPES.map((t) => (
              <SelectItem key={t} value={t}>{t}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        <Select
          value={selectedStatus}
          onValueChange={(v) => {
            setSelectedStatus(v);
            applyFilters(selectedType, v);
          }}
        >
          <SelectTrigger className="w-[160px]">
            <SelectValue placeholder="All statuses" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="all">All statuses</SelectItem>
            {ALL_STATUSES.map((s) => (
              <SelectItem key={s} value={s}>{s}</SelectItem>
            ))}
          </SelectContent>
        </Select>

        {data && (
          <span className="ml-auto text-xs text-muted-foreground">
            {data.totalCount ?? 0} total
          </span>
        )}
      </div>

      {/* Grid */}
      <DataGrid
        rowData={rows}
        columnDefs={columnDefs}
        loading={isLoading}
        height={600}
        error={error}
        onRetry={() => void refetch()}
        onRowClicked={(e) => {
          const op = e.data as OperationDto | undefined;
          if (op?.correlationId) {
            navigate(
              `/operations/activity?correlationId=${encodeURIComponent(op.correlationId)}`,
            );
          }
        }}
      />

      {/* Load more */}
      {data?.nextCursor != null && (
        <div className="flex justify-center">
          <Button
            variant="outline"
            size="sm"
            disabled={isFetching}
            onClick={() => setCursor(data.nextCursor!)}
          >
            {isFetching ? 'Loading…' : 'Load more'}
          </Button>
        </div>
      )}

      {/* Cancel confirmation dialog */}
      <ConfirmDialog
        open={cancelTarget !== null}
        title="Cancel operation?"
        description="This will attempt to cancel the operation. Already-processed steps cannot be undone."
        confirmLabel={cancelMutation.isPending ? 'Cancelling…' : 'Cancel operation'}
        variant="destructive"
        loading={cancelMutation.isPending}
        onConfirm={() => {
          if (cancelTarget) {
            cancelMutation.mutate(cancelTarget, {
              onSettled: () => setCancelTarget(null),
            });
          }
        }}
        onOpenChange={(open) => {
          if (!open) setCancelTarget(null);
        }}
      />
    </div>
  );
}
