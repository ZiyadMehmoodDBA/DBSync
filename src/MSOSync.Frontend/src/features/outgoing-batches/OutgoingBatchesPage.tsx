import { useState } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { OutgoingBatchFilters } from './OutgoingBatchFilters';
import { OutgoingBatchesGrid } from './OutgoingBatchesGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';
import { Button } from '../../components/ui/button';
import { useRetryAllBatchesMutation } from './mutations';

const defaultFilter: OutgoingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function OutgoingBatchesPage() {
  const [filter, setFilter] = useState<OutgoingBatchFilter>(defaultFilter);
  const retryAllMutation = useRetryAllBatchesMutation();

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Outgoing Batches</h1>
        <Button
          variant="outline"
          onClick={() => void retryAllMutation.mutateAsync()}
          disabled={retryAllMutation.isPending}
        >
          {retryAllMutation.isPending ? 'Retrying…' : 'Retry All'}
        </Button>
      </div>
      <OutgoingBatchFilters onFilter={setFilter} />
      <OutgoingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
