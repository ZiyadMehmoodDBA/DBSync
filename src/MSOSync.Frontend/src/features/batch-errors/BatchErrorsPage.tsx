import { useState } from 'react';
import type { BatchErrorFilter } from '../../shared/types';
import { BatchErrorFilters } from './BatchErrorFilters';
import { BatchErrorsGrid } from './BatchErrorsGrid';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';

const defaultFilter: BatchErrorFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function BatchErrorsPage() {
  const [filter, setFilter] = useState<BatchErrorFilter>(defaultFilter);
  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Batch Errors</h1>
      <BatchErrorFilters onFilter={setFilter} />
      <BatchErrorsGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
