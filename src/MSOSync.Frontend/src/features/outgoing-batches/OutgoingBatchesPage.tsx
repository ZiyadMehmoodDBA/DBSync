import { useState } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { OutgoingBatchFilters } from './OutgoingBatchFilters';
import { OutgoingBatchesGrid } from './OutgoingBatchesGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';
import { Button } from '../../components/ui/button';
import { useRetryAllBatchesMutation } from './mutations';
import { useOutgoingBatches } from './hooks';

const defaultFilter: OutgoingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function OutgoingBatchesPage() {
  const [filter, setFilter] = useState<OutgoingBatchFilter>(defaultFilter);
  const retryAllMutation = useRetryAllBatchesMutation();
  const { data } = useOutgoingBatches(filter);

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Outgoing Batches</h1>
        <div className="flex items-center gap-2">
          <ExportMenu
            resource="outgoing-batches"
            currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
            queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
          />
          <Button
            variant="outline"
            onClick={() => void retryAllMutation.mutateAsync()}
            disabled={retryAllMutation.isPending}
          >
            {retryAllMutation.isPending ? 'Retrying…' : 'Retry All'}
          </Button>
        </div>
      </div>
      <OutgoingBatchFilters onFilter={setFilter} />
      <OutgoingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
