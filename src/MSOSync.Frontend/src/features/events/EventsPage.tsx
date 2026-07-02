import { useState, useRef, useEffect } from 'react';
import type { EventFilter } from '../../shared/types';
import { EventFilters } from './EventFilters';
import { EventsGrid } from './EventsGrid';
import { ExportMenu } from '../../shared/components/ExportMenu';
import { DEFAULT_PAGE_SIZE } from '../../shared/constants/query';
import { useEvents } from './hooks';
import { usePreference, useSetPreference } from '../../shared/hooks/usePreferences';
import { PreferenceKeys } from '../../shared/types/preferences';
import { useHasPermission } from '../../shared/hooks/usePermissions';
import { PermissionKeys } from '../../shared/types/permissions';

export function EventsPage() {
  const savedFilter   = usePreference<EventFilter>(PreferenceKeys.eventsFilter,   { page: 1, pageSize: DEFAULT_PAGE_SIZE });
  const savedPageSize = usePreference<number>     (PreferenceKeys.eventsPageSize,  DEFAULT_PAGE_SIZE);
  const { mutate: setPref } = useSetPreference();

  const [filter, setFilter] = useState<EventFilter>({ page: 1, pageSize: savedPageSize });
  const prefsApplied = useRef(false);
  useEffect(() => {
    if (!prefsApplied.current && savedFilter.page !== undefined) {
      setFilter({ ...savedFilter, page: 1 });
      prefsApplied.current = true;
    }
  }, [savedFilter]);

  const { data } = useEvents(filter);
  const canExport = useHasPermission(PermissionKeys.ExportData);

  function handleFilterChange(next: EventFilter) {
    setFilter(next);
    const { page: _page, ...filterToSave } = next;
    setPref({ key: PreferenceKeys.eventsFilter,   value: filterToSave });
    setPref({ key: PreferenceKeys.eventsPageSize,  value: next.pageSize });
  }

  return (
    <div className="flex flex-col gap-4 p-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-semibold">Events</h1>
        <ExportMenu
          resource="events"
          currentData={(data?.data ?? []) as unknown as Record<string, unknown>[]}
          queryParams={filter as unknown as Record<string, string | number | boolean | undefined>}
          canExport={canExport}
        />
      </div>
      <EventFilters onFilter={handleFilterChange} />
      <EventsGrid filter={filter} onFilterChange={handleFilterChange} />
    </div>
  );
}
