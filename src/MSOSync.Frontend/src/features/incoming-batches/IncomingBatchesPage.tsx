import { useState, useRef, useEffect } from 'react';
import type { IncomingBatchFilter } from '../../shared/types';
import type { CursorIncomingBatchFilter } from '../../shared/api/batches';
import { IncomingBatchFilters } from './IncomingBatchFilters';
import { IncomingBatchesGrid } from './IncomingBatchesGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';
import { useInfiniteIncomingBatches } from '../../shared/hooks/useInfiniteIncomingBatches';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

export function IncomingBatchesPage() {
  const savedFilter   = usePreference<Omit<IncomingBatchFilter, 'page'>>(PreferenceKeys.incomingFilter,   { pageSize: DEFAULT_BATCH_PAGE_SIZE });
  const savedPageSize = usePreference<number>                            (PreferenceKeys.incomingPageSize,  DEFAULT_BATCH_PAGE_SIZE);
  const { mutate: setPref } = useSetPreference();

  const [filter, setFilter] = useState<CursorIncomingBatchFilter>({ pageSize: savedPageSize });
  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current && savedFilter.pageSize !== undefined) {
      const { page: _page, ...rest } = savedFilter as IncomingBatchFilter;
      setFilter({ ...rest, pageSize: rest.pageSize ?? DEFAULT_BATCH_PAGE_SIZE });
      prefsApplied.current = true;
    }
  }, [savedFilter]);

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = useInfiniteIncomingBatches(filter);
  const canExport = useHasPermission(PermissionKeys.ExportData);

  const allItems = data?.pages.flatMap(p => p.items) ?? [];

  function handleFilterChange(next: CursorIncomingBatchFilter) {
    setFilter(next);
    setPref({ key: PreferenceKeys.incomingFilter,   value: next });
    setPref({ key: PreferenceKeys.incomingPageSize,  value: next.pageSize });
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Incoming Batches</h1>
        <ExportMenu
          resource="incoming-batches"
          currentData={allItems as unknown as Record<string, unknown>[]}
          queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
          canExport={canExport}
        />
      </div>
      <IncomingBatchFilters onFilter={handleFilterChange} />
      <IncomingBatchesGrid
        data={allItems}
        hasMore={hasNextPage ?? false}
        isFetchingMore={isFetchingNextPage}
        onLoadMore={() => void fetchNextPage()}
        pageSize={filter.pageSize ?? DEFAULT_BATCH_PAGE_SIZE}
      />
    </div>
  );
}
