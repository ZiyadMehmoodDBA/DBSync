import { useState } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { OutgoingBatchFilters } from './OutgoingBatchFilters';
import { OutgoingBatchesGrid } from './OutgoingBatchesGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: OutgoingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function OutgoingBatchesPage() {
  const [filter, setFilter] = useState<OutgoingBatchFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Outgoing Batches</h1>
      <OutgoingBatchFilters onFilter={setFilter} />
      <OutgoingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
