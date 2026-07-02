import { useState, useRef, useEffect } from 'react';
import type { AuditFilter } from '../../shared/types';
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
import { useAuditLog } from './hooks';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

export function AuditPage() {
  const savedFilter   = usePreference<AuditFilter>(PreferenceKeys.auditFilter,   { page: 1, pageSize: DEFAULT_PAGE_SIZE });
  const savedPageSize = usePreference<number>      (PreferenceKeys.auditPageSize,  DEFAULT_PAGE_SIZE);
  const { mutate: setPref } = useSetPreference();

  const [filter, setFilter] = useState<AuditFilter>({ page: 1, pageSize: savedPageSize });
  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current && savedFilter.page !== undefined) {
      setFilter({ ...savedFilter, page: 1 });
      prefsApplied.current = true;
    }
  }, [savedFilter]);

  const { data } = useAuditLog(filter); // cache-shared with AuditGrid
  const canExport = useHasPermission(PermissionKeys.ExportData);

  function handleFilterChange(next: AuditFilter) {
    setFilter(next);
    const { page: _page, ...filterToSave } = next;
    setPref({ key: PreferenceKeys.auditFilter,   value: filterToSave });
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
                currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
                queryParams={
                  filter as unknown as Record<string, string | number | boolean | undefined>
                }
                canExport={canExport}
              />
            </div>
            <AuditGrid filter={filter} onFilterChange={handleFilterChange} />
          </div>
        </TabsContent>
        <TabsContent value="insights">
          <AuditInsightsTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
