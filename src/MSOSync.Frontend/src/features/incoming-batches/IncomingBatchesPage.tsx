import { useState } from 'react';
import type { IncomingBatchFilter } from '../../shared/types';
import { IncomingBatchFilters } from './IncomingBatchFilters';
import { IncomingBatchesGrid } from './IncomingBatchesGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: IncomingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function IncomingBatchesPage() {
  const [filter, setFilter] = useState<IncomingBatchFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Incoming Batches</h1>
      <IncomingBatchFilters onFilter={setFilter} />
      <IncomingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
