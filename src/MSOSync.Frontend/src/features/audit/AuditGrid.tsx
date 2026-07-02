import { Loader2 } from 'lucide-react';
import type { AuditDto } from '../../shared/types';
import { DataGrid } from '../../shared/components/data-display/DataGrid';
import { auditColumns } from './columns';
import { Button } from '../../components/ui/button';

interface Props {
  data: AuditDto[];
  hasMore: boolean;
  isFetchingMore: boolean;
  onLoadMore: () => void;
  pageSize: number;
}

export function AuditGrid({ data, hasMore, isFetchingMore, onLoadMore, pageSize }: Props) {
  return (
    <div className="flex flex-col">
      <DataGrid
        rowData={data}
        columnDefs={auditColumns}
        height={500}
      />
      {(hasMore || isFetchingMore) && (
        <div className="flex items-center justify-between px-2 py-3 border-t text-sm text-muted-foreground">
          <span>Showing {data.length} results</span>
          <Button
            variant="outline"
            size="sm"
            onClick={onLoadMore}
            disabled={isFetchingMore}
          >
            {isFetchingMore ? (
              <><Loader2 className="mr-2 h-4 w-4 animate-spin" /> Loading…</>
            ) : (
              `Load ${pageSize} More`
            )}
          </Button>
        </div>
      )}
      {!hasMore && data.length > 0 && (
        <div className="px-2 py-2 text-sm text-muted-foreground text-center border-t">
          Showing all {data.length} results
        </div>
      )}
    </div>
  );
}
