import { useState, useRef, useEffect } from 'react';
import type { CursorAuditFilter } from '../../shared/api/audit';
import {
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
} from '../../components/ui/tabs';
import { AuditFilterBar, type AuditFilterState } from './components/AuditFilterBar';
import { SavedFiltersPanel } from './components/SavedFiltersPanel';
import { EntityHistoryTab } from './components/EntityHistoryTab';
import { AuditGrid } from './AuditGrid';
import { AuditInsightsTab } from './AuditInsightsTab';
import { CorrelationTimeline } from '../../shared/components/CorrelationTimeline';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useInfiniteAudit } from '../../shared/hooks/useInfiniteAudit';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

interface SavedFilter { name: string; filter: AuditFilterState; }

const SAVED_FILTER_PREF_KEY = 'audit.savedFilters';

const KNOWN_AUDIT_ACTIONS = [
  'NODE_APPROVED',
  'NODE_DISABLED',
  'NODE_DECOMMISSIONED',
  'NODE_HEARTBEAT',
  'NODE_SYNC_START',
  'NODE_SYNC_COMPLETE',
  'NODE_CONFIG_CHANGED',
  'USER_LOGIN',
  'USER_LOGOUT',
  'USER_CREATED',
  'USER_DELETED',
  'ROLE_ASSIGNED',
  'ROLE_REVOKED',
];

const EMPTY_FILTER_STATE: AuditFilterState = {
  usernames: [], actionNames: [], objectNames: [], from: '', to: '',
};

function filterStateToCursorFilter(state: AuditFilterState): CursorAuditFilter {
  return {
    usernames:   state.usernames.length   > 0 ? state.usernames   : undefined,
    actionNames: state.actionNames.length > 0 ? state.actionNames : undefined,
    objectNames: state.objectNames.length > 0 ? state.objectNames : undefined,
    from:        state.from  || undefined,
    to:          state.to    || undefined,
    pageSize:    DEFAULT_PAGE_SIZE,
  };
}

export function AuditPage() {
  const savedPageSize = usePreference<number>(PreferenceKeys.auditPageSize, DEFAULT_PAGE_SIZE);
  const savedFiltersRaw = usePreference<SavedFilter[]>(SAVED_FILTER_PREF_KEY, []);
  const { mutate: setPref } = useSetPreference();

  const [filterState, setFilterState] = useState<AuditFilterState>(EMPTY_FILTER_STATE);
  const [filter, setFilter]           = useState<CursorAuditFilter>({ pageSize: savedPageSize });
  const [savedFilters, setSavedFilters] = useState<SavedFilter[]>([]);

  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current) {
      if (savedPageSize !== undefined) {
        setFilter(f => ({ ...f, pageSize: savedPageSize }));
      }
      if (savedFiltersRaw && savedFiltersRaw.length > 0) {
        setSavedFilters(savedFiltersRaw);
      }
      prefsApplied.current = true;
    }
  }, [savedPageSize, savedFiltersRaw]);

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = useInfiniteAudit(filter);
  const canExport = useHasPermission(PermissionKeys.ExportData);

  const allItems = data?.pages.flatMap(p => p.items) ?? [];

  function handleFilterChange(next: AuditFilterState) {
    setFilterState(next);
    const cursorFilter = filterStateToCursorFilter(next);
    setFilter(cursorFilter);
    setPref({ key: PreferenceKeys.auditPageSize, value: cursorFilter.pageSize });
  }

  function handleSaveFilter(name: string) {
    const updated = [...savedFilters.filter(f => f.name !== name), { name, filter: filterState }];
    setSavedFilters(updated);
    setPref({ key: SAVED_FILTER_PREF_KEY, value: updated });
  }

  function handleDeleteFilter(name: string) {
    const updated = savedFilters.filter(f => f.name !== name);
    setSavedFilters(updated);
    setPref({ key: SAVED_FILTER_PREF_KEY, value: updated });
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <h1 className="text-2xl font-semibold">Audit</h1>
      <Tabs defaultValue="log">
        <TabsList>
          <TabsTrigger value="log">Log</TabsTrigger>
          <TabsTrigger value="insights">Insights</TabsTrigger>
          <TabsTrigger value="correlation">Correlation</TabsTrigger>
          <TabsTrigger value="entity-history">Entity History</TabsTrigger>
        </TabsList>
        <TabsContent value="log">
          <div className="flex flex-col gap-4">
            <div className="pt-2">
              <AuditFilterBar
                value={filterState}
                onChange={handleFilterChange}
                onSave={handleSaveFilter}
                knownActions={KNOWN_AUDIT_ACTIONS}
              />
            </div>
            {savedFilters.length > 0 && (
              <div className="rounded-lg border bg-card p-2">
                <p className="text-xs font-medium text-muted-foreground px-2 pb-1">Saved Filters</p>
                <SavedFiltersPanel
                  filters={savedFilters}
                  onLoad={handleFilterChange}
                  onDelete={handleDeleteFilter}
                />
              </div>
            )}
            <div className="flex items-center justify-end">
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
        <TabsContent value="correlation" className="mt-4">
          <CorrelationTimeline />
        </TabsContent>
        <TabsContent value="entity-history" className="mt-4">
          <EntityHistoryTab />
        </TabsContent>
      </Tabs>
    </div>
  );
}
