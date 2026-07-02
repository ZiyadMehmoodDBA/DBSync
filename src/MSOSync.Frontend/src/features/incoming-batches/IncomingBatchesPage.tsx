import { useState } from 'react';
import type { IncomingBatchFilter } from '../../shared/types';
import { IncomingBatchFilters } from './IncomingBatchFilters';
import { IncomingBatchesGrid } from './IncomingBatchesGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';
import { useIncomingBatches } from './hooks';

const defaultFilter: IncomingBatchFilter = { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE };

export function IncomingBatchesPage() {
  const [filter, setFilter] = useState<IncomingBatchFilter>(defaultFilter);
  const { data } = useIncomingBatches(filter);

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Incoming Batches</h1>
        <ExportMenu
          resource="incoming-batches"
          currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
          queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
        />
      </div>
      <IncomingBatchFilters onFilter={setFilter} />
      <IncomingBatchesGrid filter={filter} onFilterChange={setFilter} />
    </div>
  );
}
