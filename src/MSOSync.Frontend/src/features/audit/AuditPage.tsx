import { useState, useRef, useEffect } from 'react';
import type { AuditFilter } from '../../shared/types';
import type { CursorAuditFilter } from '../../shared/api/audit';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '../../components/ui/tabs';
import { AuditFilters } from './AuditFilters';
import { AuditGrid } from './AuditGrid';
import { AuditInsightsTab } from './AuditInsightsTab';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useInfiniteAudit } from '../../shared/hooks/useInfiniteAudit';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

export function AuditPage() {
  const savedFilter   = usePreference<Omit<AuditFilter, 'page'>>(PreferenceKeys.auditFilter,   { pageSize: DEFAULT_PAGE_SIZE });
  const savedPageSize = usePreference<number>                    (PreferenceKeys.auditPageSize,  DEFAULT_PAGE_SIZE);
  const { mutate: setPref } = useSetPreference();

  const [filter, setFilter] = useState<CursorAuditFilter>({ pageSize: savedPageSize });
  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current && savedFilter.pageSize !== undefined) {
      const { page: _page, ...rest } = savedFilter as AuditFilter;
      setFilter({ ...rest, pageSize: rest.pageSize ?? DEFAULT_PAGE_SIZE });
      prefsApplied.current = true;
    }
  }, [savedFilter]);

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = useInfiniteAudit(filter);
  const canExport = useHasPermission(PermissionKeys.ExportData);

  const allItems = data?.pages.flatMap(p => p.items) ?? [];

  function handleFilterChange(next: CursorAuditFilter) {
    setFilter(next);
    setPref({ key: PreferenceKeys.auditFilter,   value: next });
    setPref({ key: PreferenceKeys.auditPageSize,  value: next.pageSize });
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Audit</h1>
      <Tabs defaultValue="log">
        <TabsList>
          <TabsTrigger value="log">Log</TabsTrigger>
          <TabsTrigger value="insights">Insights</TabsTrigger>
        </TabsList>
        <TabsContent value="log">
          <div className="flex flex-col gap-4">
            <div className="flex items-center justify-between pt-2">
              <AuditFilters onFilter={handleFilterChange} />
              <ExportMenu
                resource="audit"
                currentData={allItems as unknown as Record<string, unknown>[]}
                queryParams={
                  filter as unknown as Record<string, string | number | boolean | undefined>
                }
                canExport={canExport}
              />
            </div>
            <AuditGrid
              data={allItems}
              hasMore={hasNextPage ?? false}
              isFetchingMore={isFetchingNextPage}
              onLoadMore={() => void fetchNextPage()}
              pageSize={filter.pageSize ?? DEFAULT_PAGE_SIZE}
            />
          </div>
        </TabsContent>
        <TabsContent value="insights">
          <AuditInsightsTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
