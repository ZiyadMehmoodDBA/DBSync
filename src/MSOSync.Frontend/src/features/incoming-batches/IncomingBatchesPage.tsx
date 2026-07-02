import { useState, useRef, useEffect } from 'react';
import type { IncomingBatchFilter } from '../../shared/types';
import { IncomingBatchFilters } from './IncomingBatchFilters';
import { IncomingBatchesGrid } from './IncomingBatchesGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';
import { useIncomingBatches } from './hooks';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';

export function IncomingBatchesPage() {
  const savedFilter   = usePreference<IncomingBatchFilter>(PreferenceKeys.incomingFilter,   { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  const savedPageSize = usePreference<number>             (PreferenceKeys.incomingPageSize,  DEFAULT_BATCH_PAGE_SIZE);
  const { mutate: setPref } = useSetPreference();

  const [filter, setFilter] = useState<IncomingBatchFilter>({ page: 1, pageSize: savedPageSize });
  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current && savedFilter.page !== undefined) {
      setFilter({ ...savedFilter, page: 1 });
      prefsApplied.current = true;
    }
  }, [savedFilter]);

  const { data } = useIncomingBatches(filter);

  function handleFilterChange(next: IncomingBatchFilter) {
    setFilter(next);
    const { page: _page, ...filterToSave } = next;
    setPref({ key: PreferenceKeys.incomingFilter,   value: filterToSave });
    setPref({ key: PreferenceKeys.incomingPageSize,  value: next.pageSize });
  }

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
      <IncomingBatchFilters onFilter={handleFilterChange} />
      <IncomingBatchesGrid filter={filter} onFilterChange={handleFilterChange} />
    </div>
  );
}
