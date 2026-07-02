import { useState, useRef, useEffect } from 'react';
import type { EventFilter } from '../../shared/types';
import type { CursorEventFilter } from '../../shared/api/events';
import { EventFilters } from './EventFilters';
import { EventsGrid } from './EventsGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useInfiniteEvents } from '../../shared/hooks/useInfiniteEvents';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

export function EventsPage() {
  const savedFilter   = usePreference<Omit<EventFilter, 'page'>>(PreferenceKeys.eventsFilter,   { pageSize: DEFAULT_PAGE_SIZE });
  const savedPageSize = usePreference<number>                    (PreferenceKeys.eventsPageSize,  DEFAULT_PAGE_SIZE);
  const { mutate: setPref } = useSetPreference();

  const [filter, setFilter] = useState<CursorEventFilter>({ pageSize: savedPageSize });
  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current && savedFilter.pageSize !== undefined) {
      const { page: _page, ...rest } = savedFilter as EventFilter;
      setFilter({ ...rest, pageSize: rest.pageSize ?? DEFAULT_PAGE_SIZE });
      prefsApplied.current = true;
    }
  }, [savedFilter]);

  const { data, fetchNextPage, hasNextPage, isFetchingNextPage } = useInfiniteEvents(filter);
  const canExport = useHasPermission(PermissionKeys.ExportData);

  const allItems = data?.pages.flatMap(p => p.items) ?? [];

  function handleFilterChange(next: CursorEventFilter) {
    setFilter(next);
    setPref({ key: PreferenceKeys.eventsFilter,   value: next });
    setPref({ key: PreferenceKeys.eventsPageSize,  value: next.pageSize });
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Events</h1>
        <ExportMenu
          resource="events"
          currentData={allItems as unknown as Record<string, unknown>[]}
          queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
          canExport={canExport}
        />
      </div>
      <EventFilters onFilter={handleFilterChange} />
      <EventsGrid
        data={allItems}
        hasMore={hasNextPage ?? false}
        isFetchingMore={isFetchingNextPage}
        onLoadMore={() => void fetchNextPage()}
        pageSize={filter.pageSize ?? DEFAULT_PAGE_SIZE}
      />
    </div>
  );
}
