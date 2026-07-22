import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { Button } from '@/components/ui/button';
import { DataGrid } from '@/shared/components/data-display/DataGrid';
import { getEntityHistory } from '@/shared/api/audit';
import type { ColDef } from 'ag-grid-community';
import type { AuditDto } from '@/shared/types/audit';
import { formatDistanceToNow } from 'date-fns';

function useEntityHistory(objectName: string | null) {
  return useQuery({
    queryKey:  ['audit', 'entity', objectName],
    queryFn:   ({ signal }) => getEntityHistory(objectName!, { pageSize: 100, signal }),
    enabled:   objectName !== null && objectName.trim() !== '',
    staleTime: 30_000,
  });
}

const COLUMNS: ColDef<AuditDto>[] = [
  { field: 'createTime', headerName: 'Time', width: 180,
    valueFormatter: p => p.value ? formatDistanceToNow(new Date(p.value as string), { addSuffix: true }) : '' },
  { field: 'actionName',    headerName: 'Action',      flex: 1 },
  { field: 'username',      headerName: 'By',          width: 140 },
  { field: 'correlationId', headerName: 'Correlation', flex: 1 },
];

export function EntityHistoryTab() {
  const [inputValue, setInputValue] = useState('');
  const [objectName, setObjectName] = useState<string | null>(null);

  const { data, isFetching } = useEntityHistory(objectName);

  return (
    <div className="space-y-4 p-4">
      <div className="flex items-center gap-3">
        <div className="space-y-1 flex-1 max-w-xs">
          <label className="text-xs text-muted-foreground">Object Name (node ID, username, etc.)</label>
          <input
            className="w-full rounded border bg-background px-3 py-1.5 text-sm"
            placeholder="e.g. node-01 or alice"
            value={inputValue}
            onChange={e => setInputValue(e.target.value)}
            onKeyDown={e => { if (e.key === 'Enter') setObjectName(inputValue.trim() || null); }}
          />
        </div>
        <Button
          className="mt-5"
          onClick={() => setObjectName(inputValue.trim() || null)}
        >
          Load
        </Button>
      </div>

      {objectName && (
        isFetching ? (
          <p className="text-sm text-muted-foreground">Loading…</p>
        ) : !data || data.items.length === 0 ? (
          <p className="text-sm text-muted-foreground">No audit events found for "{objectName}".</p>
        ) : (
          <>
            {data.hasMore && (
              <p className="text-xs text-muted-foreground">Showing first 100 events. Narrow date range for full history.</p>
            )}
            <div className="h-[400px]">
              <DataGrid<AuditDto>
                rowData={data.items}
                columnDefs={COLUMNS}
                height={400}
              />
            </div>
          </>
        )
      )}
    </div>
  );
}
