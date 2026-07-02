import { useState, useRef, useEffect } from 'react';
import type { OutgoingBatchFilter } from '../../shared/types';
import { OutgoingBatchFilters } from './OutgoingBatchFilters';
import { OutgoingBatchesGrid } from './OutgoingBatchesGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_BATCH_PAGE_SIZE } from '../../shared/constants/query';
import { Button } from '../../components/ui/button';
import { useRetryAllBatchesMutation } from './mutations';
import { useOutgoingBatches } from './hooks';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

export function OutgoingBatchesPage() {
  const savedFilter   = usePreference<OutgoingBatchFilter>(PreferenceKeys.outgoingFilter,   { page: 1, pageSize: DEFAULT_BATCH_PAGE_SIZE });
  const savedPageSize = usePreference<number>             (PreferenceKeys.outgoingPageSize,  DEFAULT_BATCH_PAGE_SIZE);
  const { mutate: setPref } = useSetPreference();

  const [filter, setFilter] = useState<OutgoingBatchFilter>({ page: 1, pageSize: savedPageSize });
  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current && savedFilter.page !== undefined) {
      setFilter({ ...savedFilter, page: 1 });
      prefsApplied.current = true;
    }
  }, [savedFilter]);

  const retryAllMutation = useRetryAllBatchesMutation();
  const { data } = useOutgoingBatches(filter);
  const canExport = useHasPermission(PermissionKeys.ExportData);
  const canRetry  = useHasPermission(PermissionKeys.RetryBatches);

  function handleFilterChange(next: OutgoingBatchFilter) {
    setFilter(next);
    const { page: _page, ...filterToSave } = next;
    setPref({ key: PreferenceKeys.outgoingFilter,   value: filterToSave });
    setPref({ key: PreferenceKeys.outgoingPageSize,  value: next.pageSize });
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Outgoing Batches</h1>
        <div className="flex items-center gap-2">
          <ExportMenu
            resource="outgoing-batches"
            currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
            queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
            canExport={canExport}
          />
          {canRetry ? (
            <Button
              variant="outline"
              onClick={() => void retryAllMutation.mutateAsync()}
              disabled={retryAllMutation.isPending}
            >
              {retryAllMutation.isPending ? 'Retrying…' : 'Retry All'}
            </Button>
          ) : (
            <span title="You don't have permission to retry batches">
              <Button variant="outline" disabled>Retry All</Button>
            </span>
          )}
        </div>
      </div>
      <OutgoingBatchFilters onFilter={handleFilterChange} />
      <OutgoingBatchesGrid filter={filter} onFilterChange={handleFilterChange} canRetry={canRetry} />
    </div>
  );
}
