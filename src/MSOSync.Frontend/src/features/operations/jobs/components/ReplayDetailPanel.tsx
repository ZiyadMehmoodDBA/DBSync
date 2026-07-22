import type { ColDef } from 'ag-grid-community';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { DataGrid } from '@/shared/components/data-display/DataGrid';
import { useReplayOperation, useReplayItems, useCancelReplay } from '@/shared/hooks/useReplayOperations';
import type { ReplayItemDto } from '@/shared/types/replay';

interface Props { operationId: string; onClose: () => void }

const ITEM_COLS: ColDef<ReplayItemDto>[] = [
  { field: 'channelId',    headerName: 'Channel',      width: 120 },
  { field: 'eventCount',   headerName: 'Events',       width: 80  },
  { field: 'status',       headerName: 'Status',       width: 110 },
  { field: 'sourceBatchId', headerName: 'Source Batch', width: 110 },
  { field: 'replayBatchId', headerName: 'Replay Batch', width: 110 },
  { field: 'errorMessage', headerName: 'Error',        flex: 1    },
];

export function ReplayDetailPanel({ operationId, onClose }: Props) {
  const { data: detail } = useReplayOperation(operationId);
  const { data: items  } = useReplayItems(operationId);
  const cancelMutation   = useCancelReplay();

  const canCancel = detail?.status === 'Running' || detail?.status === 'Pending';
  const progress  = detail && detail.totalItems > 0
    ? Math.round(detail.completedItems * 100 / detail.totalItems)
    : 0;

  return (
    <div className="fixed inset-y-0 right-0 w-[640px] max-w-full border-l bg-background p-4 overflow-y-auto z-40 shadow-lg">
      <div className="flex justify-between items-center mb-4">
        <span className="font-medium">Batch Replay</span>
        <button type="button" onClick={onClose} className="text-xs text-neutral-400 hover:text-neutral-600">Close</button>
      </div>

      {detail && (
        <div className="space-y-4">
          {/* Summary */}
          <div className="rounded-md border p-3 text-sm space-y-1">
            <div><span className="font-medium">Node:</span> {detail.nodeId}</div>
            <div><span className="font-medium">Mode:</span> {detail.replayMode}</div>
            <div><span className="font-medium">Range:</span> {detail.fromTime} → {detail.toTime}</div>
            <div><span className="font-medium">Status:</span> <Badge>{detail.status}</Badge></div>
          </div>

          {/* Progress bar */}
          <div>
            <div className="flex justify-between text-xs mb-1">
              <span>{detail.completedItems}/{detail.totalItems} items</span>
              <span>{progress}%</span>
            </div>
            <div className="h-2 bg-muted rounded overflow-hidden">
              <div className="h-full bg-primary transition-all" style={{ width: `${progress}%` }} />
            </div>
          </div>

          {/* Counts */}
          <div className="flex gap-4 text-sm">
            <span className="text-green-600">✓ {detail.completedItems}</span>
            <span className="text-red-600">✗ {detail.failedItems}</span>
            <span className="text-muted-foreground">— {detail.skippedItems}</span>
          </div>

          {canCancel && (
            <Button
              variant="destructive" size="sm"
              onClick={() => cancelMutation.mutate(operationId)}
              disabled={cancelMutation.isPending}
            >
              {cancelMutation.isPending ? 'Cancelling…' : 'Cancel Replay'}
            </Button>
          )}
        </div>
      )}

      {/* Items grid */}
      <div className="mt-4">
        <DataGrid
          rowData={items?.items ?? []}
          columnDefs={ITEM_COLS}
          height={400}
        />
      </div>
    </div>
  );
}
